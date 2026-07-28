using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 语言配置资产。每语言创建一个（如 ChineseSimplified.asset / English.asset）。
    /// Addressables key 约定：<c>"Locale/{Language}"</c>（如 <c>"Locale/ChineseSimplified"</c>）。
    /// </summary>
    [CreateAssetMenu(menuName = "PumpGF/Localization/Locale")]
    public class LocaleSO : ScriptableObject
    {
        /// <summary>此资产对应语言</summary>
        public Language Language;
        /// <summary>该语言字体（TMP_FontAsset，切换语言时全局替换）</summary>
        public TMP_FontAsset Font;
        /// <summary>文本条目列表</summary>
        public List<LocaleEntry> Entries = new();
    }
}
