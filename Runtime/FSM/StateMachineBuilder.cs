using System;
using System.Collections.Generic;

namespace PumpGF
{
    /// <summary>
    /// 状态机构建器。流畅链式 API 构建 StateMachine。
    /// </summary>
    public class StateMachineBuilder
    {
        private readonly string _name;
        private readonly List<StateDef> _stateDefs = new(8);
        private string _initialState;

        internal sealed class StateDef
        {
            public string Name;
            public BuilderState State;
            public List<Transition> Transitions;
        }

        private StateMachineBuilder(string name) { _name = name; }

        /// <summary>创建状态机构建器</summary>
        public static StateMachineBuilder Create(string name) => new(name);

        /// <summary>注册状态（业务 State 子类，无参构造）</summary>
        public StateBuilder State<TState>(string name) where TState : State, new()
            => AddStateDef(name, new BuilderState { Inner = new TState() });

        /// <summary>注册状态（工厂注入依赖）</summary>
        public StateBuilder State<TState>(string name, Func<TState> factory) where TState : State
            => AddStateDef(name, new BuilderState { Inner = factory() });

        /// <summary>注册纯 Action 状态（无业务类，用 Builder 回调）</summary>
        public StateBuilder State(string name)
            => AddStateDef(name, new BuilderState { Inner = null });

        /// <summary>嵌入子状态机</summary>
        public StateBuilder State(string name, StateMachine subMachine)
            => AddStateDef(name, new BuilderState { Inner = subMachine });

        /// <summary>设置初始状态</summary>
        public StateMachineBuilder InitialState(string name)
        {
            _initialState = name;
            return this;
        }

        /// <summary>构建 StateMachine</summary>
        public StateMachine Build()
        {
            var sm = new StateMachine { Name = _name };
            for (int i = 0; i < _stateDefs.Count; i++)
            {
                var def = _stateDefs[i];
                sm.AddState(def.Name, def.State);
                var transitions = def.Transitions;
                for (int j = 0; j < transitions.Count; j++)
                    sm.AddTransition(transitions[j]);
            }
            if (!string.IsNullOrEmpty(_initialState))
                sm.SetInitialState(_initialState);
            return sm;
        }

        private StateBuilder AddStateDef(string name, BuilderState state)
        {
            var def = new StateDef
            {
                Name = name,
                State = state,
                Transitions = new List<Transition>()
            };
            _stateDefs.Add(def);
            return new StateBuilder(this, def);
        }
    }

    /// <summary>
    /// 状态构建器。配置 OnEnter/OnUpdate/OnExit 回调与转换。
    /// </summary>
    public class StateBuilder
    {
        private readonly StateMachineBuilder _parent;
        private readonly string _name;
        private readonly BuilderState _state;
        private readonly List<Transition> _transitions;

        internal StateBuilder(StateMachineBuilder parent, StateMachineBuilder.StateDef def)
        {
            _parent = parent;
            _name = def.Name;
            _state = def.State;
            _transitions = def.Transitions;
        }

        public StateBuilder OnEnter(Action onEnter)
        {
            _state.OnEnterAction = onEnter != null ? new Action<State>(_ => onEnter()) : null;
            return this;
        }

        public StateBuilder OnEnter(Action<State> onEnter) { _state.OnEnterAction = onEnter; return this; }
        public StateBuilder OnUpdate(Action<State, float> onUpdate) { _state.OnUpdateAction = onUpdate; return this; }
        public StateBuilder OnExit(Action<State> onExit) { _state.OnExitAction = onExit; return this; }

        public TransitionBuilder TransitionTo(string to) => new(this, to);

        // ── 委托回 StateMachineBuilder（链式继续）──
        public StateBuilder State<TState>(string name) where TState : State, new() => _parent.State<TState>(name);
        public StateBuilder State<TState>(string name, Func<TState> factory) where TState : State => _parent.State<TState>(name, factory);
        public StateBuilder State(string name) => _parent.State(name);
        public StateBuilder State(string name, StateMachine subMachine) => _parent.State(name, subMachine);
        public StateMachineBuilder InitialState(string name) => _parent.InitialState(name);
        public StateMachine Build() => _parent.Build();

        internal void AddTransition(string to, IStateCondition condition, Action onTransition)
        {
            _transitions.Add(new Transition
            {
                From = _name,
                To = to,
                Condition = condition,
                OnTransition = onTransition
            });
        }
    }

    /// <summary>转换构建器。配置转换条件。</summary>
    public class TransitionBuilder
    {
        private readonly StateBuilder _parent;
        private readonly string _to;

        internal TransitionBuilder(StateBuilder parent, string to) { _parent = parent; _to = to; }

        public StateBuilder When(Func<bool> condition)
        {
            _parent.AddTransition(_to, new PredicateCondition(condition), null);
            return _parent;
        }

        public StateBuilder When(Func<bool> condition, Action onTransition)
        {
            _parent.AddTransition(_to, new PredicateCondition(condition), onTransition);
            return _parent;
        }

        public StateBuilder When(IStateCondition condition)
        {
            _parent.AddTransition(_to, condition, null);
            return _parent;
        }
    }
}
