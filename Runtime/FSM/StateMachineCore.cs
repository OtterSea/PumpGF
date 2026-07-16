using System;
using System.Collections.Generic;
using R3;

namespace PumpGF
{
    /// <summary>
    /// 状态机状态切换事件。由 <see cref="StateMachine"/> 在 ChangeState 时发布（可开关）。
    /// </summary>
    public readonly struct StateChangedEvent
    {
        /// <summary>状态机名</summary>
        public readonly string StateMachineName;
        /// <summary>原状态名（首次进入为 null）</summary>
        public readonly string From;
        /// <summary>目标状态名</summary>
        public readonly string To;

        public StateChangedEvent(string stateMachineName, string from, string to)
        {
            StateMachineName = stateMachineName;
            From = from;
            To = to;
        }
    }

    /// <summary>状态条件抽象（Predicate 默认实现，SO Condition 预留）</summary>
    public interface IStateCondition
    {
        /// <summary>评估是否满足转换条件</summary>
        bool Evaluate();
    }

    /// <summary>基于谓词（Func&lt;bool&gt;）的默认条件实现</summary>
    public sealed class PredicateCondition : IStateCondition
    {
        private readonly Func<bool> _predicate;
        public PredicateCondition(Func<bool> predicate) { _predicate = predicate; }
        public bool Evaluate() => _predicate != null && _predicate();
    }

    /// <summary>状态转换定义</summary>
    public sealed class Transition
    {
        /// <summary>源状态名（"*" 表示任意状态）</summary>
        public string From;
        /// <summary>目标状态名（可含路径如 "A/B"）</summary>
        public string To;
        /// <summary>转换条件</summary>
        public IStateCondition Condition;
        /// <summary>转换时回调（可选）</summary>
        public Action OnTransition;
    }

    /// <summary>
    /// 状态抽象基类。子类重写 OnEnter/OnUpdate/OnExit 实现状态逻辑。
    /// <para>订阅托管用 <see cref="Bag"/>（.AddTo(ref Bag)），退出时自动清理。</para>
    /// </summary>
    public abstract class State
    {
        /// <summary>状态名</summary>
        public string Name { get; internal set; }

        /// <summary>所属状态机（根状态机为 null）</summary>
        public StateMachine Parent { get; internal set; }

        /// <summary>订阅托管网（OnEnter 中订阅，退出时统一释放）</summary>
        protected DisposableBag Bag;

        /// <summary>进入状态</summary>
        public virtual void OnEnter() { }

        /// <summary>每帧更新</summary>
        public virtual void OnUpdate(float dt) { }

        /// <summary>退出状态</summary>
        public virtual void OnExit() { }

        /// <summary>清理订阅网（内部，退出时调用）</summary>
        internal void DisposeBag() => Bag.Dispose();

        /// <summary>退出并清理（OnExit + 订阅释放）</summary>
        internal virtual void ExitInternal()
        {
            OnExit();
            Bag.Dispose();
        }
    }

    /// <summary>
    /// 层级状态机。继承 <see cref="State"/>，可作为一个状态嵌入父状态机，实现无限层级嵌套。
    /// <para>生命周期传播：Enter 父→子→叶；Exit 叶→子→父；Update 父→子→叶。</para>
    /// </summary>
    public class StateMachine : State
    {
        private readonly Dictionary<string, State> _states = new(8);
        private readonly List<Transition> _transitions = new(8);
        private State _currentState;
        private string _initialStateName;

        /// <summary>当前状态（叶状态或子状态机）</summary>
        public State CurrentState => _currentState;

        /// <summary>是否发布 StateChangedEvent（默认 true）</summary>
        public bool PublishStateChanged { get; set; } = true;

        /// <summary>根状态机（向上遍历 Parent）</summary>
        public StateMachine Root
        {
            get
            {
                var p = this;
                while (p.Parent != null) p = p.Parent;
                return p;
            }
        }

        // ──────────────────────────────────────────────
        //  状态注册
        // ──────────────────────────────────────────────

        /// <summary>注册状态</summary>
        public void AddState(string name, State state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            state.Name = name;
            state.Parent = this;
            _states[name] = state;
        }

        /// <summary>注册状态（工厂注入）</summary>
        public void AddState(string name, Func<State> factory) => AddState(name, factory());

        /// <summary>设置初始状态</summary>
        public void SetInitialState(string name) => _initialStateName = name;

        // ──────────────────────────────────────────────
        //  转换注册
        // ──────────────────────────────────────────────

        public void AddTransition(string from, string to, Func<bool> condition)
            => _transitions.Add(new Transition { From = from, To = to, Condition = new PredicateCondition(condition) });

        public void AddTransition(string from, string to, IStateCondition condition)
            => _transitions.Add(new Transition { From = from, To = to, Condition = condition });

        public void AddTransition(string from, string to, Func<bool> condition, Action onTransition)
            => _transitions.Add(new Transition { From = from, To = to, Condition = new PredicateCondition(condition), OnTransition = onTransition });

        /// <summary>注册转换（完整对象，Builder 内部用）</summary>
        internal void AddTransition(Transition t) => _transitions.Add(t);

        // ──────────────────────────────────────────────
        //  运行时
        // ──────────────────────────────────────────────

        /// <summary>切换状态。支持路径 "A/B"（跨层级）。</summary>
        public void ChangeState(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            // 跨层级或目标不在本地 → 委托根状态机
            if (Parent != null && (path.Contains('/') || !_states.ContainsKey(path)))
            {
                Root.ChangeStateInternal(path);
                return;
            }
            ChangeStateInternal(path);
        }

        /// <summary>驱动状态机：评估转换 + 更新当前状态。</summary>
        public void Tick(float dt)
        {
            EvaluateTransitions();
            _currentState?.OnUpdate(dt);
        }

        // ──────────────────────────────────────────────
        //  生命周期（作为 State 嵌入父状态机时被调用）
        // ──────────────────────────────────────────────

        public override void OnEnter()
        {
            if (!string.IsNullOrEmpty(_initialStateName))
                ChangeStateInternal(_initialStateName);
        }

        public override void OnUpdate(float dt)
        {
            EvaluateTransitions();
            _currentState?.OnUpdate(dt);
        }

        public override void OnExit()
        {
            if (_currentState != null)
            {
                _currentState.ExitInternal();
                _currentState = null;
            }
        }

        internal override void ExitInternal()
        {
            OnExit();          // 退出当前子状态链
            Bag.Dispose();
        }

        // ──────────────────────────────────────────────
        //  内部
        // ──────────────────────────────────────────────

        internal void ChangeStateInternal(string path)
        {
            string fromName = _currentState?.Name;

            // 退出当前状态链
            if (_currentState != null)
            {
                _currentState.ExitInternal();
                _currentState = null;
            }

            if (path.Contains('/'))
            {
                // 跨层级：进入第一段，再递归进入剩余路径
                var parts = path.Split('/');
                if (!_states.TryGetValue(parts[0], out var first))
                {
                    Log.Warning("FSM", $"[{Name}] 未找到状态 '{parts[0]}'（路径 {path}）");
                    return;
                }
                _currentState = first;
                first.OnEnter();
                if (first is StateMachine subSm && parts.Length > 1)
                {
                    var remaining = string.Join("/", parts, 1, parts.Length - 1);
                    subSm.ChangeStateInternal(remaining);
                }
            }
            else
            {
                if (!_states.TryGetValue(path, out var target))
                {
                    Log.Warning("FSM", $"[{Name}] 未找到状态 '{path}'");
                    return;
                }
                _currentState = target;
                target.OnEnter();
            }

            PublishStateChangedEvent(fromName, path);
        }

        private void EvaluateTransitions()
        {
            if (_currentState == null) return;
            string curName = _currentState.Name;
            for (int i = 0; i < _transitions.Count; i++)
            {
                var t = _transitions[i];
                if (t.From == curName || t.From == "*")
                {
                    if (t.Condition != null && t.Condition.Evaluate())
                    {
                        t.OnTransition?.Invoke();
                        ChangeState(t.To);
                        return;
                    }
                }
            }
        }

        private void PublishStateChangedEvent(string from, string to)
        {
            if (!PublishStateChanged) return;
            GameGlobal.EventBus?.Publish(new StateChangedEvent(Name, from, to));
        }
    }

    /// <summary>
    /// Builder 使用的内部状态包装。支持业务 State 子类（Inner）+ Builder Action 回调共存。
    /// </summary>
    internal sealed class BuilderState : State
    {
        public State Inner;
        public Action<State> OnEnterAction;
        public Action<State, float> OnUpdateAction;
        public Action<State> OnExitAction;

        public override void OnEnter()
        {
            Inner?.OnEnter();
            OnEnterAction?.Invoke(this);
        }

        public override void OnUpdate(float dt)
        {
            Inner?.OnUpdate(dt);
            OnUpdateAction?.Invoke(this, dt);
        }

        public override void OnExit()
        {
            OnExitAction?.Invoke(this);
            Inner?.OnExit();
        }

        internal override void ExitInternal()
        {
            OnExit();
            Inner?.DisposeBag();
            DisposeBag();
        }
    }
}
