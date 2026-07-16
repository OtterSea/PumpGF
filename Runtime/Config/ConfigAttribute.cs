using System;
using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 标记单例配置类。Addressables key 与 Preload 选项。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ConfigAttribute : Attribute
    {
        /// <summary>Addressables key</summary>
        public string Key { get; }
        /// <summary>是否启动预加载（默认 false）</summary>
        public bool Preload { get; set; }
        /// <summary>生成访问类的属性名（可选，默认从类名推导）</summary>
        public string Name { get; set; }

        public ConfigAttribute(string key) { Key = key; }
    }

    /// <summary>
    /// 标记配置表类。与 [Config] 类似，用于 ConfigTableSO 派生类。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ConfigTableAttribute : Attribute
    {
        public string Key { get; }
        public bool Preload { get; set; }
        public string Name { get; set; }

        public ConfigTableAttribute(string key) { Key = key; }
    }

    // ── 校验特性（编辑器期反射校验）──

    /// <summary>标记字段不可为 null/空</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class RequiredAttribute : PropertyAttribute { }

    /// <summary>标记数值不可为负</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class NonNegativeAttribute : PropertyAttribute { }

    /// <summary>标记配置表行的 key 字段唯一</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class UniqueIdAttribute : PropertyAttribute { }

    /// <summary>标记字段为 Addressables 配置 key（与 Addressables 不一致时黄色警告）</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class ConfigKeyAttribute : PropertyAttribute { }
}
