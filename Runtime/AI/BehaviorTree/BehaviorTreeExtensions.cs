using System;

namespace PumpGF
{
    /// <summary>
    /// BehaviorTree 扩展方法。
    /// </summary>
    public static class BehaviorTreeExtensions
    {
        /// <summary>
        /// 绑定到 Lifecycle 更新通道，自动 Tick（暂停感知）。
        /// <para>返回 IDisposable，Dispose 即解绑（配合 R3 <c>AddTo(this)</c> 管理生命周期）。</para>
        /// <para>建议绑定 <see cref="UpdateChannel.Logic"/>：暂停时不 Tick，AI 冻结（与 FSM 一致）。</para>
        /// </summary>
        public static IDisposable BindToLifecycle(this BehaviorTree tree, UpdateChannel channel)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            if (GameGlobal.LifecycleMgr == null)
            {
                Log.Warning("BT", "LifecycleMgr 未初始化，无法绑定 Tick。");
                return EmptyDisposable.Instance;
            }
            // 注意：RegisterTick 期望 Action<float>，而 tree.Tick 返回 NodeStatus，
            // 不能直接传方法组（CS0407），用 lambda 包一层丢弃返回值。
            return GameGlobal.LifecycleMgr.RegisterTick(channel, dt => tree.Tick(dt));
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public static readonly EmptyDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
