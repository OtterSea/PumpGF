using System;

namespace PumpGF
{
    /// <summary>
    /// StateMachine 扩展方法。
    /// </summary>
    public static class StateMachineExtensions
    {
        /// <summary>
        /// 绑定到 Lifecycle 更新通道，自动 Tick（暂停感知）。
        /// <para>返回 IDisposable，Dispose 即解绑（配合 R3 <c>AddTo(this)</c> 管理生命周期）。</para>
        /// </summary>
        public static IDisposable BindToLifecycle(this StateMachine sm, UpdateChannel channel)
        {
            if (GameGlobal.LifecycleMgr == null)
            {
                Log.Warning("FSM", "LifecycleMgr 未初始化，无法绑定 Tick。");
                return EmptyDisposable.Instance;
            }
            return GameGlobal.LifecycleMgr.RegisterTick(channel, sm.Tick);
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public static readonly EmptyDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
