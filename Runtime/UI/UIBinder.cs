using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PumpGF
{
    /// <summary>UIBinder 绑定类型（预留，供未来 Editor 工具生成绑定代码用）</summary>
    public enum UIBindingType
    {
        /// <summary>文本绑定（属性 → TMP_Text/Text）</summary>
        Text,
        /// <summary>滑块绑定（属性 → Slider.value）</summary>
        Slider,
        /// <summary>激活状态绑定（属性 → GameObject.SetActive）</summary>
        Active,
        /// <summary>图片绑定（属性 → Image.sprite）</summary>
        Image,
        /// <summary>点击绑定（Button → 命令）</summary>
        Click,
    }

    /// <summary>
    /// 单条 UI 绑定描述（数据容器，供 Editor 工具配置）。
    /// 本阶段不强制运行时自动执行，业务推荐用 <see cref="View{TViewModel}"/> 的 OnBind 手动绑定。
    /// </summary>
    [Serializable]
    public struct UIBindingEntry
    {
        /// <summary>源属性名（ViewModel 上的 ReactiveProperty 名）</summary>
        public string SourceProperty;
        /// <summary>目标组件（Inspector 拖拽）</summary>
        public Component Target;
        /// <summary>绑定类型</summary>
        public UIBindingType Type;
        /// <summary>格式化字符串（可选）</summary>
        public string Format;
    }

    /// <summary>
    /// UIBinder 组件。挂在 View 节点上，Inspector 配置绑定关系（数据框架）。
    /// <para>本阶段提供数据结构基础；完整的可视化配置与运行时反射绑定由后续 Editor Tools 增强。</para>
    /// <para>当前推荐：在 <c>View.OnBind</c> 中用 <c>FindChild&lt;T&gt;("Auto_Xxx")</c> + <c>BindText/BindClick</c> 手动绑定。</para>
    /// </summary>
    [AddComponentMenu("PumpGF/UI/UIBinder")]
    public sealed class UIBinder : MonoBehaviour
    {
        [SerializeField] private List<UIBindingEntry> _bindings = new();

        /// <summary>绑定制表（只读访问）</summary>
        public IReadOnlyList<UIBindingEntry> Bindings => _bindings;

        /// <summary>添加一条绑定描述</summary>
        public void AddBinding(UIBindingEntry entry) => _bindings.Add(entry);

        /// <summary>清空所有绑定描述</summary>
        public void ClearBindings() => _bindings.Clear();
    }
}
