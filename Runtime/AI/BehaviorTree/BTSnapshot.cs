using System.Collections.Generic;

namespace PumpGF
{
    /// <summary>
    /// 行为树运行时快照中的单个节点信息。供 DebugConsole / Editor Tools 展示。
    /// </summary>
    public readonly struct BTNodeSnapshot
    {
        /// <summary>节点类型名（如 SequenceNode）</summary>
        public readonly string TypeName;
        /// <summary>节点名（构建时指定，可空）</summary>
        public readonly string Name;
        /// <summary>上次 Tick 的状态</summary>
        public readonly NodeStatus LastStatus;
        /// <summary>在树中的深度（根为 0）</summary>
        public readonly int Depth;

        public BTNodeSnapshot(string typeName, string name, NodeStatus lastStatus, int depth)
        {
            TypeName = typeName;
            Name = name;
            LastStatus = lastStatus;
            Depth = depth;
        }
    }

    /// <summary>
    /// 行为树整树快照：节点列表（前序遍历）+ 树名 + 整体状态。
    /// </summary>
    public sealed class BTSnapshot
    {
        public string TreeName;
        public NodeStatus RootStatus;
        public readonly List<BTNodeSnapshot> Nodes = new();
    }

    /// <summary>
    /// 行为节点事件。行为树节点进入/结束/中止时发布（默认关闭，Builder 上 EnableEvents 开启）。
    /// </summary>
    public readonly struct BehaviorNodeEvent
    {
        /// <summary>行为树名</summary>
        public readonly string TreeName;
        /// <summary>节点名</summary>
        public readonly string NodeName;
        /// <summary>节点状态</summary>
        public readonly NodeStatus Status;

        public BehaviorNodeEvent(string treeName, string nodeName, NodeStatus status)
        {
            TreeName = treeName;
            NodeName = nodeName;
            Status = status;
        }
    }
}
