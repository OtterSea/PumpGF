using System;
using System.Collections.Generic;
using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 配置表标记接口。实现此接口的 SO 被 ConfigMgr 识别为配置表。
    /// </summary>
    public interface IConfigTable
    {
        /// <summary>构建索引（加载后由 ConfigMgr 调用）</summary>
        void BuildIndex();
    }

    /// <summary>
    /// 配置校验接口。SO 实现此接口提供复杂校验逻辑。
    /// 编辑器期由校验工具调用。
    /// </summary>
    public interface IConfigValidator
    {
        /// <summary>校验配置，返回错误列表（空列表表示通过）</summary>
        IReadOnlyList<string> Validate();
    }

    /// <summary>
    /// 配置表泛型基类。继承此类获得 GetByKey 索引能力。
    /// TKey 为 key 类型（int/string/枚举等），每张表自定义。
    /// </summary>
    public abstract class ConfigTableSO<TRow, TKey> : ScriptableObject, IConfigTable
        where TRow : class
    {
        [SerializeField] protected List<TRow> _rows = new();

        /// <summary>运行时索引</summary>
        protected Dictionary<TKey, TRow> _index;

        /// <summary>所有行（只读）</summary>
        public IReadOnlyList<TRow> AllRows => _rows;

        /// <summary>行数</summary>
        public int Count => _rows?.Count ?? 0;

        /// <summary>按 key 查询行（未找到返回 null + Warning）</summary>
        public TRow GetByKey(TKey key)
        {
            if (_index == null) BuildIndex();
            if (_index.TryGetValue(key, out var row)) return row;
            Log.Warning("ConfigTable", $"Key not found: {key} in {GetType().Name}");
            return null;
        }

        /// <summary>按 key 安全查询（不 Warning）</summary>
        public bool TryGetByKey(TKey key, out TRow row)
        {
            if (_index == null) BuildIndex();
            return _index.TryGetValue(key, out row);
        }

        /// <summary>子类定义"如何从行取 key"</summary>
        protected abstract TKey GetKey(TRow row);

        /// <summary>构建索引（加载后由 ConfigMgr 自动调用）</summary>
        public void BuildIndex()
        {
            if (_index != null) return; // 已构建
            _index = new Dictionary<TKey, TRow>();
            if (_rows == null) return;
            foreach (var row in _rows)
            {
                if (row == null) continue;
                var key = GetKey(row);
                if (_index.ContainsKey(key))
                {
                    Log.Warning("ConfigTable", $"Duplicate key '{key}' in {GetType().Name}");
                    continue;
                }
                _index[key] = row;
            }
        }
    }
}
