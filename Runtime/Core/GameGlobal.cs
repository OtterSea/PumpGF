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

        /// <summary>就绪注册表模块（一等公民的"等待就绪"机制，消除时序竞态）</summary>
        public static ReadinessRegistry Readiness { get; private set; }

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

        /// <summary>实体管理模块（ECS-Lite 组合/查询/池化）</summary>
        public static EntityManager EntityManager { get; private set; }

        /// <summary>关卡/场景管理模块（ILevel/过渡/Additive/Update 绑定）</summary>
        public static LevelManager LevelManager { get; private set; }

        /// <summary>调试控制台模块（命令/性能面板/Gizmos，#if DEBUG 主体）</summary>
        public static DebugConsole DebugConsole { get; private set; }

        // 后续批次添加更多模块：
        // CameraMgr
        // 注：FSM/HSM 为纯 C# 库（非 IModule），业务直接用 StateMachineBuilder 创建。

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            // 按依赖顺序初始化
            // 1. Lifecycle（最先，提供 Update 通道/CTS 给后续模块）
            LifecycleMgr = new LifecycleMgr();
            LifecycleMgr.Init();

            // 2. Readiness（无依赖，最早可用，供后续模块/业务注册与等待就绪）
            Readiness = new ReadinessRegistry();
            Readiness.Init();

            // 3. Scheduler（依赖 Lifecycle 的 Update 通道）
            Scheduler = new Scheduler();
            Scheduler.Init();

            // 4. Pool（无依赖）
            PoolMgr = new PoolMgr();
            PoolMgr.Init();

            // 5. Resource（依赖 Pool）
            ResMgr = new ResMgr();
            ResMgr.Init();

            // 6. EventBus（无依赖）
            EventBus = new EventBus();
            EventBus.Init();

            // 7. Config（依赖 Res）
            ConfigMgr = new ConfigMgr();
            ConfigMgr.Init();

            // 8. GameDataStore（依赖 EventBus）
            GameData = new GameDataStore();
            GameData.Init();

            // 9. SaveMgr（依赖 GameDataStore）
            SaveMgr = new SaveMgr();
            SaveMgr.Init();

            // 10. UIManager（依赖 ResMgr/GameDataStore）
            UIManager = new UIManager();
            UIManager.Init();

            // 11. Localization（依赖 ResMgr）
            Localization = new LocalizationMgr();
            Localization.Init();

            // 12. AudioMgr（依赖 ResMgr/Scheduler）
            AudioMgr = new AudioMgr();
            AudioMgr.Init();

            // 13. InputMgr（依赖 UIManager 联动 hooks）
            InputMgr = new InputMgr();
            InputMgr.Init();

            // 14. EntityManager（无强依赖，FSM 运行时关联）
            EntityManager = new EntityManager();
            EntityManager.Init();

            // 15. LevelManager（依赖 ResMgr/Lifecycle/UIManager）
            LevelManager = new LevelManager();
            LevelManager.Init();

            // 16. DebugConsole（最后，依赖其他模块指标；Release 时为空壳）
            DebugConsole = new DebugConsole();
            DebugConsole.Init();

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
            DebugConsole?.Dispose();
            LevelManager?.Dispose();
            EntityManager?.Dispose();
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
            Readiness?.Dispose();
            LifecycleMgr?.Dispose();

            DebugConsole = null;
            LevelManager = null;
            EntityManager = null;
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
            Readiness = null;
            LifecycleMgr = null;

            Log.Info("GameGlobal", "PumpGF GameGlobal disposed.");
        }
    }
}
