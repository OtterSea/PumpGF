using System.Collections.Generic;
using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 日志工具类。4 级（Debug/Info/Warning/Error）+ Tag 分类 + Tag 过滤。
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

        // 被禁用的 Tag 集合（SetTagFilter 控制）
        private static readonly HashSet<string> _disabledTags = new();

        /// <summary>设置 Tag 过滤。enabled=false 时该 Tag 日志不输出。</summary>
        public static void SetTagFilter(string tag, bool enabled)
        {
            if (string.IsNullOrEmpty(tag)) return;
            if (enabled) _disabledTags.Remove(tag);
            else _disabledTags.Add(tag);
        }

        /// <summary>清空所有 Tag 过滤</summary>
        public static void ClearTagFilters() => _disabledTags.Clear();

        /// <summary>指定 Tag 是否启用（未被过滤）</summary>
        public static bool IsTagEnabled(string tag)
        {
            return string.IsNullOrEmpty(tag) || !_disabledTags.Contains(tag);
        }

        /// <summary>Debug 级别日志（开发期详细）</summary>
        public static void Debug(string tag, string msg, Object context = null)
        {
            if (FilterLevel > LogLevel.Debug || !IsTagEnabled(tag)) return;
            UnityEngine.Debug.Log($"[{tag}] {msg}", context);
        }

        /// <summary>Info 级别日志（常规信息）</summary>
        public static void Info(string tag, string msg, Object context = null)
        {
            if (FilterLevel > LogLevel.Info || !IsTagEnabled(tag)) return;
            UnityEngine.Debug.Log($"[{tag}] {msg}", context);
        }

        /// <summary>Warning 级别日志</summary>
        public static void Warning(string tag, string msg, Object context = null)
        {
            if (FilterLevel > LogLevel.Warning || !IsTagEnabled(tag)) return;
            UnityEngine.Debug.LogWarning($"[{tag}] {msg}", context);
        }

        /// <summary>Error 级别日志</summary>
        public static void Error(string tag, string msg, Object context = null)
        {
            if (FilterLevel > LogLevel.Error || !IsTagEnabled(tag)) return;
            UnityEngine.Debug.LogError($"[{tag}] {msg}", context);
        }
    }
}
