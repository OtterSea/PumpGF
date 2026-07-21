using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace PumpGF
{
    /// <summary>
    /// 异步动作叶节点：执行一个 UniTask 异步动作，映射为 Running 语义。
    /// <para>首次 Tick 启动任务并返回 Running；后续 Tick 未完成继续 Running，完成后返回其结果。</para>
    /// <para>节点被 Abort / 树 Reset 时取消传入的 CancellationToken。</para>
    /// <para>典型用途：播放攻击动画并等待命中帧/结束、等待 Timeline 播放完。</para>
    /// </summary>
    public sealed class AsyncActionNode : LeafNode
    {
        private readonly Func<TickContext, CancellationToken, UniTask<NodeStatus>> _action;

        private CancellationTokenSource _cts;
        private bool _completed;
        private NodeStatus _result;
        private bool _faulted;

        public AsyncActionNode(Func<TickContext, CancellationToken, UniTask<NodeStatus>> action)
        {
            _action = action;
        }

        protected override void OnEnter(in TickContext context)
        {
            _completed = false;
            _faulted = false;
            _result = NodeStatus.Running;

            if (_action == null)
            {
                _completed = true;
                _result = NodeStatus.Failure;
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
            RunAsync(context, _cts.Token).Forget();
        }

        protected override NodeStatus OnTick(in TickContext context)
        {
            if (_faulted) return NodeStatus.Failure;
            if (_completed) return _result;
            return NodeStatus.Running;
        }

        protected override void OnExit(in TickContext context)
        {
            // 正常完成或被中止：清理任务
            CancelAndDispose();
        }

        public override void Abort(in TickContext context)
        {
            CancelAndDispose();
            base.Abort(in context);
        }

        private async UniTask RunAsync(TickContext context, CancellationToken token)
        {
            try
            {
                var status = await _action(context, token);
                if (!token.IsCancellationRequested)
                {
                    _result = status;
                    _completed = true;
                }
            }
            catch (OperationCanceledException)
            {
                // 被中止，静默：节点已在 Abort/OnExit 中处理状态
            }
            catch (Exception e)
            {
                Log.Error("BT", $"AsyncActionNode '{Name}' 执行异常：{e}");
                _faulted = true;
                _completed = true;
                _result = NodeStatus.Failure;
            }
        }

        private void CancelAndDispose()
        {
            if (_cts != null)
            {
                if (!_cts.IsCancellationRequested) _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
        }
    }
}
