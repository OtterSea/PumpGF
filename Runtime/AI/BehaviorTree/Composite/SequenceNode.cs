namespace PumpGF
{
    /// <summary>
    /// 顺序节点（与）：从上到下依次执行子节点。
    /// <para>遇 Failure 立即返回 Failure；遇 Running 记忆位置返回 Running；全部 Success 才返回 Success。</para>
    /// </summary>
    public sealed class SequenceNode : CompositeNode
    {
        protected override NodeStatus OnTick(in TickContext context)
        {
            while (CurrentIndex < Children.Count)
            {
                var status = Children[CurrentIndex].Tick(in context);

                if (status == NodeStatus.Running)
                    return NodeStatus.Running;

                if (status == NodeStatus.Failure)
                    return NodeStatus.Failure;

                // Success → 前进到下一个子节点
                CurrentIndex++;
            }

            return NodeStatus.Success;
        }
    }
}
