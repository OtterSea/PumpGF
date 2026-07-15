# Event System 模块功能设计指南

> **文档定位**：本文件是 Event System 模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、API 契约与数据结构，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的公开 API 语义**。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 |
|--------|------|
| 1. EventBus 形式 | 作为 `IModule`，纳入 `GameGlobal` 统一管理，`Dispose` 时清理全局订阅与所有域 |
| 2. 事件载体 | `readonly struct`，零分配，泛型路由不装箱；约束"传 Id 不传大对象" |
| 3. 命名事件域 | 提供 `EventDomain`，支持按域隔离与一键 `Clear`/`Dispose` |
| 4. 通信模式 | 只做发布订阅（单向），不做请求-响应 |
| 5. 防闭包 | 提供 `SubscribeWithState`，热路径零闭包分配 |
| 6. 延迟派发 | 本阶段不做，业务用 R3 `BufferFrame`/`Collect` 自行聚合 |
| 7. 递归 Publish | 检测同类型递归 → Warning 不阻止，业务自负 |
| 风格 | 纯 C# 类 `EventBus : IModule`，与 PoolMgr/ResMgr/LifecycleMgr 一致 |
| 底层 | 基于 R3 `Subject<T>` 实现，订阅返回 R3 `IDisposable`，享受 R3 操作符 |
| 生命周期托管 | 所有订阅必须绑定生命周期（AddTo / RegisterTo / DisposableBag） |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**Event System 是类型安全的事件路由中枢**——
发布者与订阅者通过事件类型解耦，互不持有引用，支持全局总线与命名域隔离。
但**不承担事件聚合、持久化、请求-响应等职责**。

### 1.2 职责清单

| 职责 | 说明 |
|------|------|
| 类型安全事件总线 | 以事件类型（`struct`）为路由 key，编译期类型检查 |
| 全局事件域 | 默认全局总线，任意模块可发布/订阅 |
| 命名事件域 | `EventDomain`（如 "Battle"），域销毁时清空所有订阅 |
| R3 Observable 暴露 | `OnEvent<T>()` 返回 `IObservable<T>`，可链式操作 |
| 防闭包订阅 | `SubscribeWithState` 显式传 state，零闭包 |
| 生命周期托管 | 订阅返回 IDisposable，支持 AddTo/RegisterTo/DisposableBag |
| 调试支持 | 订阅信息查询，未绑定订阅检测 |

### 1.3 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ 请求-响应模式 | 跨模块同步查询用直接方法调用或 R3 `FirstAsync` 更合适 |
| ❌ 延迟/帧末派发 | 业务用 R3 `BufferFrame`/`Collect` 自行聚合 |
| ❌ 事件持久化/重放 | 本阶段不做，未来可扩展为 EventStore |
| ❌ 事件优先级仲裁 | 订阅顺序即派发顺序，不做优先级排序 |
| ❌ 替代 R3 Subject 局部事件 | 模块内部的事件仍直接用 Subject，EventBus 只管跨模块 |

---

## 2. 整体架构

```
┌─────────────────────────────────────────────────────────┐
│                   EventBus (IModule)                      │
│                                                          │
│  ┌──────────────────┐   ┌───────────────────────────┐   │
│  │ 全局事件路由表    │   │ 命名域表                   │   │
│  │ Type → Subject  │   │ string → EventDomain      │   │
│  └──────────────────┘   └──────────────┬────────────┘   │
│                                        │                  │
│  ┌──────────────┐  ┌──────────────┐   ▼ ┌───────────┐  │
│  │ SubscribeMgr │  │ RecursionGuard│   EventDomain │  │
│  │ (生命周期)   │  │ (递归检测)    │   (独立路由表)│  │
│  └──────────────┘  └──────────────┘     └───────────┘  │
└──────────────────────────┬──────────────────────────────┘
                           │ 基于 R3 Subject<T>
                           ▼
┌─────────────────────────────────────────────────────────┐
│                         R3                               │
│   Subject<T> / IDisposable / IObservable<T> / AddTo      │
└─────────────────────────────────────────────────────────┘

        │ Publish<TEvent> / Subscribe<TEvent>
        ▼
┌─────────────────┐                    ┌─────────────────┐
│  发布者(模块A)  │  ──互不引用──>     │  订阅者(模块B)  │
│  EventBus.Publish│                    │  EventBus.Subscribe│
└─────────────────┘                    └─────────────────┘
```

---

## 3. 核心设计：类型即路由 key

### 3.1 事件定义约定

事件必须是 **`readonly struct`**：

```
public readonly struct PlayerDiedEvent
{
    public readonly int PlayerId;
    public readonly Vector3 Position;
    public PlayerDiedEvent(int id, Vector3 pos) { ... }
}
```

**约定原因：**
- struct 通过泛型路由，**不装箱**（C# 泛型特化）。
- `readonly` 防止误改，语义清晰（事件是不可变快照）。
- 事件名以 `Event` 后缀，IDE 补全友好、可检索。

### 3.2 事件载体约束（强制规范）

**事件 struct 内禁止包含大型引用类型字段（如整个 Player/Enemy 对象）。**
应传标识符（Id）而非对象引用。

| ✅ 正确 | ❌ 错误 |
|---------|---------|
| `int PlayerId` | `Player Player`（传整个玩家对象） |
| `int EnemyId` | `Enemy Enemy`（传整个敌人对象） |
| `int ItemId, int Count` | `ItemStack Stack`（若 ItemStack 是引用类型且含集合） |

**原因：**
- 传整个对象 → 订阅者在回调里访问对象，可能已被回收/状态已变（事件是快照）。
- 传 Id → 订阅者按需查询当前状态，语义明确。
- 避免"事件持有大对象导致内存峰值"。

> **例外**：`Vector3`/`Quaternion`/小值类型 struct 字段允许。
> 纯数据 DTO（如 `DamageInfo` struct）允许，但应保持轻量。

### 3.3 路由机制

内部维护 `Dictionary<Type, object>`（value 是 `Subject<TEvent>` 的装箱引用）：

```
Publish<TEvent>(TEvent evt):
  若 _subjects[typeof(TEvent)] 存在:
    ((Subject<TEvent>)subject).OnNext(evt)  // 派发时零装箱（泛型）

Subscribe<TEvent>(Action<TEvent> handler):
  若不存在则 new Subject<TEvent>() 并缓存
  返回 subject.Subscribe(handler)  // R3 IDisposable
```

- **Subject 仅在创建时装箱一次**（存入 `Dictionary<Type, object>`），派发时不装箱。
- 懒创建：某类型事件从未被订阅时，不创建 Subject。

---

## 4. EventDomain 命名事件域

### 4.1 为什么需要域

- 全局总线跨场景/跨玩法，事件残留风险（旧订阅者悬空触发崩溃）。
- "战斗域"事件在战斗结束时一键清空。
- 域隔离：同类型事件在不同域互不干扰。

### 4.2 EventDomain 结构

| 成员 | 说明 |
|------|------|
| `Name` | 域名（如 "Battle"、"Menu"） |
| 独立路由表 | `Dictionary<Type, object>`，与全局总线隔离 |
| `Publish<TEvent>(evt)` | 域内发布 |
| `Subscribe<TEvent>(handler)` | 域内订阅，返回 IDisposable |
| `OnEvent<TEvent>()` | 返回域内 `IObservable<TEvent>` |
| `Clear()` | 清空该域所有订阅，域对象保留（可重新订阅） |
| `Dispose()` | Clear + 从 EventBus 注销该域 |

### 4.3 域的创建与缓存

```
EventDomain battle = EventBus.GetDomain("Battle");
```

- 首次 `GetDomain(name)` 时创建并缓存。
- 重复 `GetDomain(name)` 返回同一实例。
- 域的订阅独立于全局总线（同类型事件在全局和 Battle 域是两个独立 Subject）。

### 4.4 典型用法

```
// 进入战斗
var battleDomain = EventBus.GetDomain("Battle");
battleDomain.Subscribe<EnemyKilledEvent>(e => AddScore(e.Score)).AddTo(this);

// 退出战斗
battleDomain.Clear();  // 清空战斗期所有订阅，防止旧战斗对象悬空
// 或 battleDomain.Dispose();  // 彻底销毁域
```

### 4.5 全局总线与域的关系

- 全局总线是"匿名域"，所有未指定域的发布/订阅走全局。
- 域是"命名隔离区"，与全局互不干扰。
- 同一个事件类型在全局域和命名域是**两个独立 Subject**，发布到 Battle 域不会触发全局订阅者。

---

## 5. SubscribeWithState 防闭包分配

### 5.1 问题

普通 `Subscribe(e => this.HandleEvent(e))` 的 lambda 捕获 `this` → 产生闭包对象 → GC。
高频事件（如每帧伤害事件）下闭包分配累积。

### 5.2 方案

`SubscribeWithState` 显式传入 state，避免闭包：

```
EventBus.SubscribeWithState<PlayerDiedEvent, PlayerController>(
    this,                                    // state，显式传入
    (controller, e) => controller.HandleDeath(e))  // 静态委托，无闭包
    .AddTo(this);
```

- 内部用 R3 的 `Subscribe(state, action)` 重载，零闭包。
- 热路径（高频事件）推荐用此 API。
- 低频事件用普通 `Subscribe` 即可。

### 5.3 双参数与三参数版本

为常见场景提供重载（避免元组装箱）：

```
SubscribeWithState<TEvent, TState>(state, action)
SubscribeWithState<TEvent, TState1, TState2>(state1, state2, action)
```

> 三参数以上不提供，建议重构 state 为单个 struct。

---

## 6. 生命周期托管

### 6.1 绑定方式

| 场景 | 绑定 API |
|------|----------|
| MonoBehaviour | `.AddTo(monoBehaviour)` （R3 原生） |
| 纯 C# 类持有 GameObject | `.AddTo(gameObject)` 或 `RegisterTo(destroyCancellationToken)` |
| 纯 C# 类无 GameObject | `.AddTo(ref disposableBag)`，手动 Dispose |
| 绑定 CancellationToken | `.RegisterTo(ct)` |

### 6.2 强制约定

- **所有订阅必须绑定生命周期**，未绑定的订阅视为 bug。
- 框架**不自动管理**未绑定订阅（避免"隐式全局存活"导致的泄漏）。
- Debug 模式下可检测"未绑定订阅"并 Warning（见 §8.2）。

### 6.3 DisposableBag 用法

```
DisposableBag bag = default;
EventBus.Subscribe<A>(...).AddTo(ref bag);
EventBus.Subscribe<B>(...).AddTo(ref bag);
// 需要清理时
bag.Dispose();
```

---

## 7. 派发语义

### 7.1 同步派发（默认）

`Publish` 同步调用所有订阅者，按**订阅顺序**派发。
- 优点：简单、可预测、栈友好。
- 风险：订阅者在回调内修改状态，影响后续订阅者所见（事件是快照，回调内改的是当前状态）。

### 7.2 递归 Publish 检测

- 订阅者在回调内 `Publish` 同类型事件 → 递归 → 潜在栈溢出。
- 内部维护"正在派发的事件类型栈"。
- 检测到递归 `Publish` 同类型 → **Warning 日志**（不阻止，业务自负）。
- 不阻止的原因：某些设计有意递归（如事件驱动的状态机推进），阻止会破坏灵活性。

### 7.3 延迟派发（本阶段不做）

- 业务需要"同帧多事件帧末统一处理"时，用 R3 操作符：
  - `EventBus.OnEvent<T>().BufferFrame()` —— 按帧缓冲。
  - `EventBus.OnEvent<T>().Collect()` —— 收集。
- EventBus 保持单一职责（路由），不承担聚合。

---

## 8. 调试支持

### 8.1 订阅信息查询

```
EventBus.GetSubscriptionInfo() → IReadOnlyList<EventSubscriptionInfo>

struct EventSubscriptionInfo
{
    Type EventType;        // 事件类型
    string DomainName;     // 域名（全局为 null）
    int SubscriberCount;   // 订阅者数量
}
```

- 开发期 Debug 面板展示"谁订阅了什么"。
- 排查"事件没人响应"或"事件订阅者异常多"。

### 8.2 未绑定订阅检测（可选，预留）

- Debug 模式下，订阅返回的 IDisposable 若在若干帧后仍未绑定生命周期 → Warning。
- 本阶段可预留接口，实现可选。

---

## 9. API 契约（公开接口）

> 以下为**公开 API 契约**，实现时方法签名必须一致（参数名可微调，但语义不可变）。

### 9.1 全局总线

```
// 发布
void Publish<TEvent>(TEvent evt) where TEvent : struct;

// 订阅（普通）
IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : struct;

// 订阅（防闭包）
IDisposable SubscribeWithState<TEvent, TState>(
    TState state, Action<TState, TEvent> handler)
    where TEvent : struct;

IDisposable SubscribeWithState<TEvent, TState1, TState2>(
    TState1 state1, TState2 state2,
    Action<TState1, TState2, TEvent> handler)
    where TEvent : struct;

// 暴露 R3 Observable（享受操作符）
IObservable<TEvent> OnEvent<TEvent>() where TEvent : struct;
```

### 9.2 命名域

```
EventDomain GetDomain(string name);

sealed class EventDomain : IDisposable
{
    string Name { get; }
    void Publish<TEvent>(TEvent evt) where TEvent : struct;
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : struct;
    IDisposable SubscribeWithState<TEvent, TState>(TState state, Action<TState, TEvent> handler) where TEvent : struct;
    IObservable<TEvent> OnEvent<TEvent>() where TEvent : struct;
    void Clear();
    void Dispose();
}
```

### 9.3 调试

```
IReadOnlyList<EventSubscriptionInfo> GetSubscriptionInfo();

struct EventSubscriptionInfo
{
    Type EventType;
    string DomainName;   // null 表示全局
    int SubscriberCount;
}
```

### 9.4 IModule 实现

```
void Init();
void Dispose();  // 清理全局订阅 + 所有 EventDomain
```

---

## 10. 使用示例（伪代码，非最终实现）

### 10.1 定义事件

```
public readonly struct PlayerDiedEvent
{
    public readonly int PlayerId;
    public readonly Vector3 Position;
    public PlayerDiedEvent(int id, Vector3 pos)
    {
        PlayerId = id;
        Position = pos;
    }
}
```

### 10.2 发布与订阅（基础）

```
// 模块 A：玩家死亡时发布
GameGlobal.EventBus.Publish(new PlayerDiedEvent(playerId, position));

// 模块 B：订阅（MonoBehaviour）
GameGlobal.EventBus.Subscribe<PlayerDiedEvent>(e => HandleDeath(e.PlayerId))
    .AddTo(this);

// 模块 C：用 R3 操作符链式处理
GameGlobal.EventBus.OnEvent<PlayerDiedEvent>()
    .Where(e => e.PlayerId == 1)
    .ThrottleFrame(5)
    .Subscribe(e => Debug.Log($"Player {e.PlayerId} died at {e.Position}"))
    .AddTo(this);
```

### 10.3 防闭包订阅（热路径）

```
// 普通 Subscribe 会闭包捕获 this
GameGlobal.EventBus.Subscribe<DamageEvent>(e => this.ApplyDamage(e))  // ❌ 闭包

// SubscribeWithState 显式传 state，零闭包
GameGlobal.EventBus.SubscribeWithState<DamageEvent, Enemy>(this, (enemy, e) => enemy.ApplyDamage(e))
    .AddTo(this);  // ✅
```

### 10.4 命名域隔离

```
// 进入战斗
var battleDomain = GameGlobal.EventBus.GetDomain("Battle");

// 战斗期订阅走战斗域
battleDomain.Subscribe<EnemyKilledEvent>(e => AddScore(e.Score)).AddTo(this);
battleDomain.Subscribe<PlayerHitEvent>(e => ShowDamageNumber(e)).AddTo(this);

// 战斗内发布走战斗域
battleDomain.Publish(new EnemyKilledEvent(enemyId, score));

// 退出战斗：一键清空，防止旧战斗对象悬空
battleDomain.Clear();
```

### 10.5 纯 C# 类的订阅管理

```
public class ScoreSystem : IDisposable
{
    DisposableBag _bag;

    public void Init()
    {
        _bag = default;
        GameGlobal.EventBus.Subscribe<EnemyKilledEvent>(OnEnemyKilled).AddTo(ref _bag);
        GameGlobal.EventBus.Subscribe<BossDefeatedEvent>(OnBossDefeated).AddTo(ref _bag);
    }

    public void Dispose()
    {
        _bag.Dispose();  // 一次性取消所有订阅
    }
}
```

### 10.6 帧末聚合（用 R3 替代延迟派发）

```
// 同帧多个伤害事件，帧末统一结算
GameGlobal.EventBus.OnEvent<DamageEvent>()
    .BufferFrame()
    .Subscribe(damages =>
    {
        foreach (var d in damages) ApplyDamage(d);
    })
    .AddTo(this);
```

---

## 11. 实现检查清单

实现完成后，逐项自查：

- [ ] `EventBus : IModule`，纳入 GameGlobal
- [ ] 全局路由表 `Dictionary<Type, object>`，value 为 `Subject<TEvent>`
- [ ] `Publish<TEvent>` 泛型派发，零装箱
- [ ] `Subscribe<TEvent>` 返回 R3 IDisposable
- [ ] `OnEvent<TEvent>` 返回 `IObservable<TEvent>`（Subject 直接转）
- [ ] `SubscribeWithState` 双参数 + 三参数重载，零闭包
- [ ] `EventDomain` 独立路由表，与全局隔离
- [ ] `GetDomain(name)` 缓存，重复调用返回同实例
- [ ] `EventDomain.Clear()` 清空订阅但保留域对象
- [ ] `EventDomain.Dispose()` 清空 + 从 EventBus 注销
- [ ] `EventBus.Dispose()` 清理全局订阅 + 所有 EventDomain
- [ ] 递归 Publish 同类型 → Warning 日志（不阻止）
- [ ] `GetSubscriptionInfo()` 返回正确的事件类型/域/订阅数
- [ ] Subject 懒创建（未订阅的事件类型不创建）
- [ ] 所有公开 API 有中文 XML 注释
- [ ] 无硬编码（域名等由调用方传入）
- [ ] 错误处理：Subscribe null handler → 抛 ArgumentNullException；Publish 在无订阅者时静默跳过

---

## 12. 依赖关系

| 依赖项 | 方向 | 说明 |
|--------|------|------|
| R3 | 引用 | Subject/IDisposable/IObservable/AddTo |
| GameGlobal | 被引用 | 作为服务定位器暴露 EventBus |

> **与 Lifecycle 的关系**：EventBus 不依赖 LifecycleMgr。
> 事件派发是同步的，不需要 Lifecycle 的 Update 通道。
> 若业务需要"帧末 flush 事件"，用 R3 `BufferFrame`（内部走 Lifecycle 的 PlayerLoop）。

> **与 ResMgr 的关系**：无直接依赖。事件是内存中的瞬态流，不涉及资源加载。

---

## 13. 后续模块依赖本模块的接口

| 后续模块 | 使用的 EventBus 接口 |
|----------|---------------------|
| Config Manager (1-3) | 配置变更通知（`OnEvent<ConfigReloadedEvent>`） |
| Save/Load System (1-4) | 存档完成/加载完成事件 |
| Scheduler/Timer (1-5) | 定时器触发事件 |
| UI Framework (2) | UI 事件流转、ViewModel 间通信 |
| Audio Manager (2) | 音效播放事件（`PlaySfxEvent`） |
| Input Manager (2) | 输入事件分发 |
| FSM/HSM (3-1) | 状态转换事件 |
| Entity Component (3-2) | 组件变更事件 |
| Level/Scene Manager (3-3) | 场景切换事件（配合 Lifecycle 钩子） |
| 业务层 | 跨模块通信的主要手段 |

---

## 14. 关键使用规范（业务层 Coding AI 必读）

> 本节是**强制规范**，业务层 Coding AI 必须严格遵守。

### 14.1 事件必须用 readonly struct

- ❌ 禁止：`class PlayerDiedEvent`
- ❌ 禁止：用 `string` 作为事件 key（如 `EventBus.Send("PlayerDied", payload)`）
- ✅ 正确：`public readonly struct PlayerDiedEvent { ... }`

### 14.2 事件传 Id 不传大对象

- ❌ 禁止：`public readonly Player Player;`
- ✅ 正确：`public readonly int PlayerId;`（订阅者按需查询当前状态）
- 例外：`Vector3`/小值类型 struct 字段允许。

### 14.3 所有订阅必须绑定生命周期

- ❌ 禁止：`EventBus.Subscribe<X>(handler)` 不 AddTo（泄漏）
- ✅ 正确：`EventBus.Subscribe<X>(handler).AddTo(this)` / `AddTo(ref bag)` / `RegisterTo(ct)`

### 14.4 跨场景/跨玩法订阅用 EventDomain

- 战斗期订阅走 `EventBus.GetDomain("Battle")`，退出时 `Clear()`。
- 避免全局总线残留旧场景订阅。

### 14.5 热路径用 SubscribeWithState

- 每帧/高频事件用 `SubscribeWithState` 防闭包。
- 低频事件（如玩家死亡）用普通 Subscribe 即可。

### 14.6 不要在事件回调内同步 Publish 同类型事件

- 会触发递归 Warning。
- 如需级联，考虑用 `UniTask.NextFrame()` 延迟一帧，或重构为不同事件类型。

---

**文档结束。实现阶段请严格遵循本契约。**
