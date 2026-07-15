using System;

namespace PumpGF
{
    /// <summary>标记 View 为"页面"（进入页面栈管理）</summary>
    public interface IPage { }

    /// <summary>标记 View 为"弹窗"（进入弹窗队列管理）</summary>
    public interface IPopup { }

    /// <summary>标记 View 为"自由 HUD"（常驻，独立管理）</summary>
    public interface IHud { }

    /// <summary>
    /// 命令接口（ViewModel 暴露给 View 的可执行操作）。
    /// View 通过 <see cref="View{TViewModel}.BindClick(UnityEngine.UI.Button, ICommand)"/> 绑定到按钮。
    /// </summary>
    public interface ICommand
    {
        /// <summary>当前是否可执行</summary>
        bool CanExecute(object parameter);

        /// <summary>执行命令</summary>
        void Execute(object parameter);
    }

    /// <summary>
    /// 页面配置。控制打开页面时的行为。
    /// </summary>
    [Serializable]
    public struct PageConfig
    {
        /// <summary>打开此页时是否隐藏下层页面（默认 true，省渲染）</summary>
        public bool HideUnderlying;

        /// <summary>是否响应返回键关闭（默认 true）</summary>
        public bool CloseOnBack;

        /// <summary>打开页面时自动切换到的输入上下文（null = 不切换）</summary>
        public InputContext? InputContext;

        /// <summary>默认配置（HideUnderlying=true, CloseOnBack=true）</summary>
        public static PageConfig Default => new()
        {
            HideUnderlying = true,
            CloseOnBack = true,
            InputContext = null
        };
    }

    /// <summary>
    /// UI 层级。决定 Canvas 的 SortingOrder 与挂载位置。
    /// </summary>
    public enum UILayer
    {
        /// <summary>常驻 HUD（血条/小地图），SortingOrder 100 段</summary>
        HUD = 0,
        /// <summary>页面栈，SortingOrder 200 段</summary>
        Page = 1,
        /// <summary>弹窗队列，SortingOrder 300 段</summary>
        Popup = 2,
        /// <summary>顶层（Loading/全局提示/Debug），SortingOrder 400 段</summary>
        Top = 3,
    }

    /// <summary>
    /// UI 层级 SortingOrder 配置。可通过 <see cref="UIManager.SetLayerSortingOrder"/> 运行时修改。
    /// </summary>
    [Serializable]
    public struct UILayerConfig
    {
        /// <summary>HUD 层起始 SortingOrder</summary>
        public int HudOrder;
        /// <summary>Page 层起始 SortingOrder</summary>
        public int PageOrder;
        /// <summary>Popup 层起始 SortingOrder</summary>
        public int PopupOrder;
        /// <summary>Top 层起始 SortingOrder</summary>
        public int TopOrder;

        /// <summary>默认层级配置</summary>
        public static UILayerConfig Default => new()
        {
            HudOrder = 100,
            PageOrder = 200,
            PopupOrder = 300,
            TopOrder = 400,
        };
    }
}
