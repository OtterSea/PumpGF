using System;

namespace PumpGF
{
    /// <summary>
    /// 更新通道枚举。决定"什么时候被 tick"以及"属于哪个暂停分组"。
    /// 框架提供 8 个基础通道，游戏可扩展（追加 1&lt;&lt;8 及以上）。
    /// </summary>
    [Flags]
    public enum UpdateChannel
    {
        /// <summary>通用兜底，每帧变量步长</summary>
        Default = 1 << 0,
        /// <summary>核心逻辑，固定步长（默认 60Hz）</summary>
        Logic = 1 << 1,
        /// <summary>动画表现层，每帧变量步长</summary>
        Animation = 1 << 2,
        /// <summary>UI 刷新，每帧</summary>
        UI = 1 << 3,
        /// <summary>特效/粒子，每帧</summary>
        Effect = 1 << 4,
        /// <summary>输入轮询，每帧</summary>
        Input = 1 << 5,
        /// <summary>Unity 原生 FixedUpdate（物理）</summary>
        FixedUpdate = 1 << 6,
        /// <summary>Unity 原生 LateUpdate（后处理/相机跟随）</summary>
        LateUpdate = 1 << 7,
    }
}
