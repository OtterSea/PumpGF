using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Pool;

namespace PumpGF
{
    /// <summary>
    /// 游戏定时任务的统一调度器。所有延迟执行、周期执行、帧驱动任务经此调度，
    /// 自动感知 <see cref="LifecycleMgr"/> 的暂停与时间缩放（通过订阅 Update 通道）。
    /// 提供 Awaitable（<see cref="Delay"/> 返回 <see cref="UniTask"/>）与回调注册
    /// （<see cref="Schedule"/> 返回 <see cref="ITimerHandle"/>）两种 API 形态。
    /// </summary>
    /// <remarks>
    /// 设计要点：
    /// <list type="bullet">
    /// <item>时间源：订阅 Lifecycle 的 Logic/Default 通道 tick，暂停时 dt=0 自然冻结，缩放自动跟随。</item>
    /// <item>Unscaled 定时器：订阅 Default 通道但累加 <c>Time.unscaledDeltaTime</c>，不受暂停/缩放影响。</item>
    /// <item>Frame-based：用帧计数器（非时间累加），dt&gt;0 时才推进（暂停冻结）。</item>
    /// <item>池化：<see cref="Timer"/> 对象统一池化复用；归池通过 <see cref="Timer.IsReturned"/> 标记幂等。</item>
    /// </list>
    /// </remarks>
    public sealed class Scheduler : IModule
    {
        private LifecycleMgr _lifecycle;
        private ObjectPool<Timer> _pool;
        private IDisposable _logicTickSub;
        private IDisposable _defaultTickSub;

        /// <summary>按通道分桶的普通定时器（Logic/Default/UI 等，含 Frame-based）</summary>
        private readonly Dictionary<UpdateChannel, List<Timer>> _buckets = new();

        /// <summary>Unscaled 定时器（订阅 Default 通道 tick，但累加 unscaledDeltaTime）</summary>
        private readonly List<Timer> _unscaledTimers = new(16);

        private readonly List<TimerGroup> _groups = new(8);

        /// <summary>当前活跃定时器总数（不含已取消待回收的）</summary>
        public int ActiveTimerCount { get; private set; }

        // ──────────────────────────────────────────────
        //  IModule
        // ──────────────────────────────────────────────

        public void Init()
        {
            _lifecycle = GameGlobal.LifecycleMgr;

            _pool = new ObjectPool<Timer>(
                createFunc: () => new Timer(),
                actionOnGet: null,
                actionOnRelease: null,
                collectionCheck: false
            );

            // 订阅 Lifecycle 通道。Logic 固定步长驱动逻辑定时器；
            // Default 每帧驱动 Default 通道定时器 + Unscaled 定时器。
            _logicTickSub = _lifecycle.RegisterTick(UpdateChannel.Logic, dt => TickBucket(UpdateChannel.Logic, dt));
            _defaultTickSub = _lifecycle.RegisterTick(UpdateChannel.Default, dt =>
            {
                TickBucket(UpdateChannel.Default, dt);
                TickUnscaled();
            });
        }

        public void Dispose()
        {
            _logicTickSub?.Dispose();
            _defaultTickSub?.Dispose();
            _logicTickSub = null;
            _defaultTickSub = null;

            // 归池所有定时器
            foreach (var kvp in _buckets)
            {
                for (int i = 0; i < kvp.Value.Count; i++)
                    kvp.Value[i].CancelInternal();
            }
            _buckets.Clear();

            for (int i = 0; i < _unscaledTimers.Count; i++)
                _unscaledTimers[i].CancelInternal();
            _unscaledTimers.Clear();

            for (int i = _groups.Count - 1; i >= 0; i--)
                _groups[i].Dispose();
            _groups.Clear();

            ActiveTimerCount = 0;
        }

        // ──────────────────────────────────────────────
        //  Awaitable API
        // ──────────────────────────────────────────────

        /// <summary>延迟指定秒数后完成（可 await）。暂停时计时冻结，缩放自动跟随。</summary>
        /// <param name="seconds">延迟秒数（&lt;0 钳制为 0，立即触发）</param>
        /// <param name="channel">更新通道（默认 Logic，固定步长）</param>
        /// <param name="owner">绑定的 GameObject，销毁时自动取消</param>
        /// <param name="ct">取消令牌</param>
        public UniTask Delay(float seconds,
            UpdateChannel channel = UpdateChannel.Logic,
            GameObject owner = null,
            CancellationToken ct = default)
        {
            float interval = ValidateInterval(seconds);
            var timer = AcquireTimer();
            timer.Kind = TimerKind.OneShot;
            timer.Channel = channel;
            timer.Interval = interval;
            timer.RemainingCount = 1;
            var tcs = new UniTaskCompletionSource();
            timer.Tcs = tcs;
            AddTimer(timer);
            BindCancellation(timer, owner, ct);
            return tcs.Task;
        }

        /// <summary>延迟指定帧数后完成（可 await）。</summary>
        /// <param name="frames">延迟帧数（&lt;0 钳制为 0）</param>
        /// <param name="channel">更新通道（默认 Default，每帧）</param>
        /// <param name="owner">绑定的 GameObject</param>
        /// <param name="ct">取消令牌</param>
        public UniTask DelayFrames(int frames,
            UpdateChannel channel = UpdateChannel.Default,
            GameObject owner = null,
            CancellationToken ct = default)
        {
            if (frames < 0) frames = 0;
            var timer = AcquireTimer();
            timer.Kind = TimerKind.Frame;
            timer.Channel = channel;
            timer.FrameInterval = frames > 0 ? frames : 1;
            timer.RemainingCount = 1;
            var tcs = new UniTaskCompletionSource();
            timer.Tcs = tcs;
            AddTimer(timer);
            BindCancellation(timer, owner, ct);
            return tcs.Task;
        }

        /// <summary>延迟指定秒数后完成（不受暂停/缩放影响）。</summary>
        public UniTask DelayUnscaled(float seconds,
            GameObject owner = null,
            CancellationToken ct = default)
        {
            float interval = ValidateInterval(seconds);
            var timer = AcquireTimer();
            timer.Kind = TimerKind.OneShot;
            timer.Channel = UpdateChannel.Default;
            timer.IsUnscaled = true;
            timer.Interval = interval;
            timer.RemainingCount = 1;
            var tcs = new UniTaskCompletionSource();
            timer.Tcs = tcs;
            AddTimer(timer);
            BindCancellation(timer, owner, ct);
            return tcs.Task;
        }

        // ──────────────────────────────────────────────
        //  回调注册 API（One-shot）
        // ──────────────────────────────────────────────

        /// <summary>延迟执行一次回调。返回句柄，Dispose 即取消。</summary>
        public ITimerHandle Schedule(float seconds, Action callback,
            UpdateChannel channel = UpdateChannel.Logic,
            GameObject owner = null)
        {
            var timer = CreateTimer(seconds, callback, channel, TimerKind.OneShot, -1, 0);
            BindOwner(timer, owner);
            return new TimerHandle(timer);
        }

        /// <summary>延迟执行一次回调（不受暂停/缩放影响）。</summary>
        public ITimerHandle ScheduleUnscaled(float seconds, Action callback,
            GameObject owner = null)
        {
            var timer = CreateTimer(seconds, callback, UpdateChannel.Default,
                TimerKind.OneShot, -1, 0, isUnscaled: true);
            BindOwner(timer, owner);
            return new TimerHandle(timer);
        }

        // ──────────────────────────────────────────────
        //  回调注册 API（Repeat）
        // ──────────────────────────────────────────────

        /// <summary>周期执行回调。count 为 -1 表示无限，&gt;0 执行指定次数后自动取消。</summary>
        public ITimerHandle ScheduleRepeat(float interval, Action callback, int count = -1,
            UpdateChannel channel = UpdateChannel.Logic,
            GameObject owner = null)
        {
            var timer = CreateTimer(interval, callback, channel, TimerKind.Repeat, count, 0);
            BindOwner(timer, owner);
            return new TimerHandle(timer);
        }

        /// <summary>周期执行回调（不受暂停/缩放影响）。</summary>
        public ITimerHandle ScheduleRepeatUnscaled(float interval, Action callback, int count = -1,
            GameObject owner = null)
        {
            var timer = CreateTimer(interval, callback, UpdateChannel.Default,
                TimerKind.Repeat, count, 0, isUnscaled: true);
            BindOwner(timer, owner);
            return new TimerHandle(timer);
        }

        // ──────────────────────────────────────────────
        //  回调注册 API（Frame-based）
        // ──────────────────────────────────────────────

        /// <summary>每 frameInterval 帧执行一次回调（默认每帧）。暂停时冻结。</summary>
        public ITimerHandle ScheduleEveryFrame(Action callback, int frameInterval = 1,
            UpdateChannel channel = UpdateChannel.Default,
            GameObject owner = null)
        {
            var timer = CreateTimer(0f, callback, channel, TimerKind.Frame, -1, frameInterval);
            BindOwner(timer, owner);
            return new TimerHandle(timer);
        }

        // ──────────────────────────────────────────────
        //  分组
        // ──────────────────────────────────────────────

        /// <summary>创建命名定时器分组，用于批量管理。</summary>
        public TimerGroup CreateGroup(string name)
        {
            var group = new TimerGroup(this);
            _groups.Add(group);
            Log.Info("Scheduler", $"创建定时器分组: {name}");
            return group;
        }

        // ──────────────────────────────────────────────
        //  内部：定时器创建/回收（供 TimerGroup 复用）
        // ──────────────────────────────────────────────

        /// <summary>创建并注册一个回调定时器。</summary>
        internal Timer CreateTimer(float seconds, Action callback, UpdateChannel channel,
            TimerKind kind, int count, int frameInterval, bool isUnscaled = false)
        {
            float interval = ValidateInterval(seconds);
            if (frameInterval < 1) frameInterval = 1;
            if (count < -1) count = -1;

            var timer = AcquireTimer();
            timer.Kind = kind;
            timer.Channel = isUnscaled ? UpdateChannel.Default : channel;
            timer.IsUnscaled = isUnscaled;
            timer.Callback = callback;
            timer.Interval = interval;
            timer.RemainingCount = count;
            timer.FrameInterval = frameInterval;
            AddTimer(timer);
            return timer;
        }

        internal void RemoveGroup(TimerGroup group)
        {
            _groups.Remove(group);
        }

        private Timer AcquireTimer()
        {
            var timer = _pool.Get();
            timer.Reset();
            timer.Version++;
            timer.IsReturned = false;
            timer.Owner = this;
            return timer;
        }

        private void ReturnTimer(Timer timer)
        {
            if (timer == null || timer.IsReturned) return;
            timer.IsReturned = true;
            timer.Owner = null;
            timer.Callback = null;
            timer.Tcs = null;
            timer.Group = null;
            if (ActiveTimerCount > 0) ActiveTimerCount--;
            _pool.Release(timer);
        }

        private void AddTimer(Timer timer)
        {
            ActiveTimerCount++;
            if (timer.IsUnscaled)
            {
                _unscaledTimers.Add(timer);
                return;
            }
            if (!_buckets.TryGetValue(timer.Channel, out var list))
            {
                list = new List<Timer>(16);
                _buckets[timer.Channel] = list;
            }
            list.Add(timer);
        }

        // ──────────────────────────────────────────────
        //  取消绑定
        // ──────────────────────────────────────────────

        private void BindCancellation(Timer timer, GameObject owner, CancellationToken ct)
        {
            bool ctActive = ct.CanBeCanceled;
            bool ownerActive = owner != null;
            if (!ctActive && !ownerActive) return;

            // 闭包分配可接受：Delay 不在每帧热路径
            if (ctActive)
                ct.Register(() => timer.CancelInternal());
            if (ownerActive)
            {
                var ownerCt = owner.destroyCancellationToken;
                if (ownerCt.CanBeCanceled)
                    ownerCt.Register(() => timer.CancelInternal());
            }
        }

        private void BindOwner(Timer timer, GameObject owner)
        {
            if (owner == null) return;
            var ownerCt = owner.destroyCancellationToken;
            if (ownerCt.CanBeCanceled)
                ownerCt.Register(() => timer.CancelInternal());
        }

        // ──────────────────────────────────────────────
        //  Tick 驱动
        // ──────────────────────────────────────────────

        private void TickBucket(UpdateChannel channel, float dt)
        {
            if (!_buckets.TryGetValue(channel, out var list)) return;

            int count = list.Count;
            for (int i = 0; i < count; i++)
            {
                var t = list[i];
                if (t.IsDisposed || t.IsReturned || t.IsPaused) continue;

                if (t.Kind == TimerKind.Frame)
                {
                    // 帧驱动：dt<=0（暂停或缩放为 0）时不推进，自然冻结
                    if (dt <= 0f) continue;
                    t.FrameCounter++;
                    if (t.FrameCounter >= t.FrameInterval)
                    {
                        t.FrameCounter = 0;
                        TriggerTimer(t);
                        if (!t.IsDisposed)
                        {
                            if (t.RemainingCount > 0 && --t.RemainingCount == 0)
                                t.CancelInternal();
                        }
                    }
                }
                else
                {
                    // 时间累加驱动
                    t.Accumulator += dt;
                    if (t.Accumulator >= t.Interval)
                    {
                        t.Accumulator -= t.Interval;
                        TriggerTimer(t);
                        if (!t.IsDisposed)
                        {
                            if (t.Kind == TimerKind.OneShot)
                            {
                                t.CancelInternal();
                            }
                            else if (t.RemainingCount > 0 && --t.RemainingCount == 0)
                            {
                                t.CancelInternal();
                            }
                        }
                    }
                }
            }

            CompactAndReturn(list);
        }

        private void TickUnscaled()
        {
            if (_unscaledTimers.Count == 0) return;
            float dt = _lifecycle.GetUnscaledDeltaTime();

            int count = _unscaledTimers.Count;
            for (int i = 0; i < count; i++)
            {
                var t = _unscaledTimers[i];
                if (t.IsDisposed || t.IsReturned || t.IsPaused) continue;

                t.Accumulator += dt;
                if (t.Accumulator >= t.Interval)
                {
                    t.Accumulator -= t.Interval;
                    TriggerTimer(t);
                    if (!t.IsDisposed)
                    {
                        if (t.Kind == TimerKind.OneShot)
                        {
                            t.CancelInternal();
                        }
                        else if (t.RemainingCount > 0 && --t.RemainingCount == 0)
                        {
                            t.CancelInternal();
                        }
                    }
                }
            }

            CompactAndReturn(_unscaledTimers);
        }

        private void TriggerTimer(Timer t)
        {
            try
            {
                if (t.Tcs != null)
                    t.Tcs.TrySetResult();
                t.Callback?.Invoke();
            }
            catch (Exception e)
            {
                Log.Error("Scheduler", $"定时器回调异常: {e}");
            }
        }

        /// <summary>紧凑化列表：移除已取消/已归池的定时器并归还对象池（零分配）。</summary>
        private void CompactAndReturn(List<Timer> list)
        {
            int write = 0;
            for (int read = 0; read < list.Count; read++)
            {
                var t = list[read];
                if (t.IsDisposed || t.IsReturned)
                {
                    ReturnTimer(t);
                }
                else
                {
                    if (write != read) list[write] = list[read];
                    write++;
                }
            }
            if (write < list.Count)
                list.RemoveRange(write, list.Count - write);
        }

        // ──────────────────────────────────────────────
        //  校验
        // ──────────────────────────────────────────────

        private static float ValidateInterval(float seconds)
        {
            if (seconds < 0f)
            {
                Log.Warning("Scheduler", $"定时器间隔为负({seconds})，已钳制为 0。");
                return 0f;
            }
            return seconds;
        }
    }
}
