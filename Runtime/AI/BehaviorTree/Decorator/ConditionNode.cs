using System;

namespace PumpGF
{
    /// <summary>
    /// 条件守卫装饰器：谓词不满足时直接返回 Failure（不执行子节点）；满足则透传子节点结果。
    /// <para>与叶节点 <c>Check</c> 的区别：本节点守卫一整棵子树。</para>
    /// </summary>
    public sealed class ConditionNode : DecoratorNode
    {
        private readonly Func<TickContext, bool> _predicate;

        public ConditionNode(Func<TickContext, bool> predicate)
        {
            _predicate = predicate;
        }

        protected override NodeStatus OnTick(in TickContext context)
        {
            if (_predicate == null || !_predicate(context))
                return NodeStatus.Failure;

            if (Child == null) return NodeStatus.Failure;
            return Child.Tick(in context);
        }
    }
}
