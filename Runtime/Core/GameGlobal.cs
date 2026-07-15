using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 全局服务定位器。纯 C# 静态类，不依赖 MonoBehaviour。
    /// 首次访问或 [RuntimeInitializeOnLoadMethod] 时自动初始化所有模块。
    /// 按依赖顺序初始化，逆序释放。
    /// </summary>
    public static class GameGlobal
    {
        static bool _initialized;

        /// <summary>生命周期管理模块（更新循环/暂停/时间缩放）</summary>
        public static LifecycleMgr LifecycleMgr { get; private set; }

        /// <summary>定时任务调度模块（延迟/周期/帧驱动，感知暂停与缩放）</summary>
        public static Scheduler Scheduler { get; private set; }

        /// <summary>对象池管理模块</summary>
        public static PoolMgr PoolMgr { get; private set; }

        /// <summary>资源加载模块</summary>
        public static ResMgr ResMgr { get; private set; }

        /// <summary>事件总线模块</summary>
        public static EventBus EventBus { get; private set; }

        /// <summary>配置管理模块</summary>
        public static ConfigMgr ConfigMgr { get; private set; }

        /// <summary>游戏数据中枢模块</summary>
        public static GameDataStore GameData { get; private set; }

        /// <summary>存档模块</summary>
        public static SaveMgr SaveMgr { get; private set; }

        /// <summary>UI 管理模块（MVVM/页面栈/弹窗/HUD）</summary>
        public static UIManager UIManager { get; private set; }

        /// <summary>多语言模块（文本/资源/字体切换）</summary>
        public static LocalizationMgr Localization { get; private set; }

        /// <summary>音频管理模块（BGM/SFX/UI/Voice 路由+池化+淡入淡出）</summary>
        public static AudioMgr AudioMgr { get; private set; }

        /// <summary>输入管理模块（上下文栈/R3暴露/缓冲/重映射）</summary>
        public static InputMgr InputMgr { get; private set; }

        // 后续批次添加更多模块：
        // CameraMgr / EntityManager / LevelManager / DebugConsole

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            // 按依赖顺序初始化
            // 1. Lifecycle（最先，提供 Update 通道/CTS 给后续模块）
            LifecycleMgr = new LifecycleMgr();
            LifecycleMgr.Init();

            // 2. Scheduler（依赖 Lifecycle 的 Update 通道）
            Scheduler = new Scheduler();
            Scheduler.Init();

            // 3. Pool（无依赖）
            PoolMgr = new PoolMgr();
            PoolMgr.Init();

            // 4. Resource（依赖 Pool）
            ResMgr = new ResMgr();
            ResMgr.Init();

            // 5. EventBus（无依赖）
            EventBus = new EventBus();
            EventBus.Init();

            // 6. Config（依赖 Res）
            ConfigMgr = new ConfigMgr();
            ConfigMgr.Init();

            // 7. GameDataStore（依赖 EventBus）
            GameData = new GameDataStore();
            GameData.Init();

            // 8. SaveMgr（依赖 GameDataStore）
            SaveMgr = new SaveMgr();
            SaveMgr.Init();

            // 9. UIManager（依赖 ResMgr/GameDataStore）
            UIManager = new UIManager();
            UIManager.Init();

            // 10. Localization（依赖 ResMgr）
            Localization = new LocalizationMgr();
            Localization.Init();

            // 11. AudioMgr（依赖 ResMgr/Scheduler）
            AudioMgr = new AudioMgr();
            AudioMgr.Init();

            // 12. InputMgr（依赖 UIManager 联动 hooks）
            InputMgr = new InputMgr();
            InputMgr.Init();

            Application.quitting += Dispose;

            Log.Info("GameGlobal", "PumpGF GameGlobal initialized.");
        }

        /// <summary>释放所有模块。Application.quitting 时自动调用。</summary>
        static void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;

            Application.quitting -= Dispose;

            // 按初始化逆序释放
            InputMgr?.Dispose();
            AudioMgr?.Dispose();
            Localization?.Dispose();
            UIManager?.Dispose();
            SaveMgr?.Dispose();
            GameData?.Dispose();
            ConfigMgr?.Dispose();
            EventBus?.Dispose();
            ResMgr?.Dispose();
            PoolMgr?.Dispose();
            Scheduler?.Dispose();
            LifecycleMgr?.Dispose();

            InputMgr = null;
            AudioMgr = null;
            Localization = null;
            UIManager = null;
            SaveMgr = null;
            GameData = null;
            ConfigMgr = null;
            EventBus = null;
            ResMgr = null;
            PoolMgr = null;
            Scheduler = null;
            LifecycleMgr = null;

            Log.Info("GameGlobal", "PumpGF GameGlobal disposed.");
        }
    }
}
