namespace PumpGF
{
    /// <summary>
    /// 重复装饰器：重复执行子节点。
    /// <para>count &gt; 0：重复 count 次，全部 Success 后返回 Success；任一次 Failure 立即返回 Failure。</para>
    /// <para>count &lt; 0：无限重复（子节点每次完成后重启），本节点持续返回 Running（除非子节点 Failure）。</para>
    /// </summary>
    public sealed class RepeatNode : DecoratorNode
    {
        private readonly int _count;
        private int _completed;

        /// <param name="count">重复次数；小于 0 表示无限重复。</param>
        public RepeatNode(int count)
        {
            _count = count;
        }

        protected override void OnEnter(in TickContext context)
        {
            _completed = 0;
        }

        protected override NodeStatus OnTick(in TickContext context)
        {
            if (Child == null) return NodeStatus.Failure;

            var status = Child.Tick(in context);

            if (status == NodeStatus.Running)
                return NodeStatus.Running;

            if (status == NodeStatus.Failure)
                return NodeStatus.Failure;

            // 子节点本轮 Success
            _completed++;

            // 无限重复：永远 Running（下一帧子节点从头再来）
            if (_count < 0)
                return NodeStatus.Running;

            // 达到次数 → 整体 Success
            if (_completed >= _count)
                return NodeStatus.Success;

            // 还需继续重复
            return NodeStatus.Running;
        }
    }
}
