using System;
using System.Collections.Generic;

namespace PumpGF
{
    /// <summary>
    /// 反应式中止装饰器：监听一个黑板 Key，当满足「中止判定」时，
    /// <b>中止当前正在 Running 的子树并重新从头评估</b>。
    /// <para>这是行为树相对 FSM 在 Boss 战上的核心优势：例如监听 <c>HpRatio</c>，
    /// 跨越残血阈值时立刻打断当前招式、切换到狂暴分支。</para>
    /// <para>实现采用每帧值比较（相较 R3 订阅无泄漏风险、零订阅分配）。</para>
    /// <para><b>「变化」 vs 「阈值」——务必区分</b>：默认判定是「值发生任意变化即中止」，
    /// 这对 <b>离散状态 Key</b>（如 <c>IsEnraged: bool</c>、<c>Phase: int</c>）是正确的；
    /// 但对 <b>连续值 Key</b>（如每帧都在微小变化的 <c>HpRatio: float</c>）会导致子树几乎每帧被中止、
    /// 破坏 Running 连续性（招式动画反复被打断）。连续值 Key 请务必用带 <c>shouldAbort</c>
    /// 谓词的构造，仅在跨越业务关心的阈值时才返回 true。</para>
    /// </summary>
    /// <typeparam name="T">被监听 Key 的值类型</typeparam>
    public sealed class ReactiveNode<T> : DecoratorNode
    {
        private readonly string _watchKey;
        private readonly IEqualityComparer<T> _comparer;

        // 可选的中止判定谓词：给定 (旧值, 新值) 判断本次变化是否应触发中止。
        // 为 null 时退化为「值发生任意变化即中止」（适合离散状态 Key）。
        private readonly Func<T, T, bool> _shouldAbort;

        private bool _hasLast;
        private T _lastValue;

        /// <summary>
        /// 默认语义：被监听 Key 的值发生<b>任意变化</b>即中止子树重评估。
        /// <para>仅适合离散状态 Key（bool / enum / int 状态位等）。连续浮点 Key 请用带谓词的重载。</para>
        /// </summary>
        public ReactiveNode(string watchKey)
            : this(watchKey, null) { }

        /// <summary>
        /// 谓词语义：仅当 <paramref name="shouldAbort"/>(旧值, 新值) 返回 true 时才中止子树重评估。
        /// <para>适合连续值 Key，例如「跨越阈值」：<c>(oldV, newV) =&gt; oldV &gt;= 0.3f &amp;&amp; newV &lt; 0.3f</c>。</para>
        /// </summary>
        public ReactiveNode(string watchKey, Func<T, T, bool> shouldAbort)
        {
            _watchKey = watchKey;
            _comparer = EqualityComparer<T>.Default;
            _shouldAbort = shouldAbort;
        }

        protected override void OnEnter(in TickContext context)
        {
            // 进入时记录初值基线，避免首帧误判为"变化"
            _hasLast = context.Blackboard != null &&
                       context.Blackboard.TryGet<T>(_watchKey, out _lastValue);
        }

        protected override NodeStatus OnTick(in TickContext context)
        {
            if (Child == null) return NodeStatus.Failure;

            // 检测被监听 Key 的变化，并按判定决定是否中止
            bool abort = false;
            if (context.Blackboard != null &&
                context.Blackboard.TryGet<T>(_watchKey, out var current))
            {
                if (!_hasLast)
                {
                    // 首次读到：仅建立基线，不算变化
                    _lastValue = current;
                    _hasLast = true;
                }
                else if (!_comparer.Equals(current, _lastValue))
                {
                    // 值发生变化：无谓词 → 直接中止；有谓词 → 交由业务判定（如跨越阈值）
                    abort = _shouldAbort == null || _shouldAbort(_lastValue, current);
                    _lastValue = current;
                }
            }

            // 满足中止判定 → 中止子树，令其下一帧从头重评估
            if (abort)
            {
                Child.Abort(in context);
            }

            return Child.Tick(in context);
        }
    }
}
