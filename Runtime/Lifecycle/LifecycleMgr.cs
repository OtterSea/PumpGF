using System;
using System.Threading;
using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;

namespace PumpGF
{
    /// <summary>
    /// 场景生命周期监听接口。实现后注册到 LifecycleMgr，按优先级顺序调用。
    /// </summary>
    public interface ISceneLifecycle
    {
        /// <summary>场景激活后调用</summary>
        void OnSceneEnter(Scene scene);
        /// <summary>场景卸载前调用</summary>
        void OnSceneExit(Scene scene);
    }

    /// <summary>
    /// 游戏运行节奏的中枢调度器。统一驱动 Update/FixedUpdate/LateUpdate，
    /// 按 UpdateChannel 分组派发，支持固定步长累加器、暂停系统、时间缩放。
    /// </summary>
    public sealed class LifecycleMgr : IModule
    {
        // ── 通道状态 ──
        private struct ChannelState
        {
            public float TimeScale;
            public float Accumulator;
            public float InterpolationAlpha;
            public float DeltaTime;
            public float FixedDt; // > 0 表示固定步长通道
        }

        private readonly Dictionary<UpdateChannel, ChannelState> _channelStates = new();
        private readonly Dictionary<UpdateChannel, Subject<float>> _updateSubjects = new();
        private readonly Dictionary<UpdateChannel, List<Action<float>>> _tickCallbacks = new();
        private readonly Subject<float> _fixedUpdateSubject = new();
        private readonly Subject<float> _lateUpdateSubject = new();

        // ── 暂停栈 ──
        private struct PauseEntry
        {
            public string Name;
            public UpdateChannel PausedChannels;
        }
        private readonly List<PauseEntry> _pauseStack = new();
        private UpdateChannel _frozenMask;

        // ── 场景钩子 ──
        private readonly Subject<Scene> _onSceneLoaded = new();
        private readonly Subject<Scene> _onSceneUnloaded = new();
        private readonly List<(ISceneLifecycle Listener, int Priority)> _sceneLifecycles = new();

        // ── 应用生命周期 ──
        private readonly Subject<bool> _onAppFocusChanged = new();
        private readonly Subject<bool> _onAppPauseChanged = new();
        private readonly Subject<Unit> _onAppQuit = new();

        // ── CTS ──
        private CancellationTokenSource _appLifetimeCts;

        // ── 配置 ──
        private int _maxCatchUp = 5;

        public void Init()
        {
            _appLifetimeCts = new CancellationTokenSource();

            // 默认固定步长通道：Logic 60Hz
            SetChannelFixedStep(UpdateChannel.Logic, 60);
            SetChannelTimeScale(UpdateChannel.Logic, 1f);
            // 变量步长通道默认 TimeScale = 1
            foreach (UpdateChannel ch in Enum.GetValues(typeof(UpdateChannel)))
            {
                if (!_channelStates.ContainsKey(ch))
                {
                    _channelStates[ch] = new ChannelState { TimeScale = 1f, FixedDt = 0f };
                }
            }

            SceneManager.sceneLoaded += OnSceneLoadedInternal;
            SceneManager.sceneUnloaded += OnSceneUnloadedInternal;

            // 创建隐藏的 MonoBehaviour 驱动节点
            LifecycleDriver.Create(this);
        }

        // ──────────────────────────────────────────────
        //  更新派发（R3 Observable + RegisterTick）
        // ──────────────────────────────────────────────

        /// <summary>获取指定通道的 R3 Observable（享受操作符能力）</summary>
        public Observable<float> GetUpdateObservable(UpdateChannel channel)
        {
            if (!_updateSubjects.TryGetValue(channel, out var subject))
            {
                subject = new Subject<float>();
                _updateSubjects[channel] = subject;
            }
            return subject;
        }

        /// <summary>Unity 原生 FixedUpdate Observable</summary>
        public Observable<float> FixedUpdateObservable => _fixedUpdateSubject;

        /// <summary>Unity 原生 LateUpdate Observable</summary>
        public Observable<float> LateUpdateObservable => _lateUpdateSubject;

        /// <summary>零分配热路径回调注册。返回 IDisposable，Dispose 即取消。</summary>
        public IDisposable RegisterTick(UpdateChannel channel, Action<float> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (!_tickCallbacks.TryGetValue(channel, out var list))
            {
                list = new List<Action<float>>();
                _tickCallbacks[channel] = list;
            }
            list.Add(callback);
            return new TickUnregister(this, channel, callback);
        }

        private void UnregisterTick(UpdateChannel channel, Action<float> callback)
        {
            if (_tickCallbacks.TryGetValue(channel, out var list))
            {
                list.Remove(callback);
            }
        }

        // ──────────────────────────────────────────────
        //  暂停系统
        // ──────────────────────────────────────────────

        /// <summary>压入暂停 Profile（直接传实例，不依赖 ResMgr）</summary>
        public void PushPause(PauseProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            _pauseStack.Add(new PauseEntry
            {
                Name = profile.ProfileName,
                PausedChannels = profile.PausedChannels
            });
            RecalculateFrozenMask();
            Log.Info("Lifecycle", $"PushPause: {profile.ProfileName}（冻结: {profile.PausedChannels}）");
        }

        /// <summary>弹出指定名称的暂停 Profile</summary>
        public void PopPause(string profileName)
        {
            for (int i = _pauseStack.Count - 1; i >= 0; i--)
            {
                if (_pauseStack[i].Name == profileName)
                {
                    _pauseStack.RemoveAt(i);
                    RecalculateFrozenMask();
                    Log.Info("Lifecycle", $"PopPause: {profileName}");
                    return;
                }
            }
            Log.Warning("Lifecycle", $"PopPause 失败：未找到 Profile '{profileName}'");
        }

        /// <summary>指定 Profile 是否在暂停栈中</summary>
        public bool IsPauseActive(string profileName)
        {
            foreach (var entry in _pauseStack)
            {
                if (entry.Name == profileName) return true;
            }
            return false;
        }

        /// <summary>指定通道是否被暂停</summary>
        public bool IsChannelPaused(UpdateChannel channel)
        {
            return (_frozenMask & channel) != 0;
        }

        /// <summary>通道暂停状态变化流</summary>
        public Observable<bool> ObserveChannelPaused(UpdateChannel channel)
        {
            // TODO: 优化为专门的 Subject 跟踪暂停状态变化
            // 当前简化：返回 GetUpdateObservable 的派生（dt == 0 表示暂停）
            return GetUpdateObservable(channel).Select(dt => dt > 0f).DistinctUntilChanged();
        }

        private void RecalculateFrozenMask()
        {
            _frozenMask = 0;
            foreach (var entry in _pauseStack)
            {
                _frozenMask |= entry.PausedChannels;
            }
        }

        // ──────────────────────────────────────────────
        //  时间缩放
        // ──────────────────────────────────────────────

        /// <summary>设置通道时间缩放（与暂停解耦）</summary>
        public void SetChannelTimeScale(UpdateChannel channel, float scale)
        {
            var state = _channelStates.TryGetValue(channel, out var s)
                ? s
                : new ChannelState { FixedDt = 0f };
            state.TimeScale = scale;
            _channelStates[channel] = state;
        }

        /// <summary>获取通道时间缩放</summary>
        public float GetTimeScale(UpdateChannel channel)
        {
            return _channelStates.TryGetValue(channel, out var s) ? s.TimeScale : 1f;
        }

        // ──────────────────────────────────────────────
        //  时间查询
        // ──────────────────────────────────────────────

        /// <summary>指定通道本帧有效增量时间</summary>
        public float GetDeltaTime(UpdateChannel channel)
        {
            return _channelStates.TryGetValue(channel, out var s) ? s.DeltaTime : 0f;
        }

        /// <summary>指定固定步长通道的步长</summary>
        public float GetFixedDeltaTime(UpdateChannel channel)
        {
            return _channelStates.TryGetValue(channel, out var s) ? s.FixedDt : 0f;
        }

        /// <summary>指定通道的插值因子（0~1，固定步长通道）</summary>
        public float GetInterpolationAlpha(UpdateChannel channel)
        {
            return _channelStates.TryGetValue(channel, out var s) ? s.InterpolationAlpha : 0f;
        }

        /// <summary>原始 unscaledDeltaTime</summary>
        public float GetUnscaledDeltaTime() => Time.unscaledDeltaTime;

        // ──────────────────────────────────────────────
        //  场景钩子
        // ──────────────────────────────────────────────

        public Observable<Scene> OnSceneLoaded => _onSceneLoaded;
        public Observable<Scene> OnSceneUnloaded => _onSceneUnloaded;

        /// <summary>注册场景生命周期监听器（按优先级排序，高优先）</summary>
        public void RegisterSceneLifecycle(ISceneLifecycle listener, int priority = 0)
        {
            _sceneLifecycles.Add((listener, priority));
            _sceneLifecycles.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        /// <summary>注销场景生命周期监听器</summary>
        public void UnregisterSceneLifecycle(ISceneLifecycle listener)
        {
            _sceneLifecycles.RemoveAll(x => x.Listener == listener);
        }

        // ──────────────────────────────────────────────
        //  应用生命周期
        // ──────────────────────────────────────────────

        public Observable<bool> OnApplicationFocusChanged => _onAppFocusChanged;
        public Observable<bool> OnApplicationPauseChanged => _onAppPauseChanged;
        public Observable<Unit> OnApplicationQuit => _onAppQuit;

        // ──────────────────────────────────────────────
        //  CTS 工厂
        // ──────────────────────────────────────────────

        /// <summary>绑定 GameObject 生命周期的 Token</summary>
        public CancellationToken CreateLinkedToken(GameObject owner)
        {
            return owner != null ? owner.GetCancellationTokenOnDestroy() : CancellationToken.None;
        }

        /// <summary>超时 Token</summary>
        public CancellationToken CreateTimeoutToken(TimeSpan timeout)
        {
            var cts = new CancellationTokenSource(timeout);
            return cts.Token;
        }

        /// <summary>链接多个 Token</summary>
        public CancellationToken CreateLinkedToken(params CancellationToken[] tokens)
        {
            if (tokens == null || tokens.Length == 0) return CancellationToken.None;
            if (tokens.Length == 1) return tokens[0];
            return CancellationTokenSource.CreateLinkedTokenSource(tokens).Token;
        }

        /// <summary>应用生命周期 Token</summary>
        public CancellationToken CreateAppLifetimeToken()
        {
            return _appLifetimeCts.Token;
        }

        // ──────────────────────────────────────────────
        //  内部：Tick（由 LifecycleDriver 调用）
        // ──────────────────────────────────────────────

        internal void OnUpdate(float rawDeltaTime)
        {
            TickChannel(UpdateChannel.Default, rawDeltaTime);
            TickChannel(UpdateChannel.Logic, rawDeltaTime);
            TickChannel(UpdateChannel.Animation, rawDeltaTime);
            TickChannel(UpdateChannel.UI, rawDeltaTime);
            TickChannel(UpdateChannel.Effect, rawDeltaTime);
            TickChannel(UpdateChannel.Input, rawDeltaTime);
        }

        internal void OnFixedUpdate(float fixedDeltaTime)
        {
            _fixedUpdateSubject.OnNext(fixedDeltaTime);
        }

        internal void OnLateUpdate()
        {
            _lateUpdateSubject.OnNext(Time.deltaTime);
        }

        internal void NotifyApplicationFocus(bool hasFocus)
        {
            _onAppFocusChanged.OnNext(hasFocus);
        }

        internal void NotifyApplicationPause(bool pauseStatus)
        {
            _onAppPauseChanged.OnNext(pauseStatus);
        }

        internal void NotifyApplicationQuit()
        {
            _onAppQuit.OnNext(Unit.Default);
        }

        private void TickChannel(UpdateChannel channel, float rawDeltaTime)
        {
            if (!_channelStates.TryGetValue(channel, out var state))
            {
                state = new ChannelState { TimeScale = 1f, FixedDt = 0f };
            }

            bool isPaused = (_frozenMask & channel) != 0;
            float effectiveDt = isPaused ? 0f : rawDeltaTime * state.TimeScale;
            state.DeltaTime = effectiveDt;

            if (state.FixedDt > 0f)
            {
                // 固定步长累加器
                state.Accumulator += effectiveDt;
                int count = 0;
                while (state.Accumulator >= state.FixedDt && count < _maxCatchUp)
                {
                    DispatchChannel(channel, state.FixedDt);
                    state.Accumulator -= state.FixedDt;
                    count++;
                }
                // 超出 MaxCatchUp 的余量丢弃（防死亡螺旋）
                if (state.Accumulator >= state.FixedDt)
                {
                    state.Accumulator = 0f;
                }
                state.InterpolationAlpha = state.FixedDt > 0f
                    ? state.Accumulator / state.FixedDt
                    : 0f;
            }
            else
            {
                // 变量步长
                DispatchChannel(channel, effectiveDt);
            }

            _channelStates[channel] = state;
        }

        private void DispatchChannel(UpdateChannel channel, float dt)
        {
            if (_updateSubjects.TryGetValue(channel, out var subject))
            {
                subject.OnNext(dt);
            }
            if (_tickCallbacks.TryGetValue(channel, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    list[i]?.Invoke(dt);
                }
            }
        }

        private void SetChannelFixedStep(UpdateChannel channel, int tickRate)
        {
            var state = _channelStates.TryGetValue(channel, out var s)
                ? s
                : new ChannelState { TimeScale = 1f };
            state.FixedDt = 1f / tickRate;
            _channelStates[channel] = state;
        }

        private void OnSceneLoadedInternal(Scene scene, LoadSceneMode mode)
        {
            _onSceneLoaded.OnNext(scene);
            foreach (var entry in _sceneLifecycles)
            {
                try { entry.Listener.OnSceneEnter(scene); }
                catch (Exception e) { Log.Error("Lifecycle", $"OnSceneEnter 异常: {e}"); }
            }
        }

        private void OnSceneUnloadedInternal(Scene scene)
        {
            _onSceneUnloaded.OnNext(scene);
            foreach (var entry in _sceneLifecycles)
            {
                try { entry.Listener.OnSceneExit(scene); }
                catch (Exception e) { Log.Error("Lifecycle", $"OnSceneExit 异常: {e}"); }
            }
        }

        public void Dispose()
        {
            SceneManager.sceneLoaded -= OnSceneLoadedInternal;
            SceneManager.sceneUnloaded -= OnSceneUnloadedInternal;

            _appLifetimeCts?.Cancel();
            _appLifetimeCts?.Dispose();
            _appLifetimeCts = null;

            foreach (var kvp in _updateSubjects)
                kvp.Value.Dispose();
            _updateSubjects.Clear();

            _fixedUpdateSubject.Dispose();
            _lateUpdateSubject.Dispose();
            _onSceneLoaded.Dispose();
            _onSceneUnloaded.Dispose();
            _onAppFocusChanged.Dispose();
            _onAppPauseChanged.Dispose();
            _onAppQuit.Dispose();

            _tickCallbacks.Clear();
            _channelStates.Clear();
            _pauseStack.Clear();
            _sceneLifecycles.Clear();
            _frozenMask = 0;
        }

        // ── Tick 取消注册的 IDisposable ──
        private sealed class TickUnregister : IDisposable
        {
            private readonly LifecycleMgr _mgr;
            private readonly UpdateChannel _channel;
            private readonly Action<float> _callback;
            private bool _disposed;

            public TickUnregister(LifecycleMgr mgr, UpdateChannel channel, Action<float> callback)
            {
                _mgr = mgr;
                _channel = channel;
                _callback = callback;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _mgr.UnregisterTick(_channel, _callback);
            }
        }
    }
}
