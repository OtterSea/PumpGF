using System;
using System.Collections.Generic;
using R3;

namespace PumpGF
{
    /// <summary>
    /// 黑板中一个 Key 的类型 + 值快照（供调试面板/Editor 展示）。
    /// </summary>
    public readonly struct BlackboardEntry
    {
        public readonly string Key;
        public readonly Type Type;
        public readonly object Value;

        public BlackboardEntry(string key, Type type, object value)
        {
            Key = key;
            Type = type;
            Value = value;
        }
    }

    /// <summary>
    /// 行为树黑板：节点间共享数据的标准载体，避免节点直接持有彼此引用造成硬耦合。
    /// <para>类型安全读写；可选 R3 响应式 Key（仅按需创建，避免无谓分配）。</para>
    /// <para>不做序列化（存档由 GameDataStore 负责）。</para>
    /// <para>Key 统一由业务侧 <c>BBKeys</c> 常量表定义；框架不定义具体 Key，只提供开发期提示工具。</para>
    /// </summary>
    public sealed class Blackboard
    {
        private const string LogTag = "BT";

        // 值存储：key -> boxed value（值类型会装箱一次存入；读取用泛型拆箱）
        private readonly Dictionary<string, object> _values = new(16);
        // 类型记录：key -> 首次写入的类型，用于开发期类型一致性校验
        private readonly Dictionary<string, Type> _types = new(16);
        // 响应式 Key：key -> Subject<T>（object 持有，按需创建）
        private readonly Dictionary<string, object> _subjects = new();

        // ──────────────────────────────────────────────
        //  读写
        // ──────────────────────────────────────────────

        /// <summary>写入（类型安全）。若开启响应式 Key，会推送变更。</summary>
        public void Set<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key))
            {
                Log.Warning(LogTag, "Blackboard.Set: key 为空，已忽略。");
                return;
            }

            CheckTypeConsistency<T>(key);
            _types[key] = typeof(T);
            _values[key] = value;

            // 推送响应式变更（若该 Key 被 Observe 过）
            if (_subjects.TryGetValue(key, out var subj))
            {
                ((Subject<T>)subj).OnNext(value);
            }
        }

        /// <summary>读取（类型安全）。Key 不存在或类型不匹配返回 default，Debug 下告警。</summary>
        public T Get<T>(string key)
        {
            return TryGet<T>(key, out var value) ? value : default;
        }

        /// <summary>尝试读取。Key 不存在或类型不匹配返回 false。</summary>
        public bool TryGet<T>(string key, out T value)
        {
            if (string.IsNullOrEmpty(key))
            {
                value = default;
                return false;
            }

            if (!_values.TryGetValue(key, out var boxed))
            {
                WarnMissingKey(key);
                value = default;
                return false;
            }

            if (boxed is T typed)
            {
                value = typed;
                return true;
            }

            // 处理存了 null 引用（boxed == null 但 Key 存在）的情况
            if (boxed == null && !typeof(T).IsValueType)
            {
                value = default;
                return true;
            }

            WarnTypeMismatch<T>(key, boxed);
            value = default;
            return false;
        }

        /// <summary>Key 是否存在（已被 Set 过）。</summary>
        public bool Has(string key) => !string.IsNullOrEmpty(key) && _values.ContainsKey(key);

        /// <summary>移除 Key。响应式 Subject 保留（订阅关系不变，后续 Set 仍推送）。</summary>
        public void Remove(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _values.Remove(key);
            _types.Remove(key);
        }

        /// <summary>清空所有值（响应式 Subject 保留）。</summary>
        public void Clear()
        {
            _values.Clear();
            _types.Clear();
        }

        // ──────────────────────────────────────────────
        //  响应式 Key（可选）
        // ──────────────────────────────────────────────

        /// <summary>
        /// 获取某 Key 的变更流。仅在首次调用时创建 Subject，避免全量装箱开销。
        /// <para>供 ReactiveNode 监听、或业务 UI 订阅。</para>
        /// </summary>
        public Observable<T> Observe<T>(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                Log.Warning(LogTag, "Blackboard.Observe: key 为空。");
                return Observable.Empty<T>();
            }

            if (!_subjects.TryGetValue(key, out var subj))
            {
                var s = new Subject<T>();
                _subjects[key] = s;
                return s;
            }

            if (subj is Subject<T> typed) return typed;

            Log.Warning(LogTag, $"Blackboard.Observe: key '{key}' 已以其它类型被 Observe，返回空流。");
            return Observable.Empty<T>();
        }

        // ──────────────────────────────────────────────
        //  调试快照
        // ──────────────────────────────────────────────

        /// <summary>
        /// 导出当前所有 Key 的 (key, type, value) 快照，供 DebugConsole / Editor 面板核对。
        /// <para>会分配 List，仅用于调试，勿在热路径调用。</para>
        /// </summary>
        public List<BlackboardEntry> CaptureKeys()
        {
            var list = new List<BlackboardEntry>(_values.Count);
            foreach (var kv in _values)
            {
                _types.TryGetValue(kv.Key, out var t);
                list.Add(new BlackboardEntry(kv.Key, t, kv.Value));
            }
            return list;
        }

        // ──────────────────────────────────────────────
        //  开发期提示工具（Release 下 [Conditional] 剔除）
        // ──────────────────────────────────────────────

        [System.Diagnostics.Conditional("DEBUG")]
        private void CheckTypeConsistency<T>(string key)
        {
            if (_types.TryGetValue(key, out var existing) && existing != typeof(T))
            {
                Log.Warning(LogTag,
                    $"Blackboard: key '{key}' 类型不一致——曾以 {existing.Name} 写入，现以 {typeof(T).Name} 写入。请检查 BBKeys 用法。");
            }
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void WarnMissingKey(string key)
        {
            Log.Warning(LogTag, $"Blackboard: 读取未定义的 key '{key}'（返回 default）。请确认已 Set 或拼写正确。");
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void WarnTypeMismatch<T>(string key, object boxed)
        {
            Log.Warning(LogTag,
                $"Blackboard: key '{key}' 类型不匹配——实际 {(boxed?.GetType().Name ?? "null")}，请求 {typeof(T).Name}。");
        }
    }
}
