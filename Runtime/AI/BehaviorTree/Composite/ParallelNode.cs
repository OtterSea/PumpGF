namespace PumpGF
{
    /// <summary>并行节点的成功判定策略。</summary>
    public enum ParallelSuccessPolicy
    {
        /// <summary>任一子节点 Success，整体 Success</summary>
        RequireOne,
        /// <summary>全部子节点 Success，整体 Success</summary>
        RequireAll,
    }

    /// <summary>并行节点的失败判定策略。</summary>
    public enum ParallelFailurePolicy
    {
        /// <summary>任一子节点 Failure，整体 Failure</summary>
        RequireOne,
        /// <summary>全部子节点 Failure，整体 Failure</summary>
        RequireAll,
    }

    /// <summary>
    /// 并行节点：每帧 Tick 所有未完成的子节点，按策略判定整体结果。
    /// <para>首版默认策略：<see cref="ParallelSuccessPolicy.RequireOne"/> + <see cref="ParallelFailurePolicy.RequireAll"/>
    /// （任一成功即成功、全部失败才失败）。</para>
    /// <para>未判定出整体结果时返回 Running；已完成的子节点本帧起不再 Tick。</para>
    /// </summary>
    public sealed class ParallelNode : CompositeNode
    {
        private readonly ParallelSuccessPolicy _successPolicy;
        private readonly ParallelFailurePolicy _failurePolicy;

        // 每个子节点是否已完成（Success/Failure），以及其最终状态
        private bool[] _finished;
        private NodeStatus[] _results;

        public ParallelNode(
            ParallelSuccessPolicy successPolicy = ParallelSuccessPolicy.RequireOne,
            ParallelFailurePolicy failurePolicy = ParallelFailurePolicy.RequireAll)
        {
            _successPolicy = successPolicy;
            _failurePolicy = failurePolicy;
        }

        protected override void OnEnter(in TickContext context)
        {
            base.OnEnter(in context);
            EnsureBuffers();
            for (int i = 0; i < Children.Count; i++)
            {
                _finished[i] = false;
                _results[i] = NodeStatus.Running;
            }
        }

        protected override NodeStatus OnTick(in TickContext context)
        {
            int successCount = 0;
            int failureCount = 0;

            for (int i = 0; i < Children.Count; i++)
            {
                if (!_finished[i])
                {
                    var status = Children[i].Tick(in context);
                    if (status != NodeStatus.Running)
                    {
                        _finished[i] = true;
                        _results[i] = status;
                    }
                }

                if (_finished[i])
                {
                    if (_results[i] == NodeStatus.Success) successCount++;
                    else failureCount++;
                }
            }

            int total = Children.Count;

            bool success = _successPolicy == ParallelSuccessPolicy.RequireOne
                ? successCount >= 1
                : successCount >= total;

            bool failure = _failurePolicy == ParallelFailurePolicy.RequireOne
                ? failureCount >= 1
                : failureCount >= total;

            if (success)
            {
                AbortRemaining(in context);
                return NodeStatus.Success;
            }
            if (failure)
            {
                AbortRemaining(in context);
                return NodeStatus.Failure;
            }

            return NodeStatus.Running;
        }

        private void AbortRemaining(in TickContext context)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                if (!_finished[i])
                    Children[i].Abort(in context);
            }
        }

        private void EnsureBuffers()
        {
            if (_finished == null || _finished.Length != Children.Count)
            {
                _finished = new bool[Children.Count];
                _results = new NodeStatus[Children.Count];
            }
        }
    }
}
