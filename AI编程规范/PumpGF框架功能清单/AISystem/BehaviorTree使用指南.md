# BehaviorTree（行为树）使用指南

> 本文面向**人类开发者**与 **AI 编程助手**，讲清 PumpGF 行为树模块「怎么用」。
> 设计契约（为什么这样设计、API 语义边界）见同目录 [`BehaviorTree模块功能设计指南.md`](./BehaviorTree模块功能设计指南.md)。
> 现阶段不提供可视化编辑器，行为树用**代码 Builder** 构建。

---

## 1. 这个模块是什么 / 什么时候用

**行为树（BehaviorTree）是给敌人、BOSS、NPC 编排「行为逻辑」的工具**——尤其适合动作游戏里「近战连段、翻滚、突进、阶段切换、被打断后切招」这类**可组合、可打断、反应式**的 AI。

选型速查：

| 你的需求 | 用什么 |
|----------|--------|
| 敌人在「巡逻 / 战斗 / 死亡」等少数互斥宏观状态间切换 | **FSM/HSM**（`StateMachineBuilder`） |
| 「战斗」时如何选招、如何连段、残血如何切狂暴 | **BehaviorTree**（本模块） |
| 敌人有很多招式，要按距离/血量/仇恨打分选一个 | BehaviorTree 里的 **UtilitySelector** |
| 目标规划型 AI（潜行 NPC、模拟经营、策略单位） | 本框架**不提供**（GOAP/HTN 已明确不做） |

> 推荐组合：**FSM 管宏观状态，BT 管战斗内部行为**。见 §7。

---

## 2. 三个核心概念

1. **节点三态**：每个节点每帧被 Tick，返回 `Success` / `Failure` / `Running`。
   - `Running` 表示「还没干完，下一帧继续」（如正在播放攻击动画）。
2. **黑板（Blackboard）**：节点间共享数据的地方（目标引用、血量比、距离等）。节点不直接持有彼此，全走黑板，解耦。
3. **Builder**：用链式 API 把节点拼成一棵树，`Sequence`（顺序）/`Selector`（择一）是最常用的两个复合节点。

复合节点短路规则（最重要，记住这两个）：

| 节点 | 行为 |
|------|------|
| `Sequence`（与） | 从上到下依次执行，**遇失败即整体失败**；全部成功才成功 |
| `Selector`（或） | 从上到下依次尝试，**遇成功即整体成功**；全部失败才失败 |

---

## 3. 最小示例

```csharp
using PumpGF.AI.BehaviorTree;

// 1. 定义黑板 Key（业务侧集中定义，禁止散落魔法字符串）
public static class BBKeys
{
    public const string Target   = "target";     // Transform
    public const string HpRatio  = "hpRatio";    // float 0~1
    public const string InMelee  = "inMelee";    // bool
}

// 2. 构建一棵树
var bb = new Blackboard();
bb.Set(BBKeys.HpRatio, 1f);

var tree = BehaviorTreeBuilder.Create("Enemy")
    .Selector()
        // 近战范围内 → 攻击
        .Sequence()
            .Check(c => c.Blackboard.Get<bool>(BBKeys.InMelee))
            .Do(c => Attack(c))
        .End()
        // 否则 → 追向目标
        .Do(c => MoveToTarget(c))
    .End()
    .Build(bb);

// 3. 绑定到 Lifecycle，自动每帧 Tick（暂停时自动冻结）
tree.BindToLifecycle(UpdateChannel.Logic).AddTo(this);
```

叶节点函数返回三态：

```csharp
NodeStatus Attack(in TickContext c)
{
    // 干活……
    return NodeStatus.Success;   // 或 Failure / Running
}
```

---

## 4. 常用节点速查

### 复合节点（有多个子节点，需 `.End()` 收尾）

```csharp
.Sequence()   ... .End()     // 顺序（与）
.Selector()   ... .End()     // 择一（或）
.Parallel(ParallelPolicy.Default) ... .End()  // 并行（任一成功即成功 / 全部失败才失败）
.UtilitySelector() ... .End()// 按打分选最高分子节点（见 §6）
```

### 装饰器（作用于紧随其后的一个节点/子树）

```csharp
.Inverter()                  // 成功↔失败取反
.Repeat(3)                   // 重复 3 次；传 -1 表示无限
.Cooldown(2f)                // 冷却期内直接返回 Failure（技能 CD）
.Condition(c => 条件)         // 守卫：条件不满足直接 Failure
.Reactive(BBKeys.HpRatio)    // 监听黑板 Key，变化时中止子树重评估（打断用）
```

### 叶节点（真正干活）

```csharp
.Do(c => NodeStatus)                     // 同步动作
.DoAsync((c, ct) => UniTask<NodeStatus>) // 异步动作（等动画/Timeline，见 §5）
.Check(c => bool)                        // 纯条件判断
```

---

## 5. 异步动作（等动画播完再继续）

`DoAsync` 用于「播放攻击动画并等它结束」这类场景。首次 Tick 启动，返回 `Running`；完成后返回结果。**必须尊重传入的 CancellationToken**（被打断/暂停时会取消）。

```csharp
.DoAsync(async (c, ct) =>
{
    var anim = c.Blackboard.Get<Animator>("anim");
    anim.Play("HeavyAttack");
    // 等待动画播放到结束（示意）
    await UniTask.Delay(TimeSpan.FromSeconds(1.2f), cancellationToken: ct);
    return NodeStatus.Success;
})
```

> 规范：禁止 `async void`；异步一律用 `UniTask`；`ct` 必须传递到下游 await。

---

## 6. UtilitySelector：让敌人「按情况选招式」

当敌人有多个招式、要根据距离/血量等打分选最合适的一个时用它。给每个子节点用 `.WithUtility(...)` 附打分函数。

```csharp
.UtilitySelector()
    .Do(c => JumpSlam(c))
        .WithUtility(c => ScoreJumpSlam(c))                 // 原始分，建议 0~1
    .Do(c => SpinAttack(c))
        .WithUtility(c => ScoreSpin(c), weight: 1.5f)       // 静态权重放大偏好
    .Do(c => FireBreath(c))
        .WithUtility(c => c.Blackboard.Get<float>(BBKeys.HpRatio),
                     weight: 1f,
                     curve: enrageCurve)                     // AnimationCurve 重映射
.End()
```

打分流水线：`原始分 → Max 归一化到 0~1 → 权重曲线重映射 → 乘静态 weight → 取最高分执行`。

> **框架只提供打分机制**（归一化 + 曲线 + 权重 + 选择）；**具体打分函数、曲线形状怎么定，是你（架构/业务层）的事**。框架不内置任何游戏化评分规则。

---

## 7. 和 FSM 组合（黑魂式敌人推荐结构）

FSM 管宏观状态，`战斗` 状态内部挂一棵行为树：

```csharp
public class CombatState : State
{
    private BehaviorTree _tree;

    public override void OnEnter()
    {
        _tree = BehaviorTreeBuilder.Create("Combat")
            .Selector()
                .Reactive(BBKeys.HpRatio)          // 残血随时打断当前招式
                .Sequence()
                    .Check(c => c.Blackboard.Get<float>(BBKeys.HpRatio) < 0.3f)
                    .DoAsync((c, ct) => EnrageRoar(c, ct))
                    .UtilitySelector() /* 狂暴期选招 */ .End()
                .End()
                .Do(c => NormalCombo(c))
            .End()
            .Build(_blackboard);
    }

    public override void OnUpdate(float dt) => _tree.Tick(dt);
    public override void OnExit() => _tree.Abort();   // 离开战斗，清理树的 Running 状态
}
```

`Reactive(BBKeys.HpRatio)` 是 BT 相对 FSM 在 Boss 战上的核心优势：血量跨过阈值时**立刻中止当前正在执行的招式**并重新决策，实现「打到残血→秒切狂暴」。

---

## 8. 黑板使用与调试

```csharp
// 读写（类型安全）
bb.Set(BBKeys.Target, enemyTransform);
var t = bb.Get<Transform>(BBKeys.Target);
if (bb.TryGet<bool>(BBKeys.InMelee, out var inMelee)) { ... }

// 可选：响应式 Key（供 Reactive 装饰器或 UI 订阅）
bb.Observe<float>(BBKeys.HpRatio).Subscribe(hp => { ... }).AddTo(this);
```

框架提供的**开发期辅助工具**（Release 可裁剪，不定义 AI 规则）：

- 同一 Key 用不同类型读写 → Debug 下告警（防止 int/float 混用）。
- 读取从未 Set 过的 Key → Debug 下告警。
- `bb.CaptureKeys()` / `tree.CaptureSnapshot()` → 导出黑板/节点状态快照，供 DebugConsole 或未来 Editor 面板查看。

---

## 9. 生命周期与驱动

```csharp
// 推荐：绑定 Lifecycle，自动 Tick，暂停感知
tree.BindToLifecycle(UpdateChannel.Logic).AddTo(this);

// 手动驱动（测试 / 自定义循环）
tree.Tick(dt);

// 重置到初始 / 中止所有 Running 节点
tree.Reset();
tree.Abort();
```

- 绑 `UpdateChannel.Logic`：游戏暂停时 AI 自动冻结（与 FSM 一致）。

---

## 10. 常见坑

| 现象 | 原因 / 处理 |
|------|------------|
| 招式一帧就切走 / 决策抖动 | 长动作要返回 `Running` 直到真正结束，别每帧返回 Success/Failure |
| 异步节点无法被打断 | `DoAsync` 必须把 `ct` 传给下游所有 `await` |
| 暂停时敌人还在动 | 用 `BindToLifecycle(UpdateChannel.Logic)`，别绑 Default |
| 节点间强耦合、互相持引用 | 数据一律走 `Blackboard`，不要节点直接引用节点 |
| 魔法字符串到处飞 | Key 统一定义在业务侧 `BBKeys` 常量表 |
| 想做「目标规划」AI | 本框架不支持 GOAP/HTN；用 BT + UtilitySelector 表达 |

---

## 11. 现状说明

- 本模块目前处于 **🟨 设计中**，设计契约已锁定，尚未产出运行时代码。
- 本指南描述的是**目标 API**，实现时以设计契约（§0 决策记录、§7 Builder API）为准，若实现细节调整会同步回本指南。
- 现阶段**不提供可视化编辑界面**，仅提供代码 Builder + 运行时状态快照接口（供后续 DebugConsole/Editor Tools 消费）。
