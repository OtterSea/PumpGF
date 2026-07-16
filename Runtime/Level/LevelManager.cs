using System;
using System.Threading;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace PumpGF
{
    /// <summary>
    /// 关卡与场景加载的高级流程编排器。在 ResMgr 场景原语之上，提供 ILevel 标准化入口、
    /// SceneTransition 过渡、数据注入、Additive Loading。
    /// </summary>
    /// <remarks>
    /// <b>初始化注意</b>：<see cref="Init"/> 不注册关卡，业务需启动时调用 <see cref="RegisterLevel"/>。
    /// </remarks>
    public sealed class LevelManager : IModule
    {
        private ResMgr _resMgr;
        private LifecycleMgr _lifecycle;

        private readonly Dictionary<string, Func<ILevel>> _levelFactories = new(8);
        private readonly List<SceneHandle> _additiveScenes = new(8);

        private ILevel _currentLevel;
        private string _currentLevelKey;
        private IDisposable _updateSub;
        private bool _isLoading;
        // 缓存 Tick 委托，避免每次 LoadLevelAsync 分配闭包
        private Action<float> _levelTickDelegate;

        public void Init()
        {
            _resMgr = GameGlobal.ResMgr;
            _lifecycle = GameGlobal.LifecycleMgr;
            _levelTickDelegate = OnCurrentLevelTick;
        }

        public void Dispose()
        {
            _updateSub?.Dispose();
            _updateSub = null;

            if (_currentLevel != null)
            {
                // 同步 Forget（不 await 但显式声明 fire-and-forget，异常上抛给 UniTaskScheduler）
                // 说明：Dispose 阶段（Application.quitting）无法 await 异步，业务需要持久化的应用
                // 应挂 Application.wantsToQuit 或在 LevelManager.UnloadCurrentLevelAsync 里手动 await。
                SafeExitFireAndForget(_currentLevel).Forget();
                _currentLevel = null;
                _currentLevelKey = null;
            }

            // 兜底卸载所有跟踪的场景句柄
            for (int i = 0; i < _additiveScenes.Count; i++)
            {
                _additiveScenes[i]?.Dispose();
            }
            _additiveScenes.Clear();
            _levelFactories.Clear();
            _isLoading = false;
        }

        private static async UniTaskVoid SafeExitFireAndForget(ILevel level)
        {
            try
            {
                await level.OnExitAsync(default);
            }
            catch (OperationCanceledException) { /* 应用退出正常取消，吞掉 */ }
            catch (Exception e)
            {
                Log.Warning("LevelManager", $"Dispose 阶段关卡 OnExit 异常（已忽略）: {e.Message}");
            }
        }

        // ──────────────────────────────────────────────
        //  关卡注册
        // ──────────────────────────────────────────────

        /// <summary>注册关卡（key → 工厂）</summary>
        public void RegisterLevel(string key, Func<ILevel> factory)
        {
            if (_levelFactories.ContainsKey(key))
                Log.Warning("LevelManager", $"关卡 '{key}' 已注册，覆盖。");
            _levelFactories[key] = factory;
        }

        /// <summary>注销关卡</summary>
        public void UnregisterLevel(string key) => _levelFactories.Remove(key);

        /// <summary>是否注册了指定关卡</summary>
        public bool HasLevel(string key) => _levelFactories.ContainsKey(key);

        // ──────────────────────────────────────────────
        //  关卡加载
        // ──────────────────────────────────────────────

        /// <summary>
        /// 加载关卡。流程：过渡遮罩 → 退出当前关卡 → 进入新关卡（场景加载）→ 绑定 Update → 揭开过渡。
        /// </summary>
        public async UniTask LoadLevelAsync(
            string key,
            ILevelData data = null,
            SceneTransition transition = null,
            IProgress<float> progress = null,
            UpdateChannel updateChannel = UpdateChannel.Logic,
            CancellationToken ct = default)
        {
            if (_isLoading)
            {
                Log.Warning("LevelManager", $"正在加载关卡，忽略重复请求 '{key}'。");
                return;
            }
            if (!_levelFactories.TryGetValue(key, out var factory))
            {
                Log.Error("LevelManager", $"未注册关卡 '{key}'。");
                return;
            }

            _isLoading = true;
            try
            {
                var level = factory();

                if (transition != null)
                    await transition.PlayFadeOut(progress, ct);

                // 退出当前关卡
                if (_currentLevel != null)
                {
                    _updateSub?.Dispose();
                    _updateSub = null;
                    await SafeExitAsync(_currentLevel, ct);
                    _currentLevel = null;
                    _currentLevelKey = null;
                }

                // 进入新关卡
                await level.OnEnterAsync(data, ct);
                _currentLevel = level;
                _currentLevelKey = key;

                // 绑定 Update（使用缓存委托，避免闭包分配）
                _updateSub = _lifecycle.RegisterTick(updateChannel, _levelTickDelegate);

                if (transition != null)
                    await transition.PlayFadeIn(ct);
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>卸载当前关卡</summary>
        public async UniTask UnloadCurrentLevelAsync(
            SceneTransition transition = null, CancellationToken ct = default)
        {
            if (_currentLevel == null) return;
            if (_isLoading) { Log.Warning("LevelManager", "正在加载，无法卸载。"); return; }
            _isLoading = true;
            try
            {
                if (transition != null)
                    await transition.PlayFadeOut(null, ct);

                _updateSub?.Dispose();
                _updateSub = null;
                await SafeExitAsync(_currentLevel, ct);
                _currentLevel = null;
                _currentLevelKey = null;

                if (transition != null)
                    await transition.PlayFadeIn(ct);
            }
            finally
            {
                _isLoading = false;
            }
        }

        // ──────────────────────────────────────────────
        //  场景加载（Additive）
        // ──────────────────────────────────────────────

        /// <summary>加载场景（委托 ResMgr）。返回 SceneHandle 供 ILevel 持有。</summary>
        public async UniTask<SceneHandle> LoadSceneAsync(
            string sceneKey,
            LoadSceneMode mode = LoadSceneMode.Single,
            IProgress<float> progress = null,
            CancellationToken ct = default)
        {
            var handle = await _resMgr.LoadSceneAsync(sceneKey, mode, true, progress, ct);
            _additiveScenes.Add(handle);
            return handle;
        }

        /// <summary>卸载场景</summary>
        public async UniTask UnloadSceneAsync(SceneHandle handle, CancellationToken ct = default)
        {
            if (handle == null) return;
            _additiveScenes.Remove(handle);
            await handle.UnloadAsync(ct);
        }

        // ──────────────────────────────────────────────
        //  查询
        // ──────────────────────────────────────────────

        /// <summary>当前关卡</summary>
        public ILevel CurrentLevel => _currentLevel;

        /// <summary>当前关卡 key</summary>
        public string CurrentLevelKey => _currentLevelKey;

        /// <summary>是否正在加载</summary>
        public bool IsLoading => _isLoading;

        // ──────────────────────────────────────────────
        //  内部
        // ──────────────────────────────────────────────

        private async UniTask SafeExitAsync(ILevel level, CancellationToken ct)
        {
            try
            {
                await level.OnExitAsync(ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                Log.Error("LevelManager", $"关卡退出异常: {e}");
            }
        }

        // 缓存的 Tick 转发函数，安全处理关卡切换瞬间 _currentLevel 变化
        private void OnCurrentLevelTick(float dt)
        {
            var level = _currentLevel;
            if (level != null) level.OnUpdate(dt);
        }
    }
}
