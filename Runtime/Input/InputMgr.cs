using System;
using System.Threading;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PumpGF
{
    /// <summary>
    /// 游戏输入统一管理与分发中枢。基于 New Input System，栈式 InputContext 管理输入上下文切换，
    /// R3 暴露输入事件，提供输入缓冲与按键重映射。
    /// <para>约定：<see cref="InputContext"/> 枚举名 = .inputactions 中对应 ActionMap 名（如 Battle→"Battle"）。</para>
    /// </summary>
    /// <remarks>
    /// <b>初始化注意</b>：<see cref="Init"/> 为同步无法加载 .inputactions 资产。业务应在启动时调用
    /// <see cref="SetInputActionAsset"/> 或 <see cref="LoadInputActionAssetAsync"/> 注入 InputActionAsset。
    /// <para>Init 会自动向 <see cref="UIManager"/> 注册 InputContext 联动钩子，使 <see cref="PageConfig.InputContext"/> 自动切换。</para>
    /// </remarks>
    public sealed class InputMgr : IModule
    {
        private InputActionAsset _asset;
        private AssetHandle<InputActionAsset> _assetHandle;
        private InputActionMap _globalMap;

        // 上下文栈
        private readonly List<InputContext> _contextStack = new(8);

        // R3 离散事件缓存
        private readonly Dictionary<string, Subject<InputAction.CallbackContext>> _performedSubjects = new(16);
        private readonly Dictionary<string, Subject<InputAction.CallbackContext>> _startedSubjects = new(16);
        private readonly Dictionary<string, Subject<InputAction.CallbackContext>> _canceledSubjects = new(16);

        // R3 连续值缓存
        private readonly Dictionary<string, ReadOnlyReactiveProperty<bool>> _buttonRO = new(16);
        private readonly Dictionary<string, ReadOnlyReactiveProperty<Vector2>> _axisRO = new(16);
        private readonly Dictionary<string, ReadOnlyReactiveProperty<float>> _triggerRO = new(16);

        // 输入缓冲
        private InputBuffer _buffer;
        private bool _autoBuffer;

        // 重映射
        private InputActionRebindingExtensions.RebindingOperation _rebindOp;

        // ──────────────────────────────────────────────
        //  IModule
        // ──────────────────────────────────────────────

        public void Init()
        {
            _buffer = new InputBuffer(0.2f);

            // 注册 UIManager 联动钩子（PageConfig.InputContext 自动切换）
            GameGlobal.UIManager?.SetInputContextHooks(Push, Pop);
        }

        public void Dispose()
        {
            CancelRebind();

            _contextStack.Clear();
            _globalMap = null;

            foreach (var kvp in _performedSubjects) kvp.Value.Dispose();
            foreach (var kvp in _startedSubjects) kvp.Value.Dispose();
            foreach (var kvp in _canceledSubjects) kvp.Value.Dispose();
            _performedSubjects.Clear();
            _startedSubjects.Clear();
            _canceledSubjects.Clear();

            foreach (var kvp in _buttonRO) kvp.Value.Dispose();
            foreach (var kvp in _axisRO) kvp.Value.Dispose();
            foreach (var kvp in _triggerRO) kvp.Value.Dispose();
            _buttonRO.Clear();
            _axisRO.Clear();
            _triggerRO.Clear();

            _buffer?.Clear();

            _assetHandle?.Dispose();
            _assetHandle = null;
            _asset = null;
        }

        // ──────────────────────────────────────────────
        //  资产注入
        // ──────────────────────────────────────────────

        /// <summary>注入 InputActionAsset（业务外部加载后传入）。注入后启用 Global ActionMap。</summary>
        public void SetInputActionAsset(InputActionAsset asset)
        {
            _asset = asset;
            if (_asset != null)
            {
                _globalMap = _asset.FindActionMap("Global");
                _globalMap?.Enable();
                Log.Info("InputMgr", $"InputActionAsset 注入，Global ActionMap 已启用。");
            }
        }

        /// <summary>通过 ResMgr 异步加载 InputActionAsset 并注入。持有 handle 至释放。</summary>
        public async UniTask LoadInputActionAssetAsync(string key, CancellationToken ct = default)
        {
            _assetHandle?.Dispose();
            _assetHandle = await GameGlobal.ResMgr.LoadAssetAsync<InputActionAsset>(key, ct: ct);
            SetInputActionAsset(_assetHandle.Asset);
        }

        // ──────────────────────────────────────────────
        //  上下文栈
        // ──────────────────────────────────────────────

        /// <summary>压入输入上下文。禁用当前栈顶 ActionMap，启用新上下文 ActionMap。</summary>
        public void Push(InputContext context)
        {
            if (context == InputContext.None)
            {
                // None 仅 Global
                if (_contextStack.Count > 0) DisableMap(_contextStack[_contextStack.Count - 1]);
                _contextStack.Add(context);
                _buffer.Clear();
                return;
            }
            if (_contextStack.Count > 0 && _contextStack[_contextStack.Count - 1] != InputContext.None)
                DisableMap(_contextStack[_contextStack.Count - 1]);
            _contextStack.Add(context);
            EnableMap(context);
            _buffer.Clear();
            Log.Info("InputMgr", $"Push InputContext: {context}");
        }

        /// <summary>弹出栈顶上下文，恢复下层 ActionMap。</summary>
        public void Pop()
        {
            if (_contextStack.Count == 0) return;
            var top = _contextStack[_contextStack.Count - 1];
            _contextStack.RemoveAt(_contextStack.Count - 1);
            if (top != InputContext.None) DisableMap(top);

            if (_contextStack.Count > 0)
            {
                var newTop = _contextStack[_contextStack.Count - 1];
                if (newTop != InputContext.None) EnableMap(newTop);
            }
            _buffer.Clear();
            Log.Info("InputMgr", $"Pop InputContext（恢复到 {CurrentContext}）");
        }

        /// <summary>清空上下文栈，仅保留 Global。</summary>
        public void PopAll()
        {
            foreach (var ctx in _contextStack)
            {
                if (ctx != InputContext.None) DisableMap(ctx);
            }
            _contextStack.Clear();
            _buffer.Clear();
        }

        /// <summary>当前上下文（栈顶，空栈返回 None）</summary>
        public InputContext CurrentContext =>
            _contextStack.Count > 0 ? _contextStack[_contextStack.Count - 1] : InputContext.None;

        /// <summary>上下文栈深度</summary>
        public int ContextStackDepth => _contextStack.Count;

        /// <summary>指定上下文是否在栈中</summary>
        public bool IsContextActive(InputContext context)
        {
            for (int i = 0; i < _contextStack.Count; i++)
                if (_contextStack[i] == context) return true;
            return false;
        }

        // ──────────────────────────────────────────────
        //  R3 暴露（离散输入）
        // ──────────────────────────────────────────────

        /// <summary>动作执行时触发（按键按下瞬间）</summary>
        public Observable<InputAction.CallbackContext> OnActionPerformed(string actionName)
        {
            return GetOrCreateSubject(actionName, _performedSubjects,
                (action, handler) => action.performed += handler);
        }

        /// <summary>动作开始</summary>
        public Observable<InputAction.CallbackContext> OnActionStarted(string actionName)
        {
            return GetOrCreateSubject(actionName, _startedSubjects,
                (action, handler) => action.started += handler);
        }

        /// <summary>动作取消（按键抬起）</summary>
        public Observable<InputAction.CallbackContext> OnActionCanceled(string actionName)
        {
            return GetOrCreateSubject(actionName, _canceledSubjects,
                (action, handler) => action.canceled += handler);
        }

        // ──────────────────────────────────────────────
        //  R3 暴露（连续输入）
        // ──────────────────────────────────────────────

        /// <summary>按钮状态（按下/抬起）</summary>
        public ReadOnlyReactiveProperty<bool> GetButtonValue(string actionName)
        {
            if (_buttonRO.TryGetValue(actionName, out var ro)) return ro;
            var action = FindAction(actionName);
            var prop = new ReactiveProperty<bool>(action != null && action.IsPressed());
            if (action != null)
            {
                action.performed += ctx => prop.Value = true;
                action.canceled += ctx => prop.Value = false;
            }
            else LogWarningMissing(actionName);
            var roProp = prop.ToReadOnlyReactiveProperty();
            _buttonRO[actionName] = roProp;
            return roProp;
        }

        /// <summary>2D 轴值（摇杆方向）</summary>
        public ReadOnlyReactiveProperty<Vector2> GetAxisValue(string actionName)
        {
            if (_axisRO.TryGetValue(actionName, out var ro)) return ro;
            var action = FindAction(actionName);
            var prop = new ReactiveProperty<Vector2>(action != null ? action.ReadValue<Vector2>() : Vector2.zero);
            if (action != null)
            {
                action.performed += ctx => prop.Value = ctx.ReadValue<Vector2>();
                action.canceled += ctx => prop.Value = Vector2.zero;
            }
            else LogWarningMissing(actionName);
            var roProp = prop.ToReadOnlyReactiveProperty();
            _axisRO[actionName] = roProp;
            return roProp;
        }

        /// <summary>1D 轴值（扳机）</summary>
        public ReadOnlyReactiveProperty<float> GetTriggerValue(string actionName)
        {
            if (_triggerRO.TryGetValue(actionName, out var ro)) return ro;
            var action = FindAction(actionName);
            var prop = new ReactiveProperty<float>(action != null ? action.ReadValue<float>() : 0f);
            if (action != null)
            {
                action.performed += ctx => prop.Value = ctx.ReadValue<float>();
                action.canceled += ctx => prop.Value = 0f;
            }
            else LogWarningMissing(actionName);
            var roProp = prop.ToReadOnlyReactiveProperty();
            _triggerRO[actionName] = roProp;
            return roProp;
        }

        // ──────────────────────────────────────────────
        //  输入缓冲
        // ──────────────────────────────────────────────

        /// <summary>缓冲一个输入（通常在 InputAction performed 时调用）</summary>
        public void BufferInput(string actionName) => _buffer.Buffer(actionName);

        /// <summary>消费缓冲的输入（动画状态机调用）。返回 true 表示有有效缓冲输入并已消费。</summary>
        public bool ConsumeInput(string actionName) => _buffer.Consume(actionName);

        /// <summary>清空所有缓冲</summary>
        public void ClearBuffer() => _buffer.Clear();

        /// <summary>设置缓冲时间窗口（秒）</summary>
        public void SetBufferWindow(float seconds) => _buffer.SetWindow(seconds);

        /// <summary>自动缓冲开关（开启后所有 performed 自动缓冲）</summary>
        public bool AutoBuffer
        {
            get => _autoBuffer;
            set => _autoBuffer = value;
        }

        // ──────────────────────────────────────────────
        //  重映射
        // ──────────────────────────────────────────────

        /// <summary>重映射指定动作的绑定。bindingIndex: 该动作第几个 binding（0=主绑定）。</summary>
        public void RemapBinding(string actionName, int bindingIndex, string newBindingPath)
        {
            var action = FindAction(actionName);
            if (action == null) return;
            action.ApplyBindingOverride(bindingIndex, newBindingPath);
            Log.Info("InputMgr", $"重映射 '{actionName}'[{bindingIndex}] → {newBindingPath}");
        }

        /// <summary>获取当前绑定路径</summary>
        public string GetBindingPath(string actionName, int bindingIndex)
        {
            var action = FindAction(actionName);
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count) return null;
            return action.bindings[bindingIndex].effectivePath;
        }

        /// <summary>重置指定动作的绑定到默认</summary>
        public void ResetBinding(string actionName)
        {
            var action = FindAction(actionName);
            action?.RemoveAllBindingOverrides();
        }

        /// <summary>重置所有绑定到默认</summary>
        public void ResetAllBindings()
        {
            if (_asset == null) return;
            foreach (var map in _asset.actionMaps)
            {
                foreach (var action in map.actions)
                    action.RemoveAllBindingOverrides();
            }
            Log.Info("InputMgr", "已重置所有输入绑定。");
        }

        /// <summary>
        /// 开始监听下一个输入设备用于重映射。返回 Observable，用户按下任意键时发出 bindingPath。
        /// </summary>
        public Observable<string> StartRebind(string actionName, int bindingIndex)
        {
            var subject = new Subject<string>();
            var action = FindAction(actionName);
            if (action == null)
            {
                LogWarningMissing(actionName);
                subject.OnCompleted();
                return subject;
            }
            CancelRebind();
            _rebindOp = action.PerformInteractiveRebinding(bindingIndex)
                .OnComplete(op =>
                {
                    subject.OnNext(action.bindings[bindingIndex].effectivePath);
                    subject.OnCompleted();
                })
                .OnCancel(op => subject.OnCompleted());
            _rebindOp.Start();
            return subject;
        }

        /// <summary>取消进行中的重映射监听</summary>
        public void CancelRebind()
        {
            if (_rebindOp != null)
            {
                _rebindOp.Dispose();
                _rebindOp = null;
            }
        }

        // ──────────────────────────────────────────────
        //  查询
        // ──────────────────────────────────────────────

        /// <summary>是否存在指定动作</summary>
        public bool HasAction(string actionName) => FindAction(actionName) != null;

        /// <summary>指定动作在当前上下文是否启用</summary>
        public bool IsActionEnabled(string actionName)
        {
            var action = FindAction(actionName);
            return action != null && action.enabled;
        }

        // ──────────────────────────────────────────────
        //  内部
        // ──────────────────────────────────────────────

        private InputAction FindAction(string actionName)
        {
            if (_asset == null || string.IsNullOrEmpty(actionName)) return null;
            return _asset.FindAction(actionName, throwIfNotFound: false);
        }

        private void EnableMap(InputContext context)
        {
            var map = _asset?.FindActionMap(context.ToString());
            if (map != null) map.Enable();
            else Log.Warning("InputMgr", $"未找到 ActionMap '{context}'（请确保 .inputactions 中有对应 ActionMap）。");
        }

        private void DisableMap(InputContext context)
        {
            var map = _asset?.FindActionMap(context.ToString());
            map?.Disable();
        }

        private Subject<InputAction.CallbackContext> GetOrCreateSubject(
            string actionName,
            Dictionary<string, Subject<InputAction.CallbackContext>> cache,
            Action<InputAction, Action<InputAction.CallbackContext>> register)
        {
            if (cache.TryGetValue(actionName, out var existing)) return existing;

            var subject = new Subject<InputAction.CallbackContext>();
            var action = FindAction(actionName);
            if (action != null)
            {
                register(action, ctx =>
                {
                    subject.OnNext(ctx);
                    if (_autoBuffer) BufferInput(actionName);
                });
            }
            else
            {
                LogWarningMissing(actionName);
            }
            cache[actionName] = subject;
            return subject;
        }

        private static void LogWarningMissing(string actionName)
        {
            Log.Warning("InputMgr", $"未找到输入动作 '{actionName}'（请确保与 .inputactions 定义一致）。");
        }
    }
}
