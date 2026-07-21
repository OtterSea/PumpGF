namespace PumpGF
{
    /// <summary>
    /// 取反装饰器：将子节点的 Success ↔ Failure 互换；Running 透传。
    /// </summary>
    public sealed class InverterNode : DecoratorNode
    {
        protected override NodeStatus OnTick(in TickContext context)
        {
            if (Child == null) return NodeStatus.Failure;

            var status = Child.Tick(in context);
            switch (status)
            {
                case NodeStatus.Success: return NodeStatus.Failure;
                case NodeStatus.Failure: return NodeStatus.Success;
                default: return NodeStatus.Running;
            }
        }
    }
}
