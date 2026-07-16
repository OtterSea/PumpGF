using System;
using System.Collections.Generic;
using R3;

namespace PumpGF
{
    /// <summary>
    /// 命名事件域。与全局 EventBus 隔离，支持按域批量 Clear/Dispose。
    /// 用于场景/玩法期事件隔离（如 "Battle" 域，退出战斗时一键清空）。
    /// </summary>
    public sealed class EventDomain : IDisposable
    {
        // 独立路由表
        private readonly Dictionary<Type, object> _subjects = new();

        /// <summary>域名</summary>
        public string Name { get; }

        internal EventDomain(string name)
        {
            Name = name;
        }

        /// <summary>域内发布事件</summary>
        public void Publish<TEvent>(TEvent evt) where TEvent : struct
        {
            if (_subjects.TryGetValue(typeof(TEvent), out var subj))
            {
                ((Subject<TEvent>)subj).OnNext(evt);
            }
        }

        /// <summary>域内订阅事件</summary>
        public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : struct
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var subject = GetOrCreateSubject<TEvent>();
            return subject.Subscribe(handler);
        }

        /// <summary>域内订阅事件（防闭包版本）</summary>
        public IDisposable SubscribeWithState<TEvent, TState>(
            TState state, Action<TState, TEvent> handler)
            where TEvent : struct
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var subject = GetOrCreateSubject<TEvent>();
            return subject.Subscribe(e => handler(state, e));
        }

        /// <summary>域内 R3 Observable</summary>
        public Observable<TEvent> OnEvent<TEvent>() where TEvent : struct
        {
            return GetOrCreateSubject<TEvent>();
        }

        /// <summary>清空域内所有订阅（域对象保留，可重新订阅）</summary>
        public void Clear()
        {
            foreach (var kvp in _subjects)
            {
                (kvp.Value as IDisposable)?.Dispose();
            }
            _subjects.Clear();
        }

        /// <summary>清空 + 标记已释放</summary>
        public void Dispose()
        {
            Clear();
        }

        // ── 内部 ──

        private Subject<TEvent> GetOrCreateSubject<TEvent>() where TEvent : struct
        {
            var type = typeof(TEvent);
            if (!_subjects.TryGetValue(type, out var subj))
            {
                subj = new Subject<TEvent>();
                _subjects[type] = subj;
            }
            return (Subject<TEvent>)subj;
        }

        internal IReadOnlyList<EventSubscriptionInfo> GetSubscriptionInfo()
        {
            var list = new List<EventSubscriptionInfo>();
            foreach (var kvp in _subjects)
            {
                list.Add(new EventSubscriptionInfo(kvp.Key, Name, 0));
            }
            return list;
        }
    }
}
