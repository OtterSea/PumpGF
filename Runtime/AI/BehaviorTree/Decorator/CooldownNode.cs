namespace PumpGF
{
    /// <summary>
    /// 冷却装饰器：子节点成功后进入冷却期，冷却期内直接返回 Failure（不执行子节点）。
    /// <para>典型用途：技能/招式的 CD。冷却计时使用 <see cref="TickContext.DeltaTime"/> 累加，
    /// 因此暂停（不 Tick）时冷却自然冻结。</para>
    /// </summary>
    public sealed class CooldownNode : DecoratorNode
    {
        private readonly float _cooldownSeconds;
        private float _remaining;

        public CooldownNode(float cooldownSeconds)
        {
            _cooldownSeconds = cooldownSeconds;
        }

        protected override NodeStatus OnTick(in TickContext context)
        {
            if (Child == null) return NodeStatus.Failure;

            // 冷却中：递减并返回 Failure
            if (_remaining > 0f)
            {
                _remaining -= context.DeltaTime;
                return NodeStatus.Failure;
            }

            var status = Child.Tick(in context);

            // 子节点成功后启动冷却
            if (status == NodeStatus.Success)
            {
                _remaining = _cooldownSeconds;
            }

            return status;
        }
    }
}
