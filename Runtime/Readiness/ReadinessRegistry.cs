using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

namespace PumpGF
{
    /// <summary>
    /// 就绪注册表。把"等待某对象/某服务就绪"提升为<strong>一等公民</strong>操作，
    /// 替代散落的 <c>WaitUntil(polling)</c> 与基于 Awake/Start 顺序的隐式跨对象依赖，
    /// 从源头消除时序竞态。
    /// <para>
    /// <b>典型用法</b>：
    /// <code>
    /// // 生产者：在自己就绪后宣告
    /// GameGlobal.Readiness.MarkReady&lt;PlayerController&gt;(this);
    ///
    /// // 消费者：在任何地方安全等待，不再依赖"对方先 Start"
    /// var player = await GameGlobal.Readiness.WaitUntilReady&lt;PlayerController&gt;(cancel);
    /// player.Attack();
    /// </code>
    /// </para>
    /// <para><b>线程假设</b>：主线程调用（与 PumpGF 其他模块一致）。</para>
    /// </summary>
    public sealed class ReadinessRegistry : IModule
    {
        // ── 类型键（一类型一实例，适合 Manager / Controller 等单例语义对象） ──
        private readonly Dictionary<Type, object> _typeValues = new(16);
        private readonly Dictionary<Type, ITypeWaiters> _typeWaiters = new(4);

        // ── 字符串键（多实例 / 动态命名，如 "Enemy:1234" / "SceneBoot:Battle"） ──
        private readonly Dictionary<string, object> _keyValues = new(16);
        private readonly Dictionary<string, Queue<UniTaskCompletionSource<object>>> _keyWaiters = new(4);

        // ── 就绪事件流（非类型化，发出就绪的 key / 类型全名，供全局监听） ──
        private readonly Subject<string> _onReady = new();

        /// <summary>任意就绪事件流。OnNext 收到的是类型 FullName（类型键）或自定义 key（字符串键）。</summary>
        public Observable<string> OnReady => _onReady;

        // ──────────────────────────────────────────────
        //  IModule
        // ──────────────────────────────────────────────

        public void Init() { }

        public void Dispose()
        {
            // 取消所有尚未就绪的等待者
            foreach (var w in _typeWaiters.Values) w.CancelAll();
            _typeWaiters.Clear();

            foreach (var q in _keyWaiters.Values)
                while (q.Count > 0) q.Dequeue().TrySetCanceled();
            _keyWaiters.Clear();

            _typeValues.Clear();
            _keyValues.Clear();
            _onReady.Dispose();
        }

        // ──────────────────────────────────────────────
        //  类型键 API
        // ──────────────────────────────────────────────

        /// <summary>
        /// 标记类型 <typeparamref name="T"/> 的实例就绪，唤醒所有等待者。
        /// <para>覆盖语义：同类型再次 MarkReady 会替换旧实例（用于"重生/重连"场景）。</para>
        /// </summary>
        public void MarkReady<T>(T instance) where T : class
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            var t = typeof(T);
            _typeValues[t] = instance;

            if (_typeWaiters.TryGetValue(t, out var waiters))
            {
                _typeWaiters.Remove(t);
                waiters.ResolveAll(instance);
            }

            _onReady.OnNext(t.FullName);
            Log.Info("Readiness", $"MarkReady<{t.Name}>()");
        }

        /// <summary>类型 <typeparamref name="T"/> 是否已就绪</summary>
        public bool IsReady<T>() where T : class => _typeValues.ContainsKey(typeof(T));

        /// <summary>同步获取已就绪实例；未就绪返回 false（不阻塞）</summary>
        public bool TryGet<T>(out T value) where T : class
        {
            if (_typeValues.TryGetValue(typeof(T), out var boxed))
            {
                value = boxed as T;
                return value != null;
            }
            value = null;
            return false;
        }

        /// <summary>
        /// 等待类型 <typeparamref name="T"/> 就绪并返回实例。已就绪则同步完成（无挂起）。
        /// <para>使用 <see cref="UniTaskExtensions.AttachExternalCancellation"/>，
        /// 单个等待者的取消不会影响其他等待者或源 TCS。</para>
        /// </summary>
        public UniTask<T> WaitUntilReady<T>(CancellationToken ct = default) where T : class
        {
            var t = typeof(T);
            if (_typeValues.TryGetValue(t, out var boxed) && boxed is T ready)
                return UniTask.FromResult(ready);

            if (!_typeWaiters.TryGetValue(t, out var waiters))
            {
                waiters = new TypeWaiters<T>();
                _typeWaiters[t] = waiters;
            }
            var src = new UniTaskCompletionSource<T>();
            ((TypeWaiters<T>)waiters).Enqueue(src);
            // src 在取消后仍留在队列，至下一次 MarkReady 时被无害地 TrySetResult 后出队清理
            return src.Task.AttachExternalCancellation(ct);
        }

        /// <summary>清除类型 <typeparamref name="T"/> 的就绪标记（对象销毁 / 场景卸载时调用）。
        /// <para>不唤醒等待者——它们继续等待下一次 MarkReady。</para></summary>
        public void Clear<T>() where T : class => _typeValues.Remove(typeof(T));

        // ──────────────────────────────────────────────
        //  字符串键 API
        // ──────────────────────────────────────────────

        /// <summary>标记字符串 key 对应的实例就绪，唤醒所有等待该 key 的等待者。</summary>
        public void MarkReady(string key, object instance)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("key is null or empty", nameof(key));
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            _keyValues[key] = instance;

            if (_keyWaiters.TryGetValue(key, out var q) && q.Count > 0)
            {
                _keyWaiters.Remove(key);
                while (q.Count > 0) q.Dequeue().TrySetResult(instance);
            }

            _onReady.OnNext(key);
            Log.Info("Readiness", $"MarkReady('{key}')");
        }

        /// <summary>字符串 key 是否已就绪</summary>
        public bool IsReady(string key) => !string.IsNullOrEmpty(key) && _keyValues.ContainsKey(key);

        /// <summary>同步获取字符串 key 对应的已就绪实例</summary>
        public bool TryGet(string key, out object value)
        {
            if (!string.IsNullOrEmpty(key) && _keyValues.TryGetValue(key, out var v))
            {
                value = v;
                return true;
            }
            value = null;
            return false;
        }

        /// <summary>等待字符串 key 就绪并返回实例。已就绪则同步完成。</summary>
        public UniTask<object> WaitUntilReady(string key, CancellationToken ct = default)
        {
            if (_keyValues.TryGetValue(key, out var v)) return UniTask.FromResult(v);

            if (!_keyWaiters.TryGetValue(key, out var q))
            {
                q = new Queue<UniTaskCompletionSource<object>>();
                _keyWaiters[key] = q;
            }
            var src = new UniTaskCompletionSource<object>();
            q.Enqueue(src);
            return src.Task.AttachExternalCancellation(ct);
        }

        /// <summary>清除字符串 key 的就绪标记。</summary>
        public void Clear(string key)
        {
            if (!string.IsNullOrEmpty(key)) _keyValues.Remove(key);
        }

        /// <summary>清空所有就绪标记（场景整体切换时调用，防止跨场景脏数据）。
        /// <para>不唤醒等待者。</para></summary>
        public void ClearAll()
        {
            _typeValues.Clear();
            _keyValues.Clear();
        }

        // ──────────────────────────────────────────────
        //  等待者队列（类型化，避免每个等待者装箱）
        // ──────────────────────────────────────────────

        private interface ITypeWaiters
        {
            void ResolveAll(object instance);
            void CancelAll();
        }

        private sealed class TypeWaiters<T> : ITypeWaiters where T : class
        {
            private readonly Queue<UniTaskCompletionSource<T>> _q = new();

            public void Enqueue(UniTaskCompletionSource<T> src) => _q.Enqueue(src);

            public void ResolveAll(object instance)
            {
                var val = (T)instance;
                while (_q.Count > 0)
                {
                    var s = _q.Dequeue();
                    s.TrySetResult(val); // 已被外部取消的 src 此处无害返回 false
                }
            }

            public void CancelAll()
            {
                while (_q.Count > 0) _q.Dequeue().TrySetCanceled();
            }
        }
    }
}
