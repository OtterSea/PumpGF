using System;

namespace PumpGF
{
    /// <summary>
    /// 条件判断叶节点：谓词为 true 返回 Success，否则 Failure。永不返回 Running。
    /// </summary>
    public sealed class ConditionLeafNode : LeafNode
    {
        private readonly Func<TickContext, bool> _condition;

        public ConditionLeafNode(Func<TickContext, bool> condition)
        {
            _condition = condition;
        }

        protected override NodeStatus OnTick(in TickContext context)
        {
            if (_condition == null) return NodeStatus.Failure;
            return _condition(context) ? NodeStatus.Success : NodeStatus.Failure;
        }
    }
}
