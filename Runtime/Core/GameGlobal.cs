using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 全局服务定位器。纯 C# 静态类，不依赖 MonoBehaviour。
    /// 首次访问或 [RuntimeInitializeOnLoadMethod] 时自动初始化所有模块。
    /// </summary>
    public static class GameGlobal
    {
        static bool _initialized;

        /// <summary>
        /// 对象池管理模块
        /// </summary>
        public static PoolMgr PoolMgr { get; private set; }

        /// <summary>
        /// 资源加载模块
        /// </summary>
        public static ResMgr ResMgr { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            // 按依赖顺序初始化：PoolMgr 无依赖，排在最前
            PoolMgr = new PoolMgr();
            PoolMgr.Init();

            // ResMgr 依赖 PoolMgr
            ResMgr = new ResMgr();
            ResMgr.Init();

            Application.quitting += Dispose;

            Debug.Log("[PumpGF] GameGlobal initialized.");
        }

        /// <summary>
        /// 释放所有模块。Application.quitting 时自动调用。
        /// </summary>
        static void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;

            Application.quitting -= Dispose;

            // 按初始化的逆序释放
            ResMgr?.Dispose();
            PoolMgr?.Dispose();

            ResMgr = null;
            PoolMgr = null;

            Debug.Log("[PumpGF] GameGlobal disposed.");
        }
    }
}
