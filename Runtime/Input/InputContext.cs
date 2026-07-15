namespace PumpGF
{
    /// <summary>
    /// 输入上下文枚举。决定当前启用的 InputActionMap。
    /// <para>约定：<see cref="InputContext"/> 的枚举名 = New Input System 中对应 ActionMap 的名字。</para>
    /// <para>例如 <see cref="Battle"/> → 启用 "Battle" ActionMap；<see cref="Menu"/> → 启用 "Menu" ActionMap。</para>
    /// <para>游戏层可直接向枚举追加值（如 <c>Inventory = 5</c>），并在 .inputactions 添加对应 ActionMap。</para>
    /// <para>本枚举独立于 <c>InputMgr</c> 主体存在，供 <see cref="PageConfig.InputContext"/> 引用。</para>
    /// </summary>
    public enum InputContext
    {
        /// <summary>无上下文（仅 Global ActionMap 启用）</summary>
        None,
        /// <summary>战斗输入（攻击/闪避/技能/移动）</summary>
        Battle,
        /// <summary>菜单导航（上下左右/确认/返回）</summary>
        Menu,
        /// <summary>对话推进</summary>
        Dialog,
        /// <summary>过场动画（跳过）</summary>
        Cutscene,
        /// <summary>调试</summary>
        Debug,
    }
}
