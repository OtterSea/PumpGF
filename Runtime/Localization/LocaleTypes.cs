using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PumpGF
{
    /// <summary>支持的语言（游戏可扩展枚举值）</summary>
    public enum Language
    {
        /// <summary>简体中文（默认）</summary>
        ChineseSimplified,
        /// <summary>英语</summary>
        English,
        /// <summary>日语</summary>
        Japanese,
    }

    /// <summary>语言条目（编辑器友好的 key→text 映射）</summary>
    [Serializable]
    public struct LocaleEntry
    {
        /// <summary>文本 key（业务引用名）</summary>
        public string Key;
        /// <summary>文本内容（支持 {0} 格式化占位符）</summary>
        [TextArea] public string Text;
    }

    /// <summary>
    /// 运行时语言数据。与数据源（SO/JSON）无关的统一结构，
    /// 由 <see cref="ILocaleProvider"/> 从数据源构建。
    /// </summary>
    public sealed class LocaleData
    {
        /// <summary>语言</summary>
        public Language Language;
        /// <summary>字体</summary>
        public TMP_FontAsset Font;
        /// <summary>key → 文本映射</summary>
        public readonly Dictionary<string, string> Texts = new();
    }
}
