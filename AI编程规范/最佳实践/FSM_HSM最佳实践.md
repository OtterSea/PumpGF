# FSM / HSM 最佳实践

> **文档定位**：给 Coding AI 提供 FSM/HSM 在**真实业务场景下的标准正例**及高频误用反例。与《FSM_HSM模块功能设计指南》(契约)配合使用：契约回答"接口是什么"，本文回答"业务该怎么用"。
> **写作依据**：2026-08 项目实战反哺，融合《踩坑记录》《代码质量审查报告》真实误用。
> **优先读**：AI 接状态机相关需求时先读本文再写代码。

---

## 0. 一句话原则

**状态逻辑用 State 类 + `.TransitionTo(...).When(...)` 集中定义，禁止在 MonoBehaviour.Update 里写 if-else 状态分支、禁止手动散落 `ChangeState`、禁止忘 `BindToLifecycle`。**

---

## 1. 标准正例集

### 1.1 动作游戏玩家 HSM → Builder 集中定义

**场景**：玩家 3C（Grounded/Airborne 层级）+ 攻击。

```csharp
// 子状态机：Grounded
var groundedSM = StateMachineBuilder.Create("Grounded")
    .State<IdleState>("Idle", () => new IdleState(player, input))
        .TransitionTo("Running").When(() => input.Move != Vector2.zero)
        .TransitionTo("Sprinting").When(() => input.Sprint && input.Move != Vector2.zero)
    .State<RunningState>("Running", () => new RunningState(player, input))
        .TransitionTo("Idle").When(() => input.Move == Vector2.zero)
    .InitialState("Idle")
    .Build();

// 子状态机：Airborne
var airborneSM = StateMachineBuilder.Create("Airborne")
    .State<JumpingState>("Jumping", () => new JumpingState(player))
        .TransitionTo("Falling").When(() => player.Velocity.y < 0)
    .State<FallingState>("Falling", () => new FallingState(player))
        .TransitionTo("Grounded/Idle").When(() => player.IsGrounded)  // 跨层级
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

// ⚠️ 关键：必须绑定生命周期，否则永不 Tick
playerSM.BindToLifecycle(UpdateChannel.Logic).AddTo(this);
```

```csharp
// ❌ 反例 1：MonoBehaviour.Update 里写 if-else 状态分支
void Update()
{
    if (_state == "Idle") { ... }
    else if (_state == "Run") { ... }
    // 每加一个状态改一次，状态一多就爆炸、且无暂停感知
}

// ❌ 反例 2：漏掉 BindToLifecycle —— 状态机"建了但不跑"
var sm = StateMachineBuilder.Create("Player")....Build();
// 忘写 sm.BindToLifecycle(...) → 永远停在初始状态，逻辑死寂
```

> **为什么禁止 if-else 分支**：Update 里手写状态枚举分支，会随着状态增多变成不可维护的"状态泥潭"，且不暂停感知、无法复用。FSM 把状态逻辑封装在 State 类，转换集中声明，天然可扩展。

### 1.2 转换条件用 When，不手动 ChangeState

**场景**：条件满足自动转换。

```csharp
// ✅ 正确：转换在 Builder 里集中声明
.State<IdleState>("Idle")
    .TransitionTo("Running").When(() => input.Move != Vector2.zero)
```

```csharp
// ❌ 反例：在 State.OnUpdate 里手动 ChangeState，转换逻辑散落
// IdleState.OnUpdate:
public override void OnUpdate(float dt)
{
    if (_input.Move != Vector2.zero)
        _stateMachine.ChangeState("Running");   // 散落，难以审查全部转换
}
```

> **为什么**：`When` 把转换关系集中在 Builder 一处，审查状态图一目了然。手动 `ChangeState` 分散在各 State 里，状态迁移关系需要到处翻。**例外**：极特殊场景（如跨状态机强制跳转、一次性事件驱动）可手动 `ChangeState`，但应少用并注释原因。

### 1.3 状态逻辑放 State 类，不放 MonoBehaviour

**场景**：某个状态的行为。

```csharp
// ✅ 正确：State 类封装状态行为
public class RunningState : State
{
    private readonly PlayerController _player;
    private readonly InputProvider _input;
    public RunningState(PlayerController player, InputProvider input) { ... }
    public override void OnEnter() => _player.Animator.Play("Run");
    public override void OnUpdate(float dt) => _player.Move(_input.Move);
}
```

```csharp
// ❌ 反例：MonoBehaviour 里塞状态逻辑
public class Player : MonoBehaviour
{
    // 一大坨状态逻辑写在 Update 里，无法复用、无法暂停感知
}
```

### 1.4 层级状态用子状态机，不平铺

**场景**："地面"下含 Idle/Run/Sprint 三态。

```csharp
// ✅ 正确：Grounded 用子 StateMachine 承载细分状态
.State("Grounded", groundedSM)   // groundedSM 内含 Idle/Run/Sprint
```

```csharp
// ❌ 反例：所有状态平铺在一个状态机里，状态互相转换的边爆炸
// Idle、Run、Sprint、Jump、Fall、Attack... 全平铺，转换关系成为网状
```

> **为什么**：层级状态机把"大状态"(Grounded)抽象为子状态机，内部再细分。平铺会让"地面状态机的转换"和"攻击状态机的转换"互相纠缠，状态量大时关系成网状，不可维护。

### 1.5 多状态机用 EventBus 联动，不硬耦合

**场景**：移动状态机与战斗状态机独立，需联动（如攻击时暂停移动）。

```csharp
// ✅ 正确：通过 EventBus 通信，状态机间解耦
// 战斗状态机发布事件
EventBus.Publish(new CombatStartedEvent());
// 移动状态机订阅，暂停移动
EventBus.OnEvent<CombatStartedEvent>()
    .Subscribe(_ => movementSM.ChangeState("Stunned"))
    .AddTo(this);
```

```csharp
// ❌ 反例：一个 MonoBehaviour 同时持有并直接互相调用两个状态机
public void OnAttackStart() => _movementSM.ChangeState("Stunned");  // 硬耦合
```

### 1.6 状态机暂停感知 → 绑定 Logic 通道

**场景**：打开菜单时，玩家状态机要冻结。

```csharp
// ✅ 正确：绑 Logic 通道，PushPause 时自动冻结
playerSM.BindToLifecycle(UpdateChannel.Logic).AddTo(this);
// 菜单 PushPause("Menu") → Logic 通道冻结 → 状态机不 Tick
```

```csharp
// ❌ 反例：绑定到 Default 通道，暂停感知失效，菜单打开角色还在跑
playerSM.BindToLifecycle(UpdateChannel.Default).AddTo(this);  // 暂停不冻结
```

---

## 2. 高频误用反例汇总（AI 思维惯性）

| # | 误用模式 | 正确做法 | 踩坑来源 |
|---|---------|---------|---------|
| 1 | Update 里写 `if (state == X)` 分支 | State 类 + Builder 声明转换 | 铁律-禁手写分支 |
| 2 | 忘 `BindToLifecycle`，状态机永不 Tick | `BindToLifecycle(channel)` | 铁律-必绑生命周期 |
| 3 | 状态逻辑写 MonoBehaviour | 放 State 类 | 契约 §17.2 |
| 4 | 在 `OnUpdate` 手动 `ChangeState` 散落 | `.TransitionTo().When()` 集中声明 | 契约 §17.4 |
| 5 | 所有状态平铺不嵌套 | 复合状态用子 StateMachine | 契约 §17.3 |
| 6 | 两状态机直接互相引用 | EventBus 通信解耦 | 契约 §17.6 |
| 7 | 绑 `Default` 通道致暂停失效 | 绑 `Logic` 通道 | 契约 §9.2 |
| 8 | 状态类裸 `new()` 无依赖注入 | 构造注入 + Builder 工厂 `() => new X(...)` | 契约 §8 |
| 9 | 高频状态切换开着 `PublishStateChanged` | 性能敏感时 `sm.PublishStateChanged = false` | 契约 §10.2 |
| 10 | 跨层级转换路径写错（`"Airborne/Jumping"` 写成 `"Jumping"`） | 用 `/` 分隔的完整路径 | 契约 §5 |

---

## 3. 边界与不做什么

- FSM/HSM **不做**：DOTS ECS 集成、SO 状态图完整实现、状态机可视化编辑器、状态历史/回滚、状态持久化/存档。
- 状态机是**纯 C#**、不依赖 MonoBehaviour、不依赖 GameGlobal（无模块初始化）。
- **不实现具体业务状态**（Idle/Attack 等逻辑是业务层写的 State 类）。
- 状态机是**多实例**的（一个实体可有多个独立状态机），无框架级全局状态。

---

## 4. 与设计契约的关系

- 接口/语义以《FSM/HSM模块功能设计指南》为准；本文只加"用法正例"，不改变契约。
- 契约 §17"关键使用规范"是**强制**的，本文与之对齐。
- 若冲突，以设计契约为准。

---

*本文档随 PumpGF 框架分发。由 2026-08 项目实战反哺。*
