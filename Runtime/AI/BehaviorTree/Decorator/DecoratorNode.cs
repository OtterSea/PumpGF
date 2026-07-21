using System.Collections.Generic;

namespace PumpGF
{
    /// <summary>
    /// 装饰器节点基类：有且仅有一个子节点，对其结果做变换/守卫/控制。
    /// </summary>
    public abstract class DecoratorNode : BTNode
    {
        /// <summary>被装饰的子节点</summary>
        protected BTNode Child;

        /// <summary>设置子节点（构建期调用，仅允许一个）</summary>
        public void SetChild(BTNode child)
        {
            if (child == null) return;
            child.Parent = this;
            Child = child;
        }

        public override void Abort(in TickContext context)
        {
            Child?.Abort(in context);
            base.Abort(in context);
        }

        internal override void CollectChildren(List<BTNode> buffer)
        {
            if (Child != null)
            {
                buffer.Add(Child);
                Child.CollectChildren(buffer);
            }
        }

        /// <summary>取直接子节点（一层，供快照带深度前序遍历）。</summary>
        internal void GetDirectChild(List<BTNode> buffer)
        {
            if (Child != null) buffer.Add(Child);
        }
    }
}
