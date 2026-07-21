using System.Collections.Generic;

namespace PumpGF
{
    /// <summary>
    /// 行为树节点执行三态。每次 Tick 返回其一。
    /// </summary>
    public enum NodeStatus
    {
        /// <summary>节点完成，成功</summary>
        Success,
        /// <summary>节点完成，失败</summary>
        Failure,
        /// <summary>节点仍在执行中，下一帧继续 Tick（记忆节点）</summary>
        Running,
    }

    /// <summary>
    /// 每次 Tick 的上下文。用 readonly struct 按引用（in）传递，避免每帧每节点 GC 分配。
    /// </summary>
    public readonly struct TickContext
    {
        /// <summary>节点间共享数据的黑板</summary>
        public readonly Blackboard Blackboard;
        /// <summary>本帧步长（秒）</summary>
        public readonly float DeltaTime;
        /// <summary>行为树拥有者（Entity / MonoBehaviour / 自定义）。叶节点按需强转。</summary>
        public readonly object Owner;

        public TickContext(Blackboard blackboard, float deltaTime, object owner)
        {
            Blackboard = blackboard;
            DeltaTime = deltaTime;
            Owner = owner;
        }

        /// <summary>用新的步长派生上下文（黑板/Owner 不变）</summary>
        public TickContext WithDeltaTime(float dt) => new TickContext(Blackboard, dt, Owner);
    }

    /// <summary>
    /// 行为树节点抽象基类。
    /// <para>Tick 流程：首次进入 Running 触发 <see cref="OnEnter"/>；每帧调 <see cref="OnTick"/>；
    /// 从 Running 转为 Success/Failure 或被 <see cref="Abort"/> 时触发 <see cref="OnExit"/>。</para>
    /// <para>纯 C#，不依赖 MonoBehaviour。</para>
    /// </summary>
    public abstract class BTNode
    {
        /// <summary>节点名（调试用，可空）</summary>
        public string Name { get; internal set; }

        /// <summary>父节点（根节点为 null）</summary>
        public BTNode Parent { get; internal set; }

        /// <summary>上次 Tick 返回的状态（调试/快照用）</summary>
        public NodeStatus LastStatus { get; protected set; } = NodeStatus.Failure;

        /// <summary>当前是否处于 Running（已 OnEnter 但未 OnExit）</summary>
        protected bool IsRunning { get; private set; }

        /// <summary>
        /// 驱动节点一帧。由父节点/树调用，处理 Enter/Exit 生命周期，返回三态。
        /// </summary>
        public NodeStatus Tick(in TickContext context)
        {
            if (!IsRunning)
            {
                IsRunning = true;
                OnEnter(in context);
            }

            var status = OnTick(in context);
            LastStatus = status;

            if (status != NodeStatus.Running)
            {
                IsRunning = false;
                OnExit(in context);
            }

            return status;
        }

        /// <summary>首次进入（从非 Running 转 Running）时调用一次。</summary>
        protected virtual void OnEnter(in TickContext context) { }

        /// <summary>每帧执行体，返回三态。</summary>
        protected abstract NodeStatus OnTick(in TickContext context);

        /// <summary>节点结束（Success/Failure 或被中止）时调用一次。</summary>
        protected virtual void OnExit(in TickContext context) { }

        /// <summary>
        /// 中止节点：若正在 Running 则触发 OnExit 清理，并递归中止子节点。
        /// 供 Reactive 装饰器 / 树 Abort / 复合节点切换分支时调用。
        /// </summary>
        public virtual void Abort(in TickContext context)
        {
            if (IsRunning)
            {
                IsRunning = false;
                OnExit(in context);
            }
            LastStatus = NodeStatus.Failure;
        }

        /// <summary>
        /// 收集本节点及其子树（供快照/遍历）。基类只加自身，复合/装饰器重写追加子节点。
        /// </summary>
        internal virtual void CollectChildren(List<BTNode> buffer) { }
    }
}
