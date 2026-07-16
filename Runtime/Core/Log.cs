using System.Collections.Concurrent;
using System.Diagnostics;
using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 日志工具类。4 级（Debug/Info/Warning/Error）+ Tag 分类 + Tag 过滤。
    /// Release 模式自动过滤到 Warning+。始终保留（非 <c>#if DEBUG</c>）。
    /// <para>线程安全：<see cref="SetTagFilter"/> / <see cref="IsTagEnabled"/> 使用 <see cref="ConcurrentDictionary{TKey,TValue}"/>，
    /// 可安全从任意线程调用（如 UniTask.RunOnThreadPool 内）。</para>
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

        // 被禁用的 Tag 集合（SetTagFilter 控制）。用 ConcurrentDictionary 保证多线程安全。
        private static readonly ConcurrentDictionary<string, byte> _disabledTags = new();

        /// <summary>设置 Tag 过滤。enabled=false 时该 Tag 日志不输出。</summary>
        public static void SetTagFilter(string tag, bool enabled)
        {
            if (string.IsNullOrEmpty(tag)) return;
            if (enabled) _disabledTags.TryRemove(tag, out _);
            else _disabledTags.TryAdd(tag, 0);
        }

        /// <summary>清空所有 Tag 过滤</summary>
        public static void ClearTagFilters() => _disabledTags.Clear();

        /// <summary>指定 Tag 是否启用（未被过滤）</summary>
        public static bool IsTagEnabled(string tag)
        {
            return string.IsNullOrEmpty(tag) || !_disabledTags.ContainsKey(tag);
        }

        /// <summary>
        /// Debug 级别日志（开发期详细）。
        /// <b>Release 构建下方法调用被 <c>[Conditional("DEBUG")]</c> 完全剔除</b>，
        /// 因此调用侧不会产生 tag/msg 字符串拼接开销。
        /// </summary>
        [Conditional("DEBUG")]
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
