using System.Collections.Generic;

namespace PumpGF
{
    /// <summary>
    /// 反应式中止装饰器：监听一个黑板 Key，当其值发生变化时，
    /// <b>中止当前正在 Running 的子树并重新从头评估</b>。
    /// <para>这是行为树相对 FSM 在 Boss 战上的核心优势：例如监听 <c>HpRatio</c>，
    /// 被打到残血时立刻打断当前招式、切换到狂暴分支。</para>
    /// <para>实现采用每帧值比较（相较 R3 订阅无泄漏风险、零订阅分配），
    /// 值类型用默认相等比较器判断变化。</para>
    /// </summary>
    /// <typeparam name="T">被监听 Key 的值类型</typeparam>
    public sealed class ReactiveNode<T> : DecoratorNode
    {
        private readonly string _watchKey;
        private readonly IEqualityComparer<T> _comparer;
        private bool _hasLast;
        private T _lastValue;

        public ReactiveNode(string watchKey)
        {
            _watchKey = watchKey;
            _comparer = EqualityComparer<T>.Default;
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

            // 检测被监听 Key 的变化
            bool changed = false;
            if (context.Blackboard != null &&
                context.Blackboard.TryGet<T>(_watchKey, out var current))
            {
                if (!_hasLast || !_comparer.Equals(current, _lastValue))
                {
                    changed = _hasLast; // 首次读到不算变化，仅建立基线
                    _lastValue = current;
                    _hasLast = true;
                }
            }

            // 值变化 → 中止子树，令其下一帧从头重评估
            if (changed)
            {
                Child.Abort(in context);
            }

            return Child.Tick(in context);
        }
    }
}
