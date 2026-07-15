using System;
using Cysharp.Threading.Tasks;

namespace PumpGF
{
    /// <summary>
    /// 单个定时器的控制句柄。支持暂停/恢复/取消。
    /// <see cref="Scheduler"/> 的 <c>Schedule</c> 系列方法返回此接口（兼容 <see cref="IDisposable"/>，
    /// 可配合 R3 的 <c>AddTo</c> 绑定 MonoBehaviour 生命周期）。
    /// </summary>
    public interface ITimerHandle : IDisposable
    {
        /// <summary>是否已暂停</summary>
        bool IsPaused { get; }

        /// <summary>暂停定时器（不销毁，恢复后继续计时）</summary>
        void Pause();

        /// <summary>恢复暂停的定时器</summary>
        void Resume();

        /// <summary>取消定时器（等同 <see cref="Dispose"/>）</summary>
        void Cancel();
    }

    /// <summary>定时器类型</summary>
    internal enum TimerKind
    {
        /// <summary>延迟执行一次</summary>
        OneShot,
        /// <summary>周期执行</summary>
        Repeat,
        /// <summary>帧驱动（每 N 帧执行一次）</summary>
        Frame
    }

    /// <summary>
    /// 内部定时器数据载体。由 <see cref="Scheduler"/> 的对象池统一管理复用，
    /// 业务层不直接持有此对象，而是通过 <see cref="TimerHandle"/> 间接控制。
    /// </summary>
    internal sealed class Timer
    {
        public TimerKind Kind;
        public UpdateChannel Channel;
        public bool IsUnscaled;
        public Action Callback;
        public float Interval;
        public float Accumulator;
        public int RemainingCount;   // -1 表示无限
        public int FrameInterval;
        public int FrameCounter;
        public bool IsPaused;
        public bool IsDisposed;
        public UniTaskCompletionSource Tcs;
        public TimerGroup Group;
        public Scheduler Owner;

        /// <summary>已归还对象池标记。归池后为 true，<see cref="Scheduler.AcquireTimer"/> 取出时置 false。
        /// 用于 tick 跳过已归池的定时器，以及幂等防止重复归池。</summary>
        public bool IsReturned;

        /// <summary>
        /// 版本号，每次从池中取出时递增。用于防止句柄在定时器被回收复用后误操作。
        /// </summary>
        public int Version;

        /// <summary>从池中取出后重置业务字段为默认值（Version 与 IsReturned 由 Scheduler 管理）。</summary>
        public void Reset()
        {
            Kind = TimerKind.OneShot;
            Channel = UpdateChannel.Default;
            IsUnscaled = false;
            Callback = null;
            Interval = 0f;
            Accumulator = 0f;
            RemainingCount = -1;
            FrameInterval = 1;
            FrameCounter = 0;
            IsPaused = false;
            IsDisposed = false;
            Tcs = null;
            Group = null;
        }

        /// <summary>取消并归还对象池。幂等。</summary>
        public void CancelInternal()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            Tcs?.TrySetCanceled();
            var owner = Owner;
            Owner = null;
            Callback = null;
            Tcs = null;
            owner?.ReturnTimer(this);
        }
    }

    /// <summary>
    /// 返回给调用方的定时器句柄。包装内部 <see cref="Timer"/>，通过版本号校验防止
    /// 在定时器被回收复用后误操作。实现 <see cref="ITimerHandle"/>。
    /// </summary>
    public sealed class TimerHandle : ITimerHandle
    {
        private Timer _timer;
        private readonly int _version;

        internal TimerHandle(Timer timer)
        {
            _timer = timer;
            _version = timer != null ? timer.Version : 0;
        }

        /// <summary>当前定时器是否已暂停（已取消后恒为 false）</summary>
        public bool IsPaused
        {
            get
            {
                var t = _timer;
                return t != null && t.Version == _version && !t.IsDisposed && t.IsPaused;
            }
        }

        /// <summary>暂停定时器</summary>
        public void Pause()
        {
            var t = _timer;
            if (t != null && t.Version == _version && !t.IsDisposed)
                t.IsPaused = true;
        }

        /// <summary>恢复定时器</summary>
        public void Resume()
        {
            var t = _timer;
            if (t != null && t.Version == _version && !t.IsDisposed)
                t.IsPaused = false;
        }

        /// <summary>取消定时器（幂等，取消后句柄失效）</summary>
        public void Cancel()
        {
            var t = _timer;
            _timer = null;
            if (t != null && t.Version == _version && !t.IsDisposed)
                t.CancelInternal();
        }

        /// <summary>等同 <see cref="Cancel"/></summary>
        public void Dispose() => Cancel();
    }
}
