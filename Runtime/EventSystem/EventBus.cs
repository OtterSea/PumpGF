using System;
using System.Collections.Generic;
using R3;

namespace PumpGF
{
    /// <summary>
    /// 事件订阅信息（用于 Debug 面板查询）。
    /// </summary>
    public readonly struct EventSubscriptionInfo
    {
        /// <summary>事件类型</summary>
        public readonly Type EventType;
        /// <summary>域名（null 表示全局）</summary>
        public readonly string DomainName;
        /// <summary>订阅者数量</summary>
        public readonly int SubscriberCount;

        public EventSubscriptionInfo(Type eventType, string domainName, int subscriberCount)
        {
            EventType = eventType;
            DomainName = domainName;
            SubscriberCount = subscriberCount;
        }
    }

    /// <summary>
    /// 类型安全的事件路由中枢。以事件类型（struct）为路由 key，编译期类型检查。
    /// 支持全局总线与命名域（EventDomain）隔离。
    /// </summary>
    public sealed class EventBus : IModule
    {
        // 全局路由表：Type → Subject<TEvent>（装箱一次存入，派发不装箱）
        private readonly Dictionary<Type, object> _subjects = new();
        // 命名域表
        private readonly Dictionary<string, EventDomain> _domains = new();
        // 递归检测：正在派发的事件类型
        private readonly HashSet<Type> _dispatching = new();

        public void Init() { }

        // ──────────────────────────────────────────────
        //  全局发布/订阅
        // ──────────────────────────────────────────────

        /// <summary>
        /// 发布事件。同步派发给所有订阅者。
        /// 递归 Publish 同类型事件会 Warning（不阻止）。
        /// </summary>
        public void Publish<TEvent>(TEvent evt) where TEvent : struct
        {
            var type = typeof(TEvent);
            if (_dispatching.Contains(type))
            {
                Log.Warning("EventBus", $"递归 Publish 同类型事件: {type.Name}（不阻止，业务自负）");
            }
            _dispatching.Add(type);
            try
            {
                if (_subjects.TryGetValue(type, out var subj))
                {
                    ((Subject<TEvent>)subj).OnNext(evt);
                }
            }
            finally
            {
                _dispatching.Remove(type);
            }
        }

        /// <summary>
        /// 订阅事件。返回 IDisposable，Dispose 即取消订阅。
        /// 必须绑定生命周期（AddTo/RegisterTo/DisposableBag）。
        /// </summary>
        public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : struct
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var subject = GetOrCreateSubject<TEvent>();
            return subject.Subscribe(handler);
        }

        /// <summary>
        /// 订阅事件（低闭包版本）。显式传入 state，通过自定义 Observer 承载 state + handler 引用，
        /// 避免为每次 Publish 分配捕获 <c>state</c> 与 <c>handler</c> 的 lambda 闭包。
        /// <para>说明：仍会为订阅本身分配 1 个 <see cref="StateObserver{TEvent,TState}"/> 对象；
        /// 但相比 <see cref="Subscribe{TEvent}(Action{TEvent})"/> + 闭包 lambda，热路径的 GC 更可控。</para>
        /// </summary>
        public IDisposable SubscribeWithState<TEvent, TState>(
            TState state, Action<TState, TEvent> handler)
            where TEvent : struct
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var subject = GetOrCreateSubject<TEvent>();
            var observer = new StateObserver<TEvent, TState>(state, handler);
            return subject.Subscribe(observer);
        }

        /// <summary>
        /// 承载 state + handler 的自定义观察者，避免 lambda 每次派发时闭包分配。
        /// </summary>
        private sealed class StateObserver<TEvent, TState> : Observer<TEvent>
            where TEvent : struct
        {
            private readonly TState _state;
            private readonly Action<TState, TEvent> _handler;

            public StateObserver(TState state, Action<TState, TEvent> handler)
            {
                _state = state;
                _handler = handler;
            }

            protected override void OnNextCore(TEvent value) => _handler(_state, value);
            protected override void OnErrorResumeCore(Exception error)
            {
                Log.Error("EventBus", $"StateObserver OnErrorResume: {error.Message}");
            }
            protected override void OnCompletedCore(Result result) { /* 事件流不常 Completed */ }
        }

        /// <summary>
        /// 暴露 R3 Observable，可链式操作（Where/Throttle/CombineLatest 等）。
        /// </summary>
        public Observable<TEvent> OnEvent<TEvent>() where TEvent : struct
        {
            return GetOrCreateSubject<TEvent>();
        }

        // ──────────────────────────────────────────────
        //  命名域
        // ──────────────────────────────────────────────

        /// <summary>
        /// 获取或创建命名事件域。同类型事件在不同域互不干扰。
        /// </summary>
        public EventDomain GetDomain(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Domain name cannot be null or empty.", nameof(name));
            if (!_domains.TryGetValue(name, out var domain))
            {
                domain = new EventDomain(name);
                _domains[name] = domain;
            }
            return domain;
        }

        // ──────────────────────────────────────────────
        //  查询（Debug）
        // ──────────────────────────────────────────────

        /// <summary>
        /// 获取所有事件订阅信息（用于 Debug 面板）。
        /// </summary>
        public IReadOnlyList<EventSubscriptionInfo> GetSubscriptionInfo()
        {
            var list = new List<EventSubscriptionInfo>();
            foreach (var kvp in _subjects)
            {
                list.Add(new EventSubscriptionInfo(kvp.Key, null, 0));
            }
            foreach (var kvp in _domains)
            {
                foreach (var info in kvp.Value.GetSubscriptionInfo())
                {
                    list.Add(info);
                }
            }
            return list;
        }

        // ──────────────────────────────────────────────
        //  内部
        // ──────────────────────────────────────────────

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

        public void Dispose()
        {
            foreach (var kvp in _subjects)
            {
                (kvp.Value as IDisposable)?.Dispose();
            }
            _subjects.Clear();

            foreach (var kvp in _domains)
            {
                kvp.Value.Dispose();
            }
            _domains.Clear();
            _dispatching.Clear();
        }
    }
}
