using System;
using System.Collections.Generic;

namespace PumpGF
{
    /// <summary>
    /// 定时器分组。用于批量管理一组相关定时器（如某个玩法期内的全部计时），
    /// 支持 <see cref="CancelAll"/> / <see cref="PauseAll"/> / <see cref="ResumeAll"/>。
    /// 通过 <see cref="Scheduler.CreateGroup"/> 创建，<see cref="Dispose"/> 后从 Scheduler 注销。
    /// </summary>
    public sealed class TimerGroup : IDisposable
    {
        private readonly Scheduler _scheduler;
        private readonly List<Timer> _timers = new(16);
        private bool _disposed;

        internal TimerGroup(Scheduler owner)
        {
            _scheduler = owner;
        }

        /// <summary>组内有效定时器数量</summary>
        public int Count
        {
            get
            {
                Compact();
                return _timers.Count;
            }
        }

        /// <summary>组是否已释放</summary>
        public bool IsDisposed => _disposed;

        // ──────────────────────────────────────────────
        //  注册（委托 Scheduler 创建）
        // ──────────────────────────────────────────────

        /// <summary>延迟执行一次（One-shot）</summary>
        public ITimerHandle Schedule(float seconds, Action callback,
            UpdateChannel channel = UpdateChannel.Logic)
        {
            var timer = _scheduler.CreateTimer(seconds, callback, channel, TimerKind.OneShot, -1, 0);
            AddInternal(timer);
            return new TimerHandle(timer);
        }

        /// <summary>周期执行（Repeat）。count 为 -1 表示无限</summary>
        public ITimerHandle ScheduleRepeat(float interval, Action callback, int count = -1,
            UpdateChannel channel = UpdateChannel.Logic)
        {
            var timer = _scheduler.CreateTimer(interval, callback, channel, TimerKind.Repeat, count, 0);
            AddInternal(timer);
            return new TimerHandle(timer);
        }

        /// <summary>帧驱动（每 frameInterval 帧执行一次）</summary>
        public ITimerHandle ScheduleEveryFrame(Action callback, int frameInterval = 1,
            UpdateChannel channel = UpdateChannel.Default)
        {
            var timer = _scheduler.CreateTimer(0f, callback, channel, TimerKind.Frame, -1, frameInterval);
            AddInternal(timer);
            return new TimerHandle(timer);
        }

        // ──────────────────────────────────────────────
        //  批量操作
        // ──────────────────────────────────────────────

        /// <summary>取消组内所有定时器</summary>
        public void CancelAll()
        {
            if (_disposed) return;
            for (int i = 0; i < _timers.Count; i++)
                _timers[i].CancelInternal();
            _timers.Clear();
        }

        /// <summary>暂停组内所有定时器（不销毁）</summary>
        public void PauseAll()
        {
            if (_disposed) return;
            for (int i = 0; i < _timers.Count; i++)
            {
                var t = _timers[i];
                if (!t.IsDisposed) t.IsPaused = true;
            }
        }

        /// <summary>恢复组内所有定时器</summary>
        public void ResumeAll()
        {
            if (_disposed) return;
            for (int i = 0; i < _timers.Count; i++)
            {
                var t = _timers[i];
                if (!t.IsDisposed) t.IsPaused = false;
            }
        }

        /// <summary>等同 <see cref="CancelAll"/>，并从 Scheduler 注销本组</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CancelAll();
            _scheduler.RemoveGroup(this);
        }

        // ──────────────────────────────────────────────
        //  内部
        // ──────────────────────────────────────────────

        private void AddInternal(Timer timer)
        {
            if (_disposed)
            {
                timer.CancelInternal();
                return;
            }
            timer.Group = this;
            _timers.Add(timer);
        }

        /// <summary>移除已取消的定时器（零分配紧凑化）</summary>
        private void Compact()
        {
            int write = 0;
            for (int read = 0; read < _timers.Count; read++)
            {
                if (!_timers[read].IsDisposed)
                {
                    if (write != read) _timers[write] = _timers[read];
                    write++;
                }
            }
            if (write < _timers.Count)
                _timers.RemoveRange(write, _timers.Count - write);
        }
    }
}
