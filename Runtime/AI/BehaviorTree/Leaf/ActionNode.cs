using System;

namespace PumpGF
{
    /// <summary>
    /// 同步动作叶节点：每帧调用委托并返回其三态结果。
    /// <para>长动作应持续返回 Running 直到真正结束，切勿每帧返回 Success/Failure 造成决策抖动。</para>
    /// </summary>
    public sealed class ActionNode : LeafNode
    {
        private readonly Func<TickContext, NodeStatus> _action;

        public ActionNode(Func<TickContext, NodeStatus> action)
        {
            _action = action;
        }

        protected override NodeStatus OnTick(in TickContext context)
        {
            if (_action == null) return NodeStatus.Failure;
            return _action(context);
        }
    }
}
