# BehaviorTree（行为树）模块功能设计指南

> **文档定位**：本文件是 BehaviorTree（行为树）模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、API 契约与数据结构，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的公开 API 语义**。
>
> **模块编号**：3-4（AISystem 子系列，归属 Phase 3 玩法支撑层）。
> **前置依赖**：Lifecycle（Tick 驱动）、EventBus（可选，行为事件广播）、R3（可选，黑板响应式属性）、Entity（可选，行为树作为 Entity 的一种能力）、FSM/HSM（同层能力，非依赖）。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 | 理由 |
|--------|------|------|
| 1. 是否引入商店行为树插件（Behavior Designer 等） | ❌ **不引入**，自研纯 C# 行为树 | 商店插件重度依赖 MonoBehaviour + 可视化编辑器 + 反射序列化，与 PumpGF「纯 C# / 代码优先 / Builder API」气质冲突；引入盗版代码有法律与维护双重风险，引擎升级必炸且无官方修复 |
| 2. 实现风格 | 纯 C#，不依赖 MonoBehaviour | 与 FSM/HSM、Entity 一致 |
| 3. 构建方式 | 流畅 Builder API（`BehaviorTreeBuilder`），SO 配置树预留 | 与 `StateMachineBuilder` 保持一致的手感 |
| 4. Tick 驱动 | 自动绑定 Lifecycle 通道（推荐）+ 手动 Tick（测试/自定义循环） | 与 FSM 一致，复用 `UpdateChannel` 暂停感知 |
| 5. 节点返回值 | 三态 `NodeStatus { Success, Failure, Running }` | 行为树工业标准 |
| 6. 黑板（Blackboard） | 内置类型安全黑板（`Blackboard`），支持 R3 响应式 Key（可选） | 节点间共享数据的标准载体，避免节点硬耦合 |
| 7. 异步节点 | 支持 UniTask 异步叶节点（`IAsyncAction`），内部转 Running 语义 | 复用框架 UniTask，处理「播放动画等待结束」等场景 |
| 8. Utility AI | 作为行为树内的一种复合节点 `UtilitySelector` 预留，**不做独立框架**；打分**支持归一化 + 权重曲线**（`AnimationCurve` 重映射），非仅取最高分 | 「选哪个」用 Utility 打分，「怎么执行」用 BT，分层而非并列；曲线让业务能精细调优决策手感 |
| 12. Parallel 策略 | 首版实现 `SuccessPolicy.RequireOne + FailurePolicy.RequireAll` 组合 | 覆盖「任一子行为成功即整体成功、任一失败才整体失败」的最常用语义 |
| 13. Blackboard Key | 走业务侧 `BBKeys` 常量表，框架**只提供 Key 提示/校验工具**，不定义具体 AI 规则 | 框架提供工具，AI 规则定义由架构层负责 |
| 14. 命名空间 | `PumpGF`（原计划 `PumpGF.AI.BehaviorTree`，编码时已统一为 `PumpGF` 以保持与 FSM/Entity 一致） | 见 §13 |
| 9. GOAP / HTN | ❌ **明确不做** | 目标规划型 AI，动作游戏 Boss 用不上，属于过度设计 |
| 10. BT 与 FSM 关系 | 二者并存、互不依赖，可组合（FSM 状态内挂 BT / BT 叶节点驱动 FSM） | 见「§11 BT 与 FSM 的分工与协作」 |
| 11. 事件驱动重评估 | 支持基于 Blackboard 变化的条件中止（`Abort` / Reactive Decorator）。Reactive 提供「值变即中止」（离散 Key）与「谓词判定中止」（连续 Key 跨越阈值）两种重载 | 应对黑魂式 Boss「被打断→切换招式」的反应式需求；谓词重载避免连续浮点 Key 每帧误打断 |
| 底层 | 纯 C# + R3（可选）+ UniTask（异步节点）+ Lifecycle（Tick） | — |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**BehaviorTree 是纯 C# 的行为树框架**——
通过组合 Composite / Decorator / Leaf 节点，以流畅 Builder 构建可复用、可打断、可组合的 AI 行为逻辑，
共享类型安全黑板，与 Lifecycle/EventBus/Entity 集成。
**但不实现具体游戏 AI 逻辑**（那是业务层的事）。

### 1.2 为什么是行为树（而非 Utility / GOAP / HTN）

| AI 范式 | 解决的问题 | 在 PumpGF 中的定位 |
|---------|-----------|-------------------|
| **行为树 (BT)** | 「怎么执行」——反应式、可组合、可打断的行为编排 | **主力**。动作游戏/黑魂式 Boss 的近战连段、翻滚、突进、阶段切换的 90% 答案 |
| **Utility AI** | 「选哪个」——按距离/血量/仇恨打分选择招式 | 作为 BT 内的 `UtilitySelector` 复合节点嵌入，非独立框架 |
| **有限状态机 (FSM)** | 少量互斥宏观状态（巡逻/战斗/死亡） | 已有 FSM/HSM 模块，与 BT 组合使用 |
| **GOAP / HTN** | 「目标规划」——潜行 NPC、模拟经营、策略单位 | **不做**，动作游戏用不上，过度设计 |

> **设计立场**：不追求「一个框架应对一切」。正确做法是分层——BT 主导执行，Utility 作为 BT 的决策节点，FSM 管宏观状态，三者组合覆盖动作游戏几乎全部 AI 需求。

### 1.3 职责清单

| 职责 | 说明 |
|------|------|
| 节点体系 | Composite（Sequence/Selector/Parallel）、Decorator（Inverter/Repeat/Cooldown/Condition）、Leaf（Action/Condition） |
| 三态执行 | 每个节点 Tick 返回 Success/Failure/Running |
| 流畅 Builder | `BehaviorTreeBuilder` 链式构建树 |
| 黑板 | 类型安全 `Blackboard`，节点间共享数据，可选 R3 响应式 Key |
| 异步叶节点 | 支持 UniTask 异步 Action（映射为 Running 语义） |
| 条件中止 | Reactive/Abort 装饰器，黑板变化触发子树中止重评估 |
| Utility 选择 | `UtilitySelector` 复合节点，按打分函数选子节点 |
| Lifecycle 集成 | 可选绑定 Update 通道自动 Tick，暂停感知 |
| EventBus 集成 | 行为开始/结束/中止发事件（可开关） |
| Entity 集成 | 行为树可作为 Entity 持有的一种能力（普通字段，非组件） |
| 调试可视化 | 运行时节点状态快照接口（供 Phase 4 DebugConsole / Editor Tools 消费） |

### 1.4 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ 引入 Behavior Designer 等商店插件 | 见 §0 决策 1 |
| ❌ GOAP / HTN 规划器 | 见 §1.2 |
| ❌ 独立 Utility AI 框架 | 收敛为 BT 内的 `UtilitySelector` |
| ❌ 行为树可视化编辑器 | Phase 4 Editor Tools 阶段，本模块只提供状态快照接口 |
| ❌ SO 行为树图完整实现 | 本阶段预留接口，代码 Builder 为主 |
| ❌ 感知系统（Perception/视锥/听觉） | 独立模块（未来 `Sensor`），BT 只从黑板读感知结果 |
| ❌ 导航寻路（NavMesh/A*） | 业务层/引擎 NavMesh，BT 叶节点调用即可 |
| ❌ 多线程 / Job 化 Tick | 单线程，简单可靠 |

---

## 2. 整体架构

```
┌─────────────────────────────────────────────┐
│                业务逻辑层                    │
│  (敌人/BOSS 用 BehaviorTree 编排行为)        │
└────────────────────┬────────────────────────┘
                     │ Builder / Tick / Blackboard
                     ▼
┌─────────────────────────────────────────────┐
│                BehaviorTree                  │
│  ┌────────────────────────────────────────┐ │
│  │ Root Node（根节点，通常是 Composite）  │ │
│  │  Tick(context) → NodeStatus            │ │
│  └────────────────────────────────────────┘ │
│  ┌────────────────────────────────────────┐ │
│  │ Blackboard（类型安全共享数据）         │ │
│  │  Get<T>(key) / Set<T>(key,val)         │ │
│  │  可选 R3 响应式 Key                    │ │
│  └────────────────────────────────────────┘ │
│  ┌────────────────────────────────────────┐ │
│  │ TickContext（每次 Tick 的上下文）      │ │
│  │  Blackboard + deltaTime + Owner        │ │
│  └────────────────────────────────────────┘ │
└──────┬───────────────────┬───────────────────┘
       │ BindToLifecycle    │ Publish（可选）
       ▼                     ▼
┌──────────────┐     ┌──────────────┐
│  Lifecycle   │     │   EventBus   │
│ (Update通道) │     │ (BehaviorEvt)│
└──────────────┘     └──────────────┘
```

---

## 3. 核心：节点体系与三态执行

### 3.1 NodeStatus（三态）

```csharp
public enum NodeStatus
{
    Success,   // 节点完成，成功
    Failure,   // 节点完成，失败
    Running,   // 节点仍在执行中，下一帧继续 Tick
}
```

### 3.2 节点基类

```csharp
public abstract class BTNode
{
    // 每次 Tick 由父节点调用；context 携带黑板/dt/owner
    public NodeStatus Tick(in TickContext context);

    // 生命周期钩子（首次进入 Running → OnEnter；Running 结束 → OnExit）
    protected virtual void OnEnter(in TickContext context) { }
    protected abstract NodeStatus OnTick(in TickContext context);
    protected virtual void OnExit(in TickContext context) { }

    // 中止：外部/装饰器请求节点提前结束（清理 Running 状态）
    public virtual void Abort(in TickContext context);
}
```

> **Running 状态语义**：节点返回 Running 时，父节点应在下一帧对同一节点继续 Tick（记忆节点），而非重头开始。Composite 节点需正确记忆「上次执行到第几个子节点」。

### 3.3 节点分类

```
BTNode（抽象基类）
  ├─ CompositeNode（有多个子节点）
  │   ├─ SequenceNode      // 顺序执行，遇 Failure/Running 短路
  │   ├─ SelectorNode      // 择一执行，遇 Success/Running 短路
  │   ├─ ParallelNode      // 并行执行子节点，按策略判定整体结果
  │   └─ UtilitySelectorNode // 按打分函数选择得分最高的子节点（Utility AI 收敛点）
  ├─ DecoratorNode（有且仅有一个子节点）
  │   ├─ InverterNode      // Success↔Failure 取反
  │   ├─ RepeatNode        // 重复 N 次 / 直到失败
  │   ├─ CooldownNode      // 冷却期内直接返回 Failure
  │   ├─ ConditionNode     // 条件不满足直接返回 Failure（守卫）
  │   └─ ReactiveNode      // 监听黑板 Key，满足中止判定时中止子树重评估（事件驱动）
│                        //   离散 Key：值变即中止；连续 Key：需传谓词做「跨越阈值」判定
  └─ LeafNode（无子节点，真正干活）
      ├─ ActionNode        // 执行一个动作（同步）
      ├─ AsyncActionNode   // 执行一个 UniTask 异步动作（映射 Running）
      └─ ConditionLeafNode // 纯条件判断，返回 Success/Failure
```

### 3.4 Composite 短路规则

| 节点 | 遇 Success | 遇 Failure | 遇 Running |
|------|-----------|-----------|-----------|
| **Sequence** | 继续下一个 | 立即返回 Failure | 立即返回 Running（记忆位置） |
| **Selector** | 立即返回 Success | 继续下一个 | 立即返回 Running（记忆位置） |
| **Parallel** | 按 SuccessPolicy 判定 | 按 FailurePolicy 判定 | 未判定则整体 Running |

> **Parallel 策略（首版锁定）**：`SuccessPolicy.RequireOne`（任一子节点 Success 则整体 Success）+ `FailurePolicy.RequireAll`（全部子节点 Failure 才整体 Failure）。枚举保留 RequireAll/RequireOne 两个取值以便未来扩展，但首版 Builder 默认套用该组合。

### 3.5 UtilitySelector 打分机制（Utility AI 收敛点）

`UtilitySelectorNode` 每次 Tick 时对每个子节点计算「效用分」，选出得分最高者执行（择一，语义上是「加权评分的 Selector」）。

**打分流水线**（每个子节点）：

```
rawScore = scorer(context)                    // 业务提供的原始打分（建议 0~1）
normalized = Normalize(rawScore, allRawScores) // 归一化（见下）
shaped = (curve != null) ? curve.Evaluate(normalized) : normalized  // 权重曲线重映射
finalScore = shaped * weight                  // 静态权重系数
```

- **归一化（Normalize）**：将当前所有子节点的 rawScore 映射到 `[0,1]`（首版采用 Max 归一化：`score / maxScore`，`maxScore == 0` 时全部记 0）。这样即使业务打分量纲不一致（距离用米、血量用比例）也能公平比较。
- **权重曲线（curve）**：可选 `AnimationCurve`，对归一化后的分数做非线性重映射，让业务在 Inspector/代码里精细调优决策手感（如「血量越低越倾向狂暴，但不是线性」）。
- **静态权重（weight）**：整体缩放该子节点的最终分，用于粗粒度偏好设置。
- **选择规则**：取 finalScore 最高的子节点 Tick；该子节点返回 Running 时，下一帧默认**继续执行同一子节点**（避免决策抖动），除非该子节点完成或被 Reactive/Abort 中止后才重新评分。

> **职责边界**：框架只提供打分流水线（归一化 + 曲线 + 权重）与选择机制；**具体的 scorer 打分函数、曲线形状、Key 语义由架构层/业务层定义**。框架不内置任何游戏化的评分规则。

---

## 4. Blackboard（黑板）

### 4.1 定位

黑板是节点间共享数据的标准载体，**避免节点直接持有彼此引用造成硬耦合**。感知结果、目标引用、临时计算值都放黑板。

### 4.2 API 契约

```csharp
public sealed class Blackboard
{
    // 类型安全读写
    T Get<T>(string key);
    bool TryGet<T>(string key, out T value);
    void Set<T>(string key, T value);
    bool Has(string key);
    void Remove(string key);

    // 可选：R3 响应式 Key（供 ReactiveNode / 业务订阅）
    // 仅当以响应式方式写入时才创建，避免全量装箱开销
    Observable<T> Observe<T>(string key);
}
```

### 4.3 约定

- Key **统一走业务侧 `static class BBKeys` 常量表**集中定义，禁止散落魔法字符串。**框架不定义任何具体 Key**（那是架构层/业务层的 AI 规则），只提供工具。
- 黑板不做序列化（存档由 GameDataStore 负责）。
- 响应式 Key 为**可选**特性，默认不创建 Subject，避免无谓分配（与 Entity 模块「ReactiveProperty 可选」一致）。

### 4.4 框架提供的 Key 提示/校验工具（框架只做工具，不定规则）

| 工具 | 契约 | 说明 |
|------|------|------|
| 类型一致性校验 | `Get<T>` / `Set<T>` 对同一 Key 用不同 `T` 时，Debug 构建下 `Log.Warning` 提示类型冲突 | 防止业务把同一 Key 当 int 又当 float 用 |
| 未定义 Key 读取提示 | `Get<T>` 读取一个从未 `Set` 过的 Key 时，Debug 构建下告警（返回 `default`） | 及早暴露拼写/时序错误 |
| Key 快照 | `Blackboard.CaptureKeys()` 返回当前所有 `(key, type, value)` | 供 DebugConsole / Editor 面板展示，辅助人类核对 BBKeys 是否齐全 |

> 以上均为**开发期辅助工具**，Release 构建下告警可裁剪。框架不校验「某个 AI 应该有哪些 Key」——这是架构层的职责。

---

## 5. TickContext（Tick 上下文）

```csharp
public readonly struct TickContext
{
    public readonly Blackboard Blackboard;
    public readonly float DeltaTime;
    public readonly object Owner;   // 行为树拥有者（Entity / MonoBehaviour / 自定义），叶节点按需强转
}
```

> 用 `readonly struct` 按引用传递（`in`），避免每帧每节点的 GC 分配。

---

## 6. BehaviorTree（树实例）

```csharp
public sealed class BehaviorTree
{
    public Blackboard Blackboard { get; }
    public NodeStatus LastStatus { get; }

    // 手动驱动一帧
    public NodeStatus Tick(float deltaTime);

    // 重置：清空所有节点 Running 状态，回到初始
    public void Reset();

    // 中止整棵树（清理所有 Running 节点）
    public void Abort();

    // 调试：导出当前节点状态快照（供 DebugConsole / Editor Tools）
    public BTSnapshot CaptureSnapshot();
}
```

---

## 7. BehaviorTreeBuilder 流畅 API

### 7.1 Builder 结构（示意）

```csharp
public sealed class BehaviorTreeBuilder
{
    public static BehaviorTreeBuilder Create(string name = null);

    // 复合节点（需 .End() 收尾）
    public BehaviorTreeBuilder Sequence(string name = null);
    public BehaviorTreeBuilder Selector(string name = null);
    public BehaviorTreeBuilder Parallel(ParallelPolicy policy, string name = null);
    public BehaviorTreeBuilder UtilitySelector(string name = null);

    // 装饰器（作用于紧随其后的一个节点/子树）
    public BehaviorTreeBuilder Inverter();
    public BehaviorTreeBuilder Repeat(int count);       // count < 0 表示无限
    public BehaviorTreeBuilder Cooldown(float seconds);
    public BehaviorTreeBuilder Condition(Func<TickContext, bool> predicate);

    // Reactive 有两个重载（实现为泛型，需指定被监听 Key 的类型 T）：
    // 1) 离散语义：值发生「任意变化」即中止子树重评估。仅适合离散状态 Key（bool/enum/int）。
    public BehaviorTreeBuilder Reactive<T>(string watchKey);
    // 2) 谓词语义：仅当 shouldAbort(旧值, 新值) 为 true 时才中止。连续值 Key（如 HpRatio）必须用此重载做「跨越阈值」判定。
    public BehaviorTreeBuilder Reactive<T>(string watchKey, Func<T, T, bool> shouldAbort);

    // 叶节点
    public BehaviorTreeBuilder Do(Func<TickContext, NodeStatus> action, string name = null);
    public BehaviorTreeBuilder DoAsync(Func<TickContext, CancellationToken, UniTask<NodeStatus>> action, string name = null);
    public BehaviorTreeBuilder Check(Func<TickContext, bool> condition, string name = null);

    // UtilitySelector 专用：给上一个子节点附加打分（原始分，建议 0~1，非强制）
    // weight：静态权重系数；curve：可选权重曲线，对归一化后的分数做重映射
    public BehaviorTreeBuilder WithUtility(
        Func<TickContext, float> scorer,
        float weight = 1f,
        AnimationCurve curve = null);

    // 收尾
    public BehaviorTreeBuilder End();      // 结束当前复合节点
    public BehaviorTree Build(Blackboard blackboard = null);
}
```

### 7.2 使用示例（黑魂式 Boss 简化版）

```csharp
var tree = BehaviorTreeBuilder.Create("Boss")
    .Selector()                                   // 从上到下择一
        // —— 阶段：血量低于 30% 进入狂暴
        // Reactive 用「跨越阈值」谓词：仅在 HpRatio 从 >=0.3 掉到 <0.3 的那一刻打断当前招式，
        // 而非每帧变化都打断（连续浮点 Key 必须这样用，否则招式动画会被反复打断）。
        .Reactive<float>(BBKeys.HpRatio, (oldV, newV) => oldV >= 0.3f && newV < 0.3f)
        .Sequence()
            .Check(c => c.Blackboard.Get<float>(BBKeys.HpRatio) < 0.3f)
            .DoAsync((c, ct) => PlayEnrageRoar(c, ct))   // 播放咆哮动画，等待结束
            .UtilitySelector()                            // 狂暴期按情况选招式
                .Do(c => DoJumpSlam(c)).WithUtility(c => ScoreJumpSlam(c))
                .Do(c => DoSpinAttack(c)).WithUtility(c => ScoreSpin(c))
                .Do(c => DoFireBreath(c)).WithUtility(c => ScoreFire(c))
            .End()
        .End()
        // —— 常规战斗
        .Sequence()
            .Check(c => IsTargetInMeleeRange(c))
            .Selector()
                .Cooldown(3f).Do(c => DoHeavyCombo(c))    // 重击有冷却
                .Do(c => DoLightAttack(c))
            .End()
        .End()
        // —— 距离过远：突进
        .Do(c => DashToTarget(c))
    .End()
    .Build(blackboard);

tree.BindToLifecycle(UpdateChannel.Logic).AddTo(this);
```

> 说明：`Reactive<float>(BBKeys.HpRatio, (o, n) => o >= 0.3f && n < 0.3f)` 装饰器监听黑板血量比，仅在**跨越 0.3 阈值的那一刻**中止当前 Running 子树、触发重评估，从而实现「被打到残血→立刻切狂暴」的反应式打断——这正是行为树相对 FSM 在 Boss 战上的核心优势。
>
> **⚠️ 变化 vs 阈值（务必区分）**：`Reactive` 有两种语义：
> - **离散 Key** 用无谓词重载 `Reactive<T>(key)`——值发生任意变化即打断，适合 `IsEnraged: bool`、`Phase: int` 这类状态位。
> - **连续 Key**（如每帧都在微小变化的 `HpRatio: float`）**必须**用谓词重载 `Reactive<T>(key, shouldAbort)`，仅在业务关心的阈值跨越时才返回 true。否则连续 Key 会导致子树**几乎每帧被中止重进入**，招式动画/异步动作被反复打断，永远播不完。
> - 另一种规避方式：业务侧只往黑板写**离散状态 Key**（如把 `HpRatio < 0.3` 归约成 `IsEnraged: bool` 再写黑板），Reactive 监听该离散 Key 即可用默认重载。

---

## 8. 异步叶节点（UniTask 集成）

### 8.1 语义

`DoAsync` 传入返回 `UniTask<NodeStatus>` 的委托。执行规则：

1. 首次 Tick：启动 UniTask，节点立即返回 `Running`。
2. 后续 Tick：UniTask 未完成 → 继续返回 `Running`；已完成 → 返回其结果（Success/Failure）。
3. 节点被 `Abort` 或树 `Reset` → 取消 UniTask（传入的 `CancellationToken` 触发）。

### 8.2 约束（对齐框架规范）

- 异步动作**必须**接收并尊重 `CancellationToken`（用于中止/暂停时取消）。
- 禁止 `async void`；内部统一 `UniTask`。
- 典型用途：「播放攻击动画并等待命中帧/结束」「等待 Timeline 播放完」。

---

## 9. 与 Lifecycle 集成

```csharp
public static class BehaviorTreeExtensions
{
    // 内部订阅 Lifecycle.OnUpdate(channel)，调用 tree.Tick(dt)
    public static IDisposable BindToLifecycle(this BehaviorTree tree, UpdateChannel channel);
}
```

- 绑定 `UpdateChannel.Logic` → 暂停时不 Tick → AI 冻结（对齐 FSM 行为）。
- 手动 Tick：`tree.Tick(dt)` 用于测试或自定义循环。

---

## 10. 与 EventBus / Entity 集成

### 10.1 EventBus（可选，可开关）

```csharp
public readonly struct BehaviorNodeEvent
{
    public readonly string TreeName;
    public readonly string NodeName;
    public readonly NodeStatus Status;   // 节点进入/结束/中止
}
```

行为节点进入/结束/中止时发事件，供业务侧（如动画、音效、调试）订阅。默认关闭以省开销，Builder 上提供 `.EnableEvents()` 开关。

### 10.2 Entity（可选）

行为树可作为 Entity 持有的一种能力字段（与 Entity 持有 StateMachine 的方式一致，普通字段、非组件）。`TickContext.Owner` 可设为该 Entity，叶节点从而访问 Entity 的组件/黑板。

---

## 11. BT 与 FSM 的分工与协作

二者**并存、互不依赖**，按粒度分工：

| 维度 | FSM / HSM | BehaviorTree |
|------|-----------|--------------|
| 擅长 | 少量互斥宏观状态（巡逻/战斗/死亡/眩晕） | 复杂、可组合、可打断的行为编排 |
| 结构 | 状态 + 转换（图） | 节点组合（树） |
| 典型用法 | 敌人宏观状态切换 | 「战斗」状态内部的招式选择与执行 |

**推荐组合模式（二选一，业务按需）**：

- **FSM 主 + BT 辅**：FSM 管宏观状态，`战斗` 状态的 `OnEnter` 启动一棵 BT、`OnUpdate` 驱动 `tree.Tick`、`OnExit` 调 `tree.Abort`。这是黑魂式敌人的推荐结构。
- **BT 主 + FSM 辅**：整体一棵 BT，叶节点通过黑板/调用切换某个局部 FSM（如武器状态机）。

> 框架不强制某种组合，只保证两模块 API 可无缝互调（均纯 C#、均支持 Lifecycle 绑定与 Abort/Reset）。

---

## 12. 调试与可视化（接口预留）

- `BehaviorTree.CaptureSnapshot()` 返回 `BTSnapshot`：节点树结构 + 每个节点上次 Tick 的 `NodeStatus` + 当前 Running 路径。
- 本模块**只提供快照数据接口**，不实现 UI。
- Phase 4 `DebugConsole` / `Editor Tools` 消费该快照，实现运行时行为树可视化调试。

---

## 13. 目录与命名约定

```
Runtime/AI/BehaviorTree/
  BTNode.cs                // 基类 + NodeStatus + TickContext
  Composite/               // Sequence / Selector / Parallel / UtilitySelector
  Decorator/               // Inverter / Repeat / Cooldown / Condition / Reactive
  Leaf/                    // Action / AsyncAction / ConditionLeaf
  Blackboard.cs
  BehaviorTree.cs
  BehaviorTreeBuilder.cs
  BehaviorTreeExtensions.cs
  BTSnapshot.cs
```

- 命名空间：**`PumpGF`**（与 FSM/Entity 等所有运行时模块一致；`PumpGF.Runtime.asmdef` 的 `rootNamespace` 即 `PumpGF`）。
  > 说明：原计划的 `PumpGF.AI.BehaviorTree` 会破坏框架命名空间一致性（业务将被迫 `using PumpGF.AI.BehaviorTree`，而 FSM/Entity 都在 `PumpGF` 下）。编码时已统一为 `PumpGF`，文件按 `Runtime/AI/BehaviorTree/` 目录归类即可。
- 归入现有 `PumpGF.Runtime.asmdef`，不新增独立程序集（保持与 FSM/Entity 一致）。

---

## 14. 分阶段实现建议（供 Coding AI 排期）

| 阶段 | 内容 | 说明 |
|------|------|------|
| BT-1 | NodeStatus / BTNode / TickContext / Blackboard | 地基 |
| BT-2 | Sequence / Selector + ActionNode / ConditionLeafNode + Builder 骨架 | 最小可用树 |
| BT-3 | Decorator（Inverter/Repeat/Cooldown/Condition）+ Parallel | 完整节点体系 |
| BT-4 | AsyncActionNode（UniTask）+ Lifecycle 绑定 | 异步与驱动 |
| BT-5 | ReactiveNode + Blackboard 响应式 Key + Abort 机制 | 事件驱动打断（Boss 战关键） |
| BT-6 | UtilitySelector + WithUtility | Utility AI 收敛点 |
| BT-7 | EventBus 集成 + CaptureSnapshot 调试接口 | 可观测性 |
| BT-8 | 单元测试（PumpGF.Tests）+ 使用示例文档 | 交付 |

> **实现纪律**：每阶段完成后跑通 `PumpGF.Tests`，禁止在无本设计契约支撑的情况下扩展公开 API 语义。

---

## 15. 人类架构师确认结论（已锁定，2026-07-21）

| # | 确认项 | 结论 |
|---|--------|------|
| 1 | Utility 打分归一化/权重曲线 | ✅ **需要**。支持 Max 归一化 + `AnimationCurve` 权重曲线 + 静态 weight（见 §3.5、§0 决策 8） |
| 2 | Parallel 首版策略 | ✅ 按建议：`SuccessPolicy.RequireOne + FailurePolicy.RequireAll`（见 §3.4、§0 决策 12） |
| 3 | Blackboard Key | ✅ 走业务侧 `BBKeys` 常量表；框架**只提供 Key 提示/校验工具**，不定义 AI 规则（见 §4.4、§0 决策 13） |
| 4 | 命名空间 | ✅ 统一为 `PumpGF`（原计划 `PumpGF.AI.BehaviorTree`，为与 FSM/Entity 命名空间一致，编码时已改为 `PumpGF`，文件仍按 `Runtime/AI/BehaviorTree/` 目录归类；见 §13、§0 决策 14） |

> 全部确认项已锁定，决策记录（§0）已补录。本模块进入可编码状态（按 §14 分阶段实现）。
