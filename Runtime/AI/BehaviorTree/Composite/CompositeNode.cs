using System.Collections.Generic;

namespace PumpGF
{
    /// <summary>
    /// 复合节点基类：持有多个子节点。子类定义子节点的组合执行策略。
    /// </summary>
    public abstract class CompositeNode : BTNode
    {
        /// <summary>子节点列表（预分配，构建期填充）</summary>
        protected readonly List<BTNode> Children = new(4);

        /// <summary>当前执行到的子节点索引（记忆型复合节点使用）</summary>
        protected int CurrentIndex;

        /// <summary>添加子节点（构建期调用）</summary>
        public void AddChild(BTNode child)
        {
            if (child == null) return;
            child.Parent = this;
            Children.Add(child);
        }

        /// <summary>进入时重置执行游标</summary>
        protected override void OnEnter(in TickContext context)
        {
            CurrentIndex = 0;
        }

        /// <summary>中止：递归中止所有子节点后清理自身。</summary>
        public override void Abort(in TickContext context)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i].Abort(in context);
            }
            base.Abort(in context);
            CurrentIndex = 0;
        }

        internal override void CollectChildren(List<BTNode> buffer)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                buffer.Add(Children[i]);
                Children[i].CollectChildren(buffer);
            }
        }

        /// <summary>取直接子节点（一层，供快照带深度前序遍历）。</summary>
        internal void GetDirectChildren(List<BTNode> buffer)
        {
            for (int i = 0; i < Children.Count; i++)
                buffer.Add(Children[i]);
        }
    }
}
