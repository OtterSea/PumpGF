namespace PumpGF
{
    /// <summary>
    /// 择一节点（或）：从上到下依次尝试子节点。
    /// <para>遇 Success 立即返回 Success；遇 Running 记忆位置返回 Running；全部 Failure 才返回 Failure。</para>
    /// </summary>
    public sealed class SelectorNode : CompositeNode
    {
        protected override NodeStatus OnTick(in TickContext context)
        {
            while (CurrentIndex < Children.Count)
            {
                var status = Children[CurrentIndex].Tick(in context);

                if (status == NodeStatus.Running)
                    return NodeStatus.Running;

                if (status == NodeStatus.Success)
                    return NodeStatus.Success;

                // Failure → 尝试下一个子节点
                CurrentIndex++;
            }

            return NodeStatus.Failure;
        }
    }
}
