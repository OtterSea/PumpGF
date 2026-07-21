using System.Collections.Generic;

namespace PumpGF
{
    /// <summary>
    /// 行为树实例：持有根节点与黑板，按帧驱动 Tick。
    /// <para>纯 C#，不依赖 MonoBehaviour。用 <see cref="BehaviorTreeBuilder"/> 构建。</para>
    /// <para>驱动方式：<see cref="BehaviorTreeExtensions.BindToLifecycle"/> 自动 Tick，或手动 <see cref="Tick"/>。</para>
    /// </summary>
    public sealed class BehaviorTree
    {
        /// <summary>树名（调试/事件用）</summary>
        public string Name { get; }

        /// <summary>共享黑板</summary>
        public Blackboard Blackboard { get; }

        /// <summary>根节点</summary>
        public BTNode Root { get; }

        /// <summary>拥有者（写入 TickContext.Owner，供叶节点强转访问）</summary>
        public object Owner { get; set; }

        /// <summary>上次 Tick 根节点返回的状态</summary>
        public NodeStatus LastStatus { get; private set; } = NodeStatus.Failure;

        internal BehaviorTree(string name, BTNode root, Blackboard blackboard, object owner)
        {
            Name = name;
            Root = root;
            Blackboard = blackboard ?? new Blackboard();
            Owner = owner;
        }

        /// <summary>驱动一帧。构造上下文并 Tick 根节点。</summary>
        public NodeStatus Tick(float deltaTime)
        {
            if (Root == null) return NodeStatus.Failure;
            var ctx = new TickContext(Blackboard, deltaTime, Owner);
            LastStatus = Root.Tick(in ctx);
            return LastStatus;
        }

        /// <summary>重置：中止所有 Running 节点，回到初始（下次 Tick 从头执行）。</summary>
        public void Reset()
        {
            Abort();
        }

        /// <summary>中止整棵树，清理所有 Running 节点。</summary>
        public void Abort()
        {
            if (Root == null) return;
            var ctx = new TickContext(Blackboard, 0f, Owner);
            Root.Abort(in ctx);
            LastStatus = NodeStatus.Failure;
        }

        /// <summary>
        /// 导出当前节点状态快照（前序遍历），供 DebugConsole / Editor 面板。
        /// 会分配，仅用于调试，勿在热路径调用。
        /// </summary>
        public BTSnapshot CaptureSnapshot()
        {
            var snap = new BTSnapshot { TreeName = Name, RootStatus = LastStatus };
            if (Root == null) return snap;

            // 前序遍历：先收集 root，再用 CollectChildren 递归收集
            AppendNode(snap, Root, 0);
            return snap;
        }

        private static void AppendNode(BTSnapshot snap, BTNode node, int depth)
        {
            snap.Nodes.Add(new BTNodeSnapshot(
                node.GetType().Name, node.Name, node.LastStatus, depth));

            // 直接子节点：借助 CollectChildren 的单层能力
            var directChildren = new List<BTNode>();
            CollectDirectChildren(node, directChildren);
            for (int i = 0; i < directChildren.Count; i++)
                AppendNode(snap, directChildren[i], depth + 1);
        }

        // 仅取直接子节点（一层），用于带深度的前序遍历
        private static void CollectDirectChildren(BTNode node, List<BTNode> buffer)
        {
            switch (node)
            {
                case CompositeNode composite:
                    composite.GetDirectChildren(buffer);
                    break;
                case DecoratorNode decorator:
                    decorator.GetDirectChild(buffer);
                    break;
            }
        }
    }
}
