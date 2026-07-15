# Scheduler / Timer 模块功能设计指南

> **文档定位**：本文件是 Scheduler/Timer 模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、API 契约与数据结构，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的公开 API 语义**。
>
> **模块编号**：建议 1-6（因新增 GameDataStore 为 1-4，原 1-5 顺延）。
> **前置依赖**：Lifecycle（1-1）必须先实现，Scheduler 订阅其 Update 通道。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 |
|--------|------|
| 1. 时间源 | 基于 Lifecycle 通道的 deltaTime 驱动（订阅 Update），暂停/缩放自动感知 |
| 2. 定时器类型 | One-shot + Repeat + Frame-based 三种都提供 |
| 3. API 形态 | Awaitable（`Delay` 返回 UniTask）+ 回调注册（`Schedule` 返回 IDisposable）两者都提供 |
| 4. 暂停感知 | 定时器创建时指定通道（默认 Logic），暂停由通道决定 |
| 5. 定时器分组 | 支持 `TimerGroup`，可批量取消/暂停/恢复 |
| 6. Unscaled | 提供 Unscaled 选项（用 unscaledDeltaTime，不受暂停/缩放影响） |
| 7. GameObject 绑定 | 定时器可选绑定 GameObject，销毁时自动取消 |
| 风格 | `Scheduler : IModule`，纳入 `GameGlobal`，纯 C# 类 |
| 底层 | 基于 Lifecycle 的 Update 通道 + R3/UniTask |
| 池化 | 定时器对象池化（遵循"池化一切"原则） |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**Scheduler 是游戏定时任务的统一调度器**——
所有延迟执行、周期执行、帧驱动任务经此调度，自动感知 Lifecycle 的暂停与时间缩放。
但**不实现业务逻辑**，只提供"何时触发"的机制。

### 1.2 职责清单

| 职责 | 说明 |
|------|------|
| 延迟执行 | One-shot：延迟 N 秒执行一次 |
| 周期执行 | Repeat：每 N 秒执行一次（可配次数或无限） |
| 帧驱动 | Frame-based：每 N 帧执行一次 |
| 暂停感知 | 通过 Lifecycle 通道自动感知暂停/恢复 |
| 时间缩放 | 通过通道的 TimeScale 自动跟随缩放 |
| Unscaled | 提供不受暂停/缩放影响的定时器 |
| 生命周期绑定 | 可选绑定 GameObject，销毁时自动取消 |
| 分组管理 | TimerGroup 批量取消/暂停/恢复 |

### 1.3 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ 替代 UniTask.Delay | UniTask.Delay 适用于"不关心暂停"的场景（如纯 UI 动画），Scheduler 管游戏逻辑定时器 |
| ❌ 业务逻辑 | 只提供"何时触发"，不管"触发后做什么" |
| ❌ 跨场景持久化 | 定时器是运行时的，不存档 |
| ❌ 分布式调度 | 单机，不考虑多端同步 |
| ❌ CRON 表达式 | 过重，游戏用不上 |

---

## 2. 整体架构

```
┌─────────────────────────────────────────┐
│            业务逻辑层                    │
│  技能冷却/体力恢复/UI倒计时/轮询         │
└──────────────────┬──────────────────────┘
                   │ Schedule / Delay / DelayFrames
                   ▼
┌─────────────────────────────────────────┐
│           Scheduler (IModule)            │
│  ┌──────────────────────────────────┐   │
│  │ 定时器桶（按通道分桶）            │   │
│  │  Logic → List<Timer>             │   │
│  │  Default → List<Timer>           │   │
│  │  Unscaled → List<Timer>          │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ TimerGroup 表                    │   │
│  │  name → TimerGroup               │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ TimerPool (对象池)               │   │
│  └──────────────────────────────────┘   │
└──────────────────┬──────────────────────┘
                   │ 订阅 Update 通道
                   ▼
┌─────────────────────────────────────────┐
│           Lifecycle (1-1)                │
│  OnUpdate(channel) → 每帧/每逻辑帧回调   │
│  暂停时不派发 → 定时器冻结              │
└─────────────────────────────────────────┘
```

---

## 3. 时间源：Lifecycle 通道驱动

### 3.1 为什么不用 UniTask.Delay

- `UniTask.Delay` 用真实时间，**不感知暂停**（暂停时仍倒计时）。
- `UniTask.Delay` **不感知缩放**（子弹时间下不减速）。
- 散落各处，无法统一管理/统计/分组取消。

### 3.2 通道驱动原理

Scheduler 订阅 Lifecycle 的 Update 通道，在回调中累加 `deltaTime`：

```
每帧/每逻辑帧：
  foreach (timer in channelBucket):
    if (timer.IsPaused) continue;
    timer.Accumulator += deltaTime;  // 通道的 deltaTime（已感知暂停/缩放）
    if (timer.Accumulator >= timer.Interval):
      timer.Trigger();
      timer.Accumulator -= timer.Interval;
      if (timer is One-shot): timer.Dispose();
      if (timer is Repeat && --remaining == 0): timer.Dispose();
```

- 暂停时 Lifecycle 不派发 → 定时器**自然冻结**。
- 缩放由通道 `TimeScale` 决定 → 定时器**自动跟随**。
- 无需额外暂停管理逻辑。

### 3.3 通道选择

| 通道 | 适用 | 精度 | 暂停感知 |
|------|------|------|----------|
| `Logic`（默认） | 游戏逻辑（技能冷却、体力恢复） | 60Hz 固定 | 是 |
| `Default` | UI 定时器（淡入淡出） | 每帧 | 是 |
| `Unscaled` | 不受暂停/缩放（UI 强制倒计时） | 每帧 | 否 |

> **Unscaled 实现**：订阅 Lifecycle 的 Default 通道，但累加 `Time.unscaledDeltaTime` 而非通道 deltaTime。
> 这样仍统一管理，但不受暂停/缩放影响。

---

## 4. 定时器类型

### 4.1 One-shot（延迟执行一次）

```
IDisposable Schedule(float seconds, Action callback, ...);
UniTask Delay(float seconds, ...);
```

- 延迟 N 秒后执行一次，自动销毁。
- 最常用类型。

### 4.2 Repeat（周期执行）

```
IDisposable ScheduleRepeat(float interval, Action callback, int count = -1, ...);
```

- 每 `interval` 秒执行一次。
- `count`：执行次数，`-1` 表示无限。
- 执行完指定次数后自动销毁。

### 4.3 Frame-based（帧驱动）

```
IDisposable ScheduleEveryFrame(Action callback, int frameInterval = 1, ...);
UniTask DelayFrames(int frames, ...);
```

- 每 `frameInterval` 帧执行一次。
- 用帧计数器而非时间累加（精确帧数）。
- 适用：轮询、检查、帧级同步。

---

## 5. 双形态 API

### 5.1 Awaitable（异步流程）

```
UniTask Delay(float seconds, UpdateChannel channel = Logic, GameObject owner = null, CancellationToken ct = default);
UniTask DelayFrames(int frames, UpdateChannel channel = Default, GameObject owner = null, CancellationToken ct = default);
UniTask DelayUnscaled(float seconds, GameObject owner = null, CancellationToken ct = default);
```

- 返回 `UniTask`，可 `await`，融入异步流程。
- 适合：等待条件、异步序列中的延迟。

```
await GameGlobal.Scheduler.Delay(2f, channel: UpdateChannel.Logic);
Debug.Log("2 秒后（暂停时不计时）");
```

### 5.2 回调注册（fire-and-forget）

```
IDisposable Schedule(float seconds, Action callback, UpdateChannel channel = Logic, GameObject owner = null);
IDisposable ScheduleRepeat(float interval, Action callback, int count = -1, UpdateChannel channel = Logic, GameObject owner = null);
IDisposable ScheduleEveryFrame(Action callback, int frameInterval = 1, GameObject owner = null);
```

- 返回 `IDisposable`，Dispose 即取消。
- 适合：不关心 await 的周期任务。

```
GameGlobal.Scheduler.ScheduleRepeat(2f, () => RestoreStamina(1), count: -1)
    .AddTo(this);  // 绑定 MonoBehaviour 生命周期
```

---

## 6. 暂停感知与时间缩放

### 6.1 暂停感知

- 定时器走 Logic 通道 → PauseProfile 冻结 Logic 通道时 → 定时器不累加 → 自然暂停。
- 恢复时继续累加，无需额外处理。
- **与 Lifecycle 的 PauseProfile 完全联动**。

### 6.2 时间缩放

- 通道的 `TimeScale` 影响 `deltaTime` → 定时器自动跟随缩放。
- 如 `Logic.TimeScale = 0.5` → 所有 Logic 定时器减速一半。

### 6.3 单定时器暂停

- `TimerGroup.PauseAll()` / `ResumeAll()`：批量暂停/恢复组内定时器。
- 单个定时器也可 `Pause()`/`Resume()`（通过返回的 handle）。

---

## 7. Unscaled 定时器

### 7.1 适用场景

- UI 强制倒计时（暂停时仍倒数）。
- 不受子弹时间影响的定时器。
- 网络重连倒计时（单机版可忽略）。

### 7.2 API

```
UniTask DelayUnscaled(float seconds, GameObject owner = null, CancellationToken ct = default);
IDisposable ScheduleUnscaled(float seconds, Action callback, GameObject owner = null);
IDisposable ScheduleRepeatUnscaled(float interval, Action callback, int count = -1, GameObject owner = null);
```

### 7.3 实现

- 订阅 Lifecycle 的 Default 通道（每帧执行）。
- 累加 `Time.unscaledDeltaTime`（不受 TimeScale/暂停影响）。
- 仍纳入 Scheduler 统一管理（可分组/可统计）。

---

## 8. 定时器分组（TimerGroup）

### 8.1 设计

```
TimerGroup CreateGroup(string name);
```

- 创建命名分组，返回 `TimerGroup` 实例。
- 组内定时器可通过 `group.Schedule(...)` 添加。
- 可批量 `CancelAll()` / `PauseAll()` / `ResumeAll()`。

### 8.2 TimerGroup API

```
class TimerGroup : IDisposable
{
    IDisposable Schedule(float seconds, Action callback, UpdateChannel channel = Logic);
    IDisposable ScheduleRepeat(float interval, Action callback, int count = -1, UpdateChannel channel = Logic);
    IDisposable ScheduleEveryFrame(Action callback, int frameInterval = 1);

    void CancelAll();    // 取消组内所有定时器
    void PauseAll();     // 暂停组内所有定时器（不销毁）
    void ResumeAll();    // 恢复组内所有定时器
    int Count { get; }   // 组内定时器数量
    void Dispose();       // 等同 CancelAll + 从 Scheduler 注销
}
```

### 8.3 典型用法

```
// 进入战斗，创建战斗定时器组
var battleTimers = GameGlobal.Scheduler.CreateGroup("Battle");

// 战斗期定时器加入组
battleTimers.ScheduleRepeat(5f, () => SpawnEnemy(), count: -1);
battleTimers.ScheduleRepeat(2f, () => RestoreStamina(1), count: -1);

// 退出战斗：一键取消所有战斗定时器
battleTimers.Dispose();
```

---

## 9. GameObject 生命周期绑定

### 9.1 便捷重载

所有 API 提供 `GameObject owner` 参数：
```
GameGlobal.Scheduler.Schedule(5f, () => DoSomething(), owner: gameObject);
```

- `owner` 销毁时自动取消定时器。
- 内部用 `destroyCancellationToken` 监听。

### 9.2 与 CancellationToken 的关系

- `owner` 参数是 `destroyCancellationToken` 的语法糖。
- 业务也可直接传 `ct: destroyCancellationToken`。
- 两者等价，`owner` 更简洁。

---

## 10. API 契约（公开接口）

### 10.1 Scheduler

```
class Scheduler : IModule
{
    // ── Awaitable ──
    UniTask Delay(float seconds, UpdateChannel channel = UpdateChannel.Logic,
                  GameObject owner = null, CancellationToken ct = default);
    UniTask DelayFrames(int frames, UpdateChannel channel = UpdateChannel.Default,
                        GameObject owner = null, CancellationToken ct = default);
    UniTask DelayUnscaled(float seconds, GameObject owner = null,
                          CancellationToken ct = default);

    // ── 回调注册（One-shot）──
    IDisposable Schedule(float seconds, Action callback,
                         UpdateChannel channel = UpdateChannel.Logic,
                         GameObject owner = null);
    IDisposable ScheduleUnscaled(float seconds, Action callback,
                                  GameObject owner = null);

    // ── 回调注册（Repeat）──
    IDisposable ScheduleRepeat(float interval, Action callback, int count = -1,
                               UpdateChannel channel = UpdateChannel.Logic,
                               GameObject owner = null);
    IDisposable ScheduleRepeatUnscaled(float interval, Action callback, int count = -1,
                                       GameObject owner = null);

    // ── 回调注册（Frame-based）──
    IDisposable ScheduleEveryFrame(Action callback, int frameInterval = 1,
                                    UpdateChannel channel = UpdateChannel.Default,
                                    GameObject owner = null);

    // ── 分组 ──
    TimerGroup CreateGroup(string name);

    // ── 查询 ──
    int ActiveTimerCount { get; }

    // ── IModule ──
    void Init();
    void Dispose();
}
```

### 10.2 TimerGroup

```
sealed class TimerGroup : IDisposable
{
    IDisposable Schedule(float seconds, Action callback, UpdateChannel channel = UpdateChannel.Logic);
    IDisposable ScheduleRepeat(float interval, Action callback, int count = -1, UpdateChannel channel = UpdateChannel.Logic);
    IDisposable ScheduleEveryFrame(Action callback, int frameInterval = 1, UpdateChannel channel = UpdateChannel.Default);

    void CancelAll();
    void PauseAll();
    void ResumeAll();
    int Count { get; }
    void Dispose();
}
```

### 10.3 ITimerHandle（可选，单定时器控制）

```
interface ITimerHandle : IDisposable
{
    bool IsPaused { get; }
    void Pause();
    void Resume();
    void Cancel();  // 等同 Dispose
}
```

> `Schedule` 等返回 `IDisposable`，若需 Pause/Resume，可转换为 `ITimerHandle`（或直接返回 `ITimerHandle : IDisposable`）。

---

## 11. 使用示例（伪代码）

### 11.1 延迟执行（Awaitable）

```
// 等待 2 秒（暂停时不计时）
await GameGlobal.Scheduler.Delay(2f);
Debug.Log("2 秒后执行");

// 等待 5 帧
await GameGlobal.Scheduler.DelayFrames(5);
Debug.Log("5 帧后执行");

// 不受暂停影响的等待
await GameGlobal.Scheduler.DelayUnscaled(3f);
```

### 11.2 周期执行（回调）

```
// 每 2 秒恢复 1 点体力，无限
GameGlobal.Scheduler.ScheduleRepeat(2f, () => RestoreStamina(1), count: -1)
    .AddTo(this);  // 绑定 MonoBehaviour

// 每 5 秒生成敌人，共 10 次
GameGlobal.Scheduler.ScheduleRepeat(5f, () => SpawnEnemy(), count: 10, owner: gameObject);

// 每帧检查
GameGlobal.Scheduler.ScheduleEveryFrame(() => CheckPlayerGround(), frameInterval: 1)
    .AddTo(this);
```

### 11.3 定时器分组

```
// 进入战斗
var battleTimers = GameGlobal.Scheduler.CreateGroup("Battle");
battleTimers.ScheduleRepeat(5f, () => SpawnEnemy());
battleTimers.ScheduleRepeat(2f, () => RestoreStamina(1));

// 暂停所有战斗定时器（过场动画）
battleTimers.PauseAll();

// 恢复
battleTimers.ResumeAll();

// 退出战斗：一键取消
battleTimers.Dispose();
```

### 11.4 GameObject 绑定

```
// 技能冷却（玩家销毁时自动取消）
GameGlobal.Scheduler.Schedule(5f, () => SkillReady(), owner: playerGameObject);

// 等同于
var ct = playerGameObject.destroyCancellationToken;
await GameGlobal.Scheduler.Delay(5f, ct: ct);
```

### 11.5 子弹时间下的定时器

```
// 敌人减速（Logic 通道 TimeScale = 0.3）
GameGlobal.Lifecycle.SetChannelTimeScale(UpdateChannel.Logic, 0.3f);

// 所有 Logic 通道定时器减速 3 倍
// 这个定时器实际间隔变为 10 秒（原 3 秒 / 0.3）
GameGlobal.Scheduler.ScheduleRepeat(3f, () => EnemyAttack(), channel: UpdateChannel.Logic);
```

---

## 12. 实现检查清单

- [ ] `Scheduler : IModule`，纳入 GameGlobal
- [ ] 订阅 Lifecycle 的 Update 通道（Logic/Default）
- [ ] 定时器按通道分桶（`Dictionary<UpdateChannel, List<Timer>>`）
- [ ] One-shot / Repeat / Frame-based 三种类型实现
- [ ] Awaitable API（`Delay`/`DelayFrames`/`DelayUnscaled` 返回 UniTask）
- [ ] 回调注册 API（`Schedule`/`ScheduleRepeat`/`ScheduleEveryFrame` 返回 IDisposable）
- [ ] Unscaled 定时器用 `Time.unscaledDeltaTime`
- [ ] Frame-based 用帧计数器（非时间累加）
- [ ] `TimerGroup` 批量 CancelAll/PauseAll/ResumeAll
- [ ] `GameObject owner` 参数用 `destroyCancellationToken` 绑定
- [ ] 定时器对象池化（`TimerPool`，遵循"池化一切"）
- [ ] 暂停时通道不派发 → 定时器自然冻结（无需额外逻辑）
- [ ] `ActiveTimerCount` 查询
- [ ] `Dispose` 清理所有定时器与分组
- [ ] 所有公开 API 有中文 XML 注释
- [ ] 无硬编码（通道默认值等走配置）
- [ ] 错误处理：`Schedule(0秒)` / `ScheduleRepeat(负间隔)` → Warning + 钳制

---

## 13. 依赖关系

| 依赖项 | 方向 | 说明 |
|--------|------|------|
| Lifecycle | 引用 | 订阅 Update 通道，感知暂停/缩放 |
| UniTask | 引用 | Awaitable API |
| R3 | 引用 | IDisposable/AddTo |
| GameGlobal | 被引用 | 暴露 Scheduler |

> **初始化顺序**：... → Lifecycle → ... → **Scheduler**（在 Lifecycle 之后）
> Scheduler 依赖 Lifecycle 的 Update 通道，必须在 Lifecycle 初始化后注册。

---

## 14. 后续模块依赖本模块的接口

| 后续模块 | 使用的 Scheduler 接口 |
|----------|----------------------|
| UI Framework (2-1) | UI 动画延迟、弹窗队列定时 |
| Audio Manager (2-2) | BGM 淡入淡出、音效延迟 |
| FSM/HSM (3-1) | 状态超时、状态持续时间 |
| 业务层 | 技能冷却、体力恢复、敌人刷新、轮询 |

---

## 15. 关键使用规范（业务层 Coding AI 必读）

### 15.1 游戏逻辑定时器必须走 Scheduler（不用 UniTask.Delay）

- ❌ 禁止：`await UniTask.Delay(5f)`（不感知暂停）
- ✅ 正确：`await GameGlobal.Scheduler.Delay(5f, channel: UpdateChannel.Logic)`

### 15.2 定时器必须绑定生命周期

- ❌ 禁止：`Schedule(5f, callback)` 不绑定 owner（泄漏）
- ✅ 正确：`Schedule(5f, callback, owner: gameObject).AddTo(this)`

### 15.3 场景/玩法期定时器用 TimerGroup

- 进入战斗创建 `TimerGroup`，退出时 `Dispose()`。
- 避免旧场景定时器残留触发。

### 15.4 选择正确的通道

- 游戏逻辑（技能冷却、体力）→ `Logic`
- UI 动画 → `Default`
- 暂停时仍计时 → `Unscaled`

### 15.5 帧驱动用 ScheduleEveryFrame（不用 Update）

- ❌ 禁止：MonoBehaviour 里写 `Update()` 做轮询
- ✅ 正确：`ScheduleEveryFrame(() => Check(), frameInterval: 3)`

---

**文档结束。实现阶段请严格遵循本契约。**
