using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 日志工具类。4 级（Debug/Info/Warning/Error）+ Tag 分类。
    /// Release 模式自动过滤到 Warning+。始终保留（非 #if DEBUG）。
    /// </summary>
    public static class Log
    {
        /// <summary>全局过滤级别。低于此级别的日志不输出。</summary>
        public static LogLevel FilterLevel { get; set; } =
#if DEBUG
            LogLevel.Debug;
#else
            LogLevel.Warning;
#endif

        /// <summary>Debug 级别日志（开发期详细）</summary>
        public static void Debug(string tag, string msg, Object context = null)
        {
            if (FilterLevel <= LogLevel.Debug)
                UnityEngine.Debug.Log($"[{tag}] {msg}", context);
        }

        /// <summary>Info 级别日志（常规信息）</summary>
        public static void Info(string tag, string msg, Object context = null)
        {
            if (FilterLevel <= LogLevel.Info)
                UnityEngine.Debug.Log($"[{tag}] {msg}", context);
        }

        /// <summary>Warning 级别日志</summary>
        public static void Warning(string tag, string msg, Object context = null)
        {
            if (FilterLevel <= LogLevel.Warning)
                UnityEngine.Debug.LogWarning($"[{tag}] {msg}", context);
        }

        /// <summary>Error 级别日志</summary>
        public static void Error(string tag, string msg, Object context = null)
        {
            if (FilterLevel <= LogLevel.Error)
                UnityEngine.Debug.LogError($"[{tag}] {msg}", context);
        }
    }
}
