using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 行为树构建器：流畅链式 API 构建 <see cref="BehaviorTree"/>。
    /// <para>复合节点（Sequence/Selector/Parallel/UtilitySelector）需以 <see cref="End"/> 收尾；
    /// 装饰器（Inverter/Repeat/Cooldown/Condition/Reactive）作用于其后紧跟的一个节点/子树。</para>
    /// </summary>
    public sealed class BehaviorTreeBuilder
    {
        // 一个正在构建的复合节点条目：composite 接收子节点，mount 是其挂到父级时的对外对象（可能被装饰器包裹）
        private struct CompositeFrame
        {
            public CompositeNode Composite;
            public BTNode Mount;
        }

        // 复合节点栈：栈顶为当前正在填充子节点的复合节点
        private readonly Stack<CompositeFrame> _stack = new();
        // 待应用的装饰器链（LIFO 包裹紧跟的下一个节点/复合）
        private readonly Stack<DecoratorNode> _pendingDecorators = new();

        private readonly string _name;
        private BTNode _root;
        private bool _enableEvents;

        // WithUtility 定位：最近一次向某 UtilitySelector 添加子节点的引用
        private UtilitySelectorNode _lastUtilityParent;

        private BehaviorTreeBuilder(string name)
        {
            _name = name;
        }

        /// <summary>创建构建器。</summary>
        public static BehaviorTreeBuilder Create(string name = null)
            => new BehaviorTreeBuilder(name);

        // ──────────────────────────────────────────────
        //  复合节点（需 End 收尾）
        // ──────────────────────────────────────────────

        /// <summary>顺序节点（与）。</summary>
        public BehaviorTreeBuilder Sequence(string name = null)
            => PushComposite(new SequenceNode { Name = name });

        /// <summary>择一节点（或）。</summary>
        public BehaviorTreeBuilder Selector(string name = null)
            => PushComposite(new SelectorNode { Name = name });

        /// <summary>并行节点。首版默认 RequireOne 成功 / RequireAll 失败。</summary>
        public BehaviorTreeBuilder Parallel(
            ParallelSuccessPolicy successPolicy = ParallelSuccessPolicy.RequireOne,
            ParallelFailurePolicy failurePolicy = ParallelFailurePolicy.RequireAll,
            string name = null)
            => PushComposite(new ParallelNode(successPolicy, failurePolicy) { Name = name });

        /// <summary>效用选择节点（Utility AI）。子节点用 <see cref="WithUtility"/> 附打分。</summary>
        public BehaviorTreeBuilder UtilitySelector(string name = null)
            => PushComposite(new UtilitySelectorNode { Name = name });

        /// <summary>结束当前复合节点，把它（含外层装饰器）挂到父级或设为根。</summary>
        public BehaviorTreeBuilder End()
        {
            if (_stack.Count == 0)
            {
                Log.Warning("BT", "End() 调用多余：没有待结束的复合节点。");
                return this;
            }

            var frame = _stack.Pop();
            AttachToCurrent(frame.Mount);
            return this;
        }

        // ──────────────────────────────────────────────
        //  装饰器（作用于紧随其后的一个节点/子树）
        // ──────────────────────────────────────────────

        /// <summary>取反装饰器。</summary>
        public BehaviorTreeBuilder Inverter()
            => PushDecorator(new InverterNode());

        /// <summary>重复装饰器。count 小于 0 表示无限重复。</summary>
        public BehaviorTreeBuilder Repeat(int count)
            => PushDecorator(new RepeatNode(count));

        /// <summary>冷却装饰器（子节点成功后进入冷却，冷却期内返回 Failure）。</summary>
        public BehaviorTreeBuilder Cooldown(float seconds)
            => PushDecorator(new CooldownNode(seconds));

        /// <summary>条件守卫装饰器（谓词不满足直接 Failure）。</summary>
        public BehaviorTreeBuilder Condition(Func<TickContext, bool> predicate)
            => PushDecorator(new ConditionNode(predicate));

        /// <summary>
        /// 反应式中止装饰器（离散 Key 语义）：监听黑板 Key，值发生<b>任意变化</b>时中止子树重评估。
        /// <para>仅适合离散状态 Key（bool / enum / int 状态位）。连续浮点 Key（如 HpRatio）
        /// 请用带谓词的重载，否则会每帧被打断。</para>
        /// </summary>
        public BehaviorTreeBuilder Reactive<T>(string watchKey)
            => PushDecorator(new ReactiveNode<T>(watchKey));

        /// <summary>
        /// 反应式中止装饰器（谓词语义）：仅当 <paramref name="shouldAbort"/>(旧值, 新值) 返回 true 时才中止子树重评估。
        /// <para>适合连续值 Key 的「跨越阈值」判定，例如：
        /// <c>Reactive&lt;float&gt;(BBKeys.HpRatio, (o, n) =&gt; o &gt;= 0.3f &amp;&amp; n &lt; 0.3f)</c>。</para>
        /// </summary>
        public BehaviorTreeBuilder Reactive<T>(string watchKey, System.Func<T, T, bool> shouldAbort)
            => PushDecorator(new ReactiveNode<T>(watchKey, shouldAbort));

        // ──────────────────────────────────────────────
        //  叶节点
        // ──────────────────────────────────────────────

        /// <summary>同步动作叶节点。</summary>
        public BehaviorTreeBuilder Do(Func<TickContext, NodeStatus> action, string name = null)
            => AttachLeaf(new ActionNode(action) { Name = name });

        /// <summary>异步动作叶节点（UniTask，映射 Running；必须尊重 CancellationToken）。</summary>
        public BehaviorTreeBuilder DoAsync(
            Func<TickContext, CancellationToken, UniTask<NodeStatus>> action, string name = null)
            => AttachLeaf(new AsyncActionNode(action) { Name = name });

        /// <summary>条件判断叶节点。</summary>
        public BehaviorTreeBuilder Check(Func<TickContext, bool> condition, string name = null)
            => AttachLeaf(new ConditionLeafNode(condition) { Name = name });

        // ──────────────────────────────────────────────
        //  UtilitySelector 专用
        // ──────────────────────────────────────────────

        /// <summary>
        /// 给"当前 UtilitySelector 最近添加的子节点"附加打分。
        /// <para>weight：静态权重系数；curve：可选权重曲线，对归一化后的分数重映射。</para>
        /// </summary>
        public BehaviorTreeBuilder WithUtility(
            Func<TickContext, float> scorer, float weight = 1f, AnimationCurve curve = null)
        {
            if (_lastUtilityParent != null)
            {
                _lastUtilityParent.SetScorer(scorer, weight, curve);
            }
            else
            {
                Log.Warning("BT", "WithUtility() 必须紧跟 UtilitySelector 内的子节点之后调用。");
            }
            return this;
        }

        // ──────────────────────────────────────────────
        //  开关
        // ──────────────────────────────────────────────

        /// <summary>开启行为节点事件（进入/结束发 <see cref="BehaviorNodeEvent"/>）。默认关闭。</summary>
        public BehaviorTreeBuilder EnableEvents()
        {
            _enableEvents = true;
            return this;
        }

        // ──────────────────────────────────────────────
        //  构建
        // ──────────────────────────────────────────────

        /// <summary>构建行为树。</summary>
        public BehaviorTree Build(Blackboard blackboard = null, object owner = null)
        {
            if (_stack.Count > 0)
            {
                Log.Warning("BT", $"Build() 时仍有 {_stack.Count} 个复合节点未 End()，请检查 Builder 配对。");
                while (_stack.Count > 0)
                {
                    var frame = _stack.Pop();
                    AttachToCurrent(frame.Mount);
                }
            }

            if (_pendingDecorators.Count > 0)
                Log.Warning("BT", "Build() 时存在未附着到节点的装饰器（装饰器后必须紧跟一个节点）。");

            if (_root == null)
                Log.Warning("BT", "Build() 时根节点为空，返回空树（Tick 恒 Failure）。");

            // 事件开关：标记位预留，节点级事件发布由后续版本在 Tick 中判定。
            _ = _enableEvents;

            return new BehaviorTree(_name, _root, blackboard, owner);
        }

        // ──────────────────────────────────────────────
        //  内部：构建栈逻辑
        // ──────────────────────────────────────────────

        private BehaviorTreeBuilder PushComposite(CompositeNode composite)
        {
            // 复合节点入栈：composite 接收子节点，mount 是其对外挂载对象（外层可能有待应用的装饰器）
            var mount = WrapWithPendingDecorators(composite);
            _stack.Push(new CompositeFrame { Composite = composite, Mount = mount });
            return this;
        }

        private BehaviorTreeBuilder PushDecorator(DecoratorNode decorator)
        {
            _pendingDecorators.Push(decorator);
            return this;
        }

        private BehaviorTreeBuilder AttachLeaf(BTNode leaf)
        {
            var mount = WrapWithPendingDecorators(leaf);
            AttachToCurrent(mount);
            return this;
        }

        // 用待应用的装饰器链（LIFO）包裹 inner，返回最外层节点；无装饰器则返回 inner 本身
        private BTNode WrapWithPendingDecorators(BTNode inner)
        {
            BTNode current = inner;
            while (_pendingDecorators.Count > 0)
            {
                var deco = _pendingDecorators.Pop();
                deco.SetChild(current);
                current = deco;
            }
            return current;
        }

        // 把 node 挂到当前栈顶复合节点；栈空则设为根
        private void AttachToCurrent(BTNode node)
        {
            if (_stack.Count > 0)
            {
                var parent = _stack.Peek().Composite;
                parent.AddChild(node);
                // 若父复合是 UtilitySelector，则记录它供 WithUtility 定位
                _lastUtilityParent = parent as UtilitySelectorNode;
            }
            else
            {
                if (_root != null)
                    Log.Warning("BT", "行为树已有根节点，重复设置根将覆盖。请确保只有一个顶层节点。");
                _root = node;
                _lastUtilityParent = null;
            }
        }
    }
}
