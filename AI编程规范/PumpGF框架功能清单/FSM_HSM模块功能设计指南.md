# FSM / HSM 模块功能设计指南

> **文档定位**：本文件是 FSM/HSM（层级状态机）模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、API 契约与数据结构，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的公开 API 语义**。
>
> **模块编号**：3-1。
> **前置依赖**：Lifecycle（Update 驱动）、EventBus（状态切换事件）、R3。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 |
|--------|------|
| 1. 层级深度 | 无限嵌套（StateMachine 继承 State，递归实现） |
| 2. Update 驱动 | 自动绑定 Lifecycle 通道（推荐）+ 手动 Tick（特殊场景）两者都支持 |
| 3. 条件转换 | Predicate（`Func<bool>`）+ SO Condition（`IStateCondition`）两者，SO 本阶段预留 |
| 4. SO 状态图 | 本阶段预留接口，用代码 Builder 为主 |
| 5. Builder API | 流畅 API（`StateMachineBuilder`） |
| 6. 数据注入 | 构造注入（状态类构造函数接收依赖） |
| 7. EventBus 集成 | 状态切换发 `StateChangedEvent`，可开关 |
| 8. 并行状态机 | 支持多实例（一个实体可有多个独立状态机） |
| 风格 | 纯 C#，不依赖 MonoBehaviour |
| 底层 | R3（可选，事件流）+ Lifecycle（Update 驱动） |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**FSM/HSM 是纯 C# 的层级状态机框架**——
通过"StateMachine 继承 State"实现无限层级嵌套，
流畅 Builder API 构建状态图，与 Lifecycle/EventBus 集成。
但**不实现具体业务状态逻辑**（那是业务层的事）。

### 1.2 职责清单

| 职责 | 说明 |
|------|------|
| 层级状态机 | StateMachine 继承 State，支持无限嵌套 |
| 状态生命周期 | Enter / Update / Exit，自动传播 |
| 条件转换 | Predicate + IStateCondition（SO 预留） |
| 跨层级转换 | 转换可跨子状态机 |
| 流畅 Builder | StateMachineBuilder 链式 API |
| 数据注入 | 构造注入，状态类接收依赖 |
| Lifecycle 集成 | 可选绑定 Update 通道自动 Tick |
| EventBus 集成 | 状态切换发事件（可开关） |
| 多实例并行 | 一个实体可有多个独立状态机 |

### 1.3 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ DOTS ECS 集成 | 非 DOTS，纯 C# |
| ❌ SO 状态图完整实现 | 本阶段预留接口 |
| ❌ 状态机可视化编辑器 | Phase 4 Editor Tools |
| ❌ 状态历史/回滚 | 过重，未来需求 |
| ❌ 状态持久化/存档 | 业务自行处理 |

---

## 2. 整体架构

```
┌─────────────────────────────────────────┐
│            业务逻辑层                    │
│  (玩家/敌人/AI 用 StateMachine 管理状态) │
└──────────────────┬──────────────────────┘
                   │ Builder / Tick
                   ▼
┌─────────────────────────────────────────┐
│              StateMachine                │
│  ┌──────────────────────────────────┐   │
│  │ State 表 + Transition 表         │   │
│  │  states: Dictionary<string,State>│   │
│  │  transitions: List<Transition>   │   │
│  │  currentState: State             │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ 生命周期传播（Enter/Update/Exit） │   │
│  │  父→子→叶                        │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ 转换评估                          │   │
│  │  每帧检查当前状态的转换条件       │   │
│  └──────────────────────────────────┘   │
└──────┬──────────────────┬───────────────┘
       │ BindToLifecycle   │ Publish
       ▼                    ▼
┌──────────────┐    ┌──────────────┐
│  Lifecycle   │    │   EventBus   │
│ (Update通道) │    │ (StateChanged)│
└──────────────┘    └──────────────┘
```

---

## 3. 核心：StateMachine 继承 State（HSM 递归）

### 3.1 类继承关系

```
State（抽象基类）
  ├─ 普通状态（叶状态）：IdleState, AttackState 等
  └─ StateMachine：本身也是一个 State，可嵌套
```

**关键设计**：`StateMachine : State`，意味着状态机本身可以作为一个状态嵌入父状态机。

### 3.2 层级示例

```
PlayerStateMachine（根状态机）
  ├─ "Grounded" → StateMachine（子状态机）
  │   ├─ "Idle" → IdleState（叶状态）
  │   ├─ "Running" → RunningState
  │   └─ "Sprinting" → SprintingState
  ├─ "Airborne" → StateMachine
  │   ├─ "Jumping" → JumpingState
  │   └─ "Falling" → FallingState
  └─ "Attacking" → StateMachine
      ├─ "LightAttack" → LightAttackState
      └─ "HeavyAttack" → HeavyAttackState
```

### 3.3 为什么这样设计

- `Grounded` 是一个 `StateMachine`，本身是 `PlayerStateMachine` 的一个"状态"。
- 进入 `Grounded` → 子状态机启动 → 进入初始子状态 `Idle`。
- `Grounded.OnUpdate` → 子状态机 `Tick` → 当前子状态 `OnUpdate`。
- 无限嵌套：子状态机的子状态也可以是状态机。

---

## 4. 状态生命周期与传播

### 4.1 传播规则

| 事件 | 传播顺序 |
|------|----------|
| Enter | 父状态 OnEnter → 子状态机 OnEnter → 子状态 OnEnter |
| Update | 父状态 OnUpdate → 子状态机 OnUpdate → 子状态 OnUpdate |
| Exit | 子状态 OnExit → 子状态机 OnExit → 父状态 OnExit |

### 4.2 StateMachine 作为 State 的生命周期

当 StateMachine 被作为子状态嵌入父状态机时：

```
// 父状态机调用子状态机（StateMachine）的 OnEnter
override void OnEnter()
{
    // 进入初始子状态
    ChangeState(_initialStateName);
}

// 父状态机调用子状态机的 OnUpdate
override void OnUpdate(float dt)
{
    // 1. 评估当前状态的转换
    EvaluateTransitions();
    // 2. 更新当前子状态
    _currentState?.OnUpdate(dt);
}

// 父状态机调用子状态机的 OnExit
override void OnExit()
{
    _currentState?.OnExit();
    _currentState = null;
}
```

---

## 5. 跨层级转换

### 5.1 转换定义

转换可跨子状态机：

```
// Running（Grounded 子状态）→ Jumping（Airborne 子状态）
// 这是从 Grounded.Running 跳到 Airborne.Jumping
builder.State("Running")
    .TransitionTo("Airborne/Jumping")  // 跨子状态机
    .When(() => input.Jump);
```

### 5.2 跨层级转换处理

- 转换目标用路径表示（如 `"Airborne/Jumping"`，`/` 分隔层级）。
- ChangeState 时：
  1. 退出当前状态链（从叶到根）。
  2. 进入目标状态链（从根到叶）。
  3. 中间状态的 OnEnter/OnExit 正确触发。

### 5.3 同层级转换

同子状态机内的转换不需路径：
```
builder.State("Idle")
    .TransitionTo("Running")
    .When(() => input.Move != Vector2.zero);
```

---

## 6. 条件转换

### 6.1 IStateCondition 接口

```
public interface IStateCondition
{
    bool Evaluate();
}
```

- Predicate 是默认实现（代码条件）。
- SO Condition 是未来实现（可配置条件），本阶段预留。

### 6.2 PredicateCondition（默认）

```
public class PredicateCondition : IStateCondition
{
    private readonly Func<bool> _predicate;
    public PredicateCondition(Func<bool> predicate) { _predicate = predicate; }
    public bool Evaluate() => _predicate();
}
```

### 6.3 SO Condition（预留）

未来可实现：
```
// 未来：SO 配置条件
// public class HPBelowCondition : ScriptableObject, IStateCondition
// {
//     public float Threshold;
//     public bool Evaluate() => GameGlobal.GameData.Player.Hp.Value < Threshold;
// }
```

### 6.4 Transition 结构

```
public class Transition
{
    public string From;
    public string To;
    public IStateCondition Condition;
    public Action OnTransition;  // 转换时回调（可选）
}
```

---

## 7. StateMachineBuilder 流畅 API

### 7.1 Builder 结构

```
public class StateMachineBuilder
{
    public static StateMachineBuilder Create(string name);
    public StateBuilder State<TState>(string name) where TState : State, new();
    public StateMachineBuilder InitialState(string name);
    public StateMachine Build();
}

public class StateBuilder
{
    public StateBuilder OnEnter(Action onEnter);
    public StateBuilder OnEnter(Action<State> onEnter);
    public StateBuilder OnUpdate(Action<State, float> onUpdate);
    public StateBuilder OnExit(Action<State> onExit);
    public TransitionBuilder TransitionTo(string to);
    // 返回 StateBuilder 继续
}

public class TransitionBuilder
{
    public StateBuilder When(Func<bool> condition);
    public StateBuilder When(Func<bool> condition, Action onTransition);
}
```

### 7.2 使用示例

```
var sm = StateMachineBuilder.Create("Player")
    .State<IdleState>("Idle")
        .OnEnter(s => s.PlayAnim("Idle"))
        .OnUpdate((s, dt) => s.CheckInput())
        .TransitionTo("Running").When(() => input.Move != Vector2.zero)
        .TransitionTo("Jumping").When(() => input.Jump)
        .TransitionTo("Attacking/LightAttack").When(() => input.Attack)
    .State<RunningState>("Running")
        .OnEnter(s => s.PlayAnim("Run"))
        .OnUpdate((s, dt) => s.Move(input.Move))
        .TransitionTo("Idle").When(() => input.Move == Vector2.zero)
    .InitialState("Idle")
    .Build();
```

### 7.3 嵌套状态机构建

```
var groundedSM = StateMachineBuilder.Create("Grounded")
    .State<IdleState>("Idle")...
    .State<RunningState>("Running")...
    .InitialState("Idle")
    .Build();

var playerSM = StateMachineBuilder.Create("Player")
    .State("Grounded", groundedSM)  // 嵌入子状态机
        .TransitionTo("Airborne").When(() => !isGrounded)
    .State("Airborne", airborneSM)
        .TransitionTo("Grounded").When(() => isGrounded)
    .InitialState("Grounded")
    .Build();
```

---

## 8. 状态数据注入（构造注入）

### 8.1 设计

状态类通过构造函数接收依赖：

```
public class IdleState : State
{
    private readonly PlayerController _player;
    private readonly InputProvider _input;

    public IdleState(PlayerController player, InputProvider input)
    {
        _player = player;
        _input = input;
    }

    public override void OnEnter()
    {
        _player.Animator.Play("Idle");
    }

    public override void OnUpdate(float dt)
    {
        if (_input.Move != Vector2.zero)
            _player.StateMachine.ChangeState("Running");
    }
}
```

### 8.2 Builder 中注入

```
var sm = StateMachineBuilder.Create("Player")
    .State("Idle", () => new IdleState(player, input))  // 工厂函数注入
        .OnEnter(s => ((IdleState)s).PlayAnim("Idle"))
    ...
```

或泛型版本：
```
.State<IdleState>("Idle", () => new IdleState(player, input))
```

---

## 9. 与 Lifecycle 集成

### 9.1 自动绑定

```
public static class StateMachineExtensions
{
    public static IDisposable BindToLifecycle(this StateMachine sm, UpdateChannel channel);
    // 内部订阅 Lifecycle.OnUpdate(channel)，调用 sm.Tick(dt)
}
```

### 9.2 使用

```
var sm = StateMachineBuilder.Create("Player")....Build();
sm.BindToLifecycle(UpdateChannel.Logic).AddTo(this);  // 自动 Tick，暂停感知
```

- 绑定 Logic 通道 → 暂停时不 Tick → 状态机冻结。
- 绑定 Default 通道 → 每帧 Tick。

### 9.3 手动 Tick

```
sm.Tick(dt);  // 特殊场景手动驱动（如测试、自定义循环）
```

---

## 10. 与 EventBus 集成

### 10.1 StateChangedEvent

```
public readonly struct StateChangedEvent
{
    public readonly string StateMachineName;
    public readonly string From;
    public readonly string To;
    public StateChangedEvent(string sm, string from, string to) { ... }
}
```

### 10.2 发布开关

```
sm.PublishStateChanged = true;  // 默认 true，可关闭
```

- `ChangeState` 时自动发布 `StateChangedEvent`。
- 性能敏感时关闭（如高频状态切换）。

### 10.3 订阅

```
EventBus.OnEvent<StateChangedEvent>()
    .Where(e => e.StateMachineName == "Player")
    .Subscribe(e => Debug.Log($"Player: {e.From} → {e.To}"))
    .AddTo(this);
```

---

## 11. 并行状态机

### 11.1 多实例

一个实体可有多个独立状态机：

```
// 玩家有移动状态机 + 攻击状态机
var movementSM = StateMachineBuilder.Create("PlayerMovement")....Build();
movementSM.BindToLifecycle(UpdateChannel.Logic).AddTo(this);

var combatSM = StateMachineBuilder.Create("PlayerCombat")....Build();
combatSM.BindToLifecycle(UpdateChannel.Logic).AddTo(this);
```

- 两个状态机独立运行，各自有当前状态。
- 互不干扰（移动状态不影响攻击状态）。
- 如需联动，通过 EventBus 通信（如攻击状态发布事件，移动状态订阅暂停移动）。

### 11.2 实体持有多个状态机

```
public class PlayerEntity
{
    public StateMachine Movement { get; }
    public StateMachine Combat { get; }
}
```

---

## 12. API 契约（公开接口）

### 12.1 State（抽象基类）

```
public abstract class State
{
    public string Name { get; }
    public StateMachine Parent { get; }

    public virtual void OnEnter() { }
    public virtual void OnUpdate(float dt) { }
    public virtual void OnExit() { }
}
```

### 12.2 StateMachine

```
public class StateMachine : State
{
    // 状态管理
    void AddState(string name, State state);
    void AddState(string name, Func<State> factory);
    void SetInitialState(string name);

    // 转换管理
    void AddTransition(string from, string to, Func<bool> condition);
    void AddTransition(string from, string to, IStateCondition condition);
    void AddTransition(string from, string to, Func<bool> condition, Action onTransition);

    // 运行时
    State CurrentState { get; }
    void ChangeState(string name);
    void Tick(float dt);

    // EventBus
    bool PublishStateChanged { get; set; }
    string Name { get; }
}
```

### 12.3 StateMachineBuilder

```
public class StateMachineBuilder
{
    static StateMachineBuilder Create(string name);
    StateBuilder State<TState>(string name) where TState : State, new();
    StateBuilder State<TState>(string name, Func<TState> factory) where TState : State;
    StateBuilder State(string name, StateMachine subMachine);  // 嵌入子状态机
    StateMachineBuilder InitialState(string name);
    StateMachine Build();
}

public class StateBuilder
{
    StateBuilder OnEnter(Action onEnter);
    StateBuilder OnEnter(Action<State> onEnter);
    StateBuilder OnUpdate(Action<State, float> onUpdate);
    StateBuilder OnExit(Action<State> onExit);
    TransitionBuilder TransitionTo(string to);
}

public class TransitionBuilder
{
    StateBuilder When(Func<bool> condition);
    StateBuilder When(Func<bool> condition, Action onTransition);
    StateBuilder When(IStateCondition condition);
}
```

### 12.4 IStateCondition

```
public interface IStateCondition
{
    bool Evaluate();
}
```

### 12.5 StateChangedEvent

```
public readonly struct StateChangedEvent
{
    public readonly string StateMachineName;
    public readonly string From;
    public readonly string To;
}
```

### 12.6 扩展方法

```
public static class StateMachineExtensions
{
    static IDisposable BindToLifecycle(this StateMachine sm, UpdateChannel channel);
}
```

---

## 13. 使用示例（伪代码）

### 13.1 简单状态机

```
var sm = StateMachineBuilder.Create("Door")
    .State("Closed")
        .OnEnter(s => door.Close())
        .TransitionTo("Open").When(() => input.Interact)
    .State("Open")
        .OnEnter(s => door.Open())
        .TransitionTo("Closed").When(() => input.Interact)
    .InitialState("Closed")
    .Build();

sm.BindToLifecycle(UpdateChannel.Logic).AddTo(this);
```

### 13.2 层级状态机（动作游戏）

```
// 构建子状态机
var groundedSM = StateMachineBuilder.Create("Grounded")
    .State<IdleState>("Idle", () => new IdleState(player, input))
        .OnEnter(s => player.PlayAnim("Idle"))
        .TransitionTo("Running").When(() => input.Move != Vector2.zero)
    .State<RunningState>("Running", () => new RunningState(player, input))
        .OnEnter(s => player.PlayAnim("Run"))
        .TransitionTo("Idle").When(() => input.Move == Vector2.zero)
        .TransitionTo("Sprinting").When(() => input.Sprint && input.Move != Vector2.zero)
    .InitialState("Idle")
    .Build();

var airborneSM = StateMachineBuilder.Create("Airborne")
    .State<JumpingState>("Jumping", () => new JumpingState(player))
        .TransitionTo("Falling").When(() => player.Velocity.y < 0)
    .State<FallingState>("Falling", () => new FallingState(player))
        .TransitionTo("Grounded/Idle").When(() => player.IsGrounded)
    .InitialState("Jumping")
    .Build();

// 根状态机
var playerSM = StateMachineBuilder.Create("Player")
    .State("Grounded", groundedSM)
        .TransitionTo("Airborne").When(() => !player.IsGrounded)
    .State("Airborne", airborneSM)
        .TransitionTo("Grounded").When(() => player.IsGrounded)
    .InitialState("Grounded")
    .Build();

playerSM.BindToLifecycle(UpdateChannel.Logic).AddTo(this);
```

### 13.3 监听状态切换

```
EventBus.OnEvent<StateChangedEvent>()
    .Where(e => e.StateMachineName == "Player")
    .Subscribe(e => Debug.Log($"Player: {e.From} → {e.To}"))
    .AddTo(this);
```

### 13.4 并行状态机

```
public class PlayerEntity
{
    public StateMachine Movement { get; }
    public StateMachine Combat { get; }

    public PlayerEntity()
    {
        Movement = StateMachineBuilder.Create("PlayerMovement")....Build();
        Movement.BindToLifecycle(UpdateChannel.Logic);

        Combat = StateMachineBuilder.Create("PlayerCombat")....Build();
        Combat.BindToLifecycle(UpdateChannel.Logic);
    }
}
```

---

## 14. 实现检查清单

- [ ] `State` 抽象基类（Name/Parent/OnEnter/OnUpdate/OnExit）
- [ ] `StateMachine : State`（继承，递归嵌套）
- [ ] 状态注册（AddState，支持工厂函数注入）
- [ ] 转换注册（AddTransition，支持 Predicate + IStateCondition）
- [ ] `ChangeState` 正确触发 OnExit → OnEnter 链
- [ ] 层级传播：父→子→叶 Enter/Update/Exit
- [ ] 跨层级转换（路径解析 `"A/B/C"`）
- [ ] `Tick(dt)` 评估转换 + 更新当前状态
- [ ] `StateMachineBuilder` 流畅 API（State/Transition/InitialState/Build）
- [ ] `StateBuilder`（OnEnter/OnUpdate/OnExit/TransitionTo）
- [ ] `TransitionBuilder`（When）
- [ ] 嵌入子状态机（`State(name, subMachine)`）
- [ ] `BindToLifecycle(channel)` 扩展方法
- [ ] `PublishStateChanged` 开关 + `StateChangedEvent` 发布
- [ ] `IStateCondition` 接口 + `PredicateCondition` 默认实现
- [ ] 多实例并行（无全局状态）
- [ ] `Dispose` 清理订阅
- [ ] 所有公开 API 有中文 XML 注释
- [ ] 无硬编码

---

## 15. 依赖关系

| 依赖项 | 方向 | 说明 |
|--------|------|------|
| Lifecycle | 可选引用 | `BindToLifecycle` 自动 Tick |
| EventBus | 可选引用 | `StateChangedEvent` 发布（可开关） |
| R3 | 可选引用 | IDisposable/AddTo（绑定生命周期） |
| GameGlobal | 被引用 | 无直接依赖（状态机不依赖 GameGlobal） |

> **初始化**：FSM/HSM 是纯 C# 库，不需要 IModule 初始化。
> 业务层直接用 `StateMachineBuilder` 创建状态机，按需 `BindToLifecycle`。

---

## 16. 后续模块依赖本模块的接口

| 后续模块 | 使用的 FSM 接口 |
|----------|----------------|
| Entity Component (3-2) | 实体的状态机组件 |
| Level/Scene Manager (3-3) | 场景状态机 |
| 业务层（敌人 AI） | AI 状态机 |
| 业务层（玩家） | 玩家状态机 |

---

## 17. 关键使用规范（业务层 Coding AI 必读）

### 17.1 禁止 Update 里写 if-else 状态分支

- ❌ 禁止：`if (state == Idle) { ... } else if (state == Attack) { ... }`
- ✅ 正确：用 StateMachine + State 类

### 17.2 状态逻辑放 State 类，不放 MonoBehaviour

- ❌ 禁止：MonoBehaviour 的 Update 里写状态逻辑
- ✅ 正确：State.OnUpdate 里写，StateMachine 驱动

### 17.3 层级状态用子状态机

- 复杂状态（如"地面"含 Idle/Run/Sprint）用子 StateMachine，不用平铺。

### 17.4 转换条件用 When，不手动 ChangeState

- ❌ 禁止：在 State.OnUpdate 里手动 `sm.ChangeState("X")`（散落）
- ✅ 正确：Builder 里 `.TransitionTo("X").When(() => condition)`（集中定义）
- 例外：特殊场景可在代码内手动 ChangeState，但应少用。

### 17.5 状态机必须绑定生命周期

- ❌ 禁止：StateMachine 不 BindToLifecycle（永不 Tick）
- ✅ 正确：`sm.BindToLifecycle(UpdateChannel.Logic).AddTo(this)`

### 17.6 多状态机用 EventBus 联动

- 移动状态机与攻击状态机独立运行，通过 EventBus 通信联动。

---

**文档结束。实现阶段请严格遵循本契约。**
