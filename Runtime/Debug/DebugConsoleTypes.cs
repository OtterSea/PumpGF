using System;
using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 标记方法为控制台命令。DebugConsole 启动时反射扫描自动注册。
    /// <para>特性本身始终保留（无运行时开销）；反射扫描仅在 #if DEBUG 内执行。</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ConsoleCommandAttribute : Attribute
    {
        /// <summary>命令名</summary>
        public string CommandName { get; }
        /// <summary>描述（help 命令显示）</summary>
        public string Description { get; }

        public ConsoleCommandAttribute(string name, string description = null)
        {
            CommandName = name;
            Description = description;
        }
    }

    /// <summary>
    /// 自定义调试面板接口。业务实现后注册到 DebugConsole，在性能面板区域绘制。
    /// </summary>
    public interface IDebugPanel
    {
        /// <summary>面板标题</summary>
        string Title { get; }
        /// <summary>在指定区域内绘制（GUILayout）</summary>
        void OnGUI(Rect rect);
    }

    /// <summary>
    /// Gizmos 绘制接口。实现后注册到 DebugConsole，框架统一在 OnDrawGizmos 调用。
    /// <para>仅编辑器生效（OnDrawGizmos 打包后不调用）。</para>
    /// </summary>
    public interface IGizmosDrawable
    {
        /// <summary>绘制 Gizmos</summary>
        void DrawGizmos();
    }

    /// <summary>命令信息</summary>
    public struct CommandInfo
    {
        /// <summary>命令名</summary>
        public string Name;
        /// <summary>描述</summary>
        public string Description;
        /// <summary>参数类型列表</summary>
        public Type[] ParameterTypes;
        /// <summary>用法字符串</summary>
        public string Usage;
    }

    /// <summary>DebugConsole 配置</summary>
    [Serializable]
    public class DebugConsoleConfig
    {
        /// <summary>控制台开关快捷键（编辑器）</summary>
        public KeyCode ToggleKey = KeyCode.BackQuote;
        /// <summary>是否启用真机三指手势开关</summary>
        public bool EnableTouchGesture = true;
        /// <summary>Stats 面板是否默认显示</summary>
        public bool ShowStatsByDefault = true;
    }
}
