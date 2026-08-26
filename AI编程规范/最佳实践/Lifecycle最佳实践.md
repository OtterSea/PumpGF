# Lifecycle 最佳实践

> **文档定位**：本文件给 Coding AI 提供 Lifecycle 模块在**真实业务场景下的标准正例**，以及高频误用的反例。与《Lifecycle模块功能设计指南》(契约)配合使用：设计指南回答"接口是什么"，本文回答"业务该怎么用"。
> **写作依据**：本文件由 2026-08 项目实战反哺，融合《踩坑记录》《代码质量审查报告》中的真实误用案例。
> **优先读**：AI 接到 Lifecycle 相关需求时，先读本文再写代码。

---

## 0. 一句话原则

**所有"每帧/定时/暂停感知"的需求，都走 Lifecycle 的通道与暂停系统，禁止直接用 `MonoBehaviour.Update` 或 `Time.timeScale`。**

---

## 1. 标准正例集

### 1.1 每帧驱动逻辑 → 按通道订阅，不写 MonoBehaviour.Update

**场景**：一个纯 C# 系统（非关 MonoBehaviour）需要每帧推进逻辑，且想要暂停感知。

```csharp
// ✅ 正确：订阅 Logic 固定步长通道（60Hz），暂停自动冻结
GameGlobal.Lifecycle.GetUpdateObservable(UpdateChannel.Logic)
    .Subscribe(dt => TickLogic(dt))
    .AddTo(this);

// ✅ 或零分配热路径（海量实体/弹幕）
GameGlobal.Lifecycle.RegisterTick(UpdateChannel.Logic, dt => TickLogic(dt));
```

```csharp
// ❌ 反例 1：自己写 MonoBehaviour.Update —— 无法暂停感知，违反"Update 走 Lifecycle"铁律
void Update() { TickLogic(Time.deltaTime); }
```

> **为什么**：`GetUpdateObservable(Logic)` 订阅的是固定步长通道，`PushPause` 时自动冻结；`MonoBehaviour.Update` 永远每帧跑，无法感知暂停，要自己加 `if (paused) return`，散落各处。

### 1.2 细分帧率 → 逻辑用固定步长，表现用变量步长

**场景**：伤害结算要稳定 60Hz（不受帧率波动影响），UI 动画要最高帧率丝滑。

```csharp
// ✅ 逻辑走固定步长（60Hz，累加器保证稳定）
GameGlobal.Lifecycle.GetUpdateObservable(UpdateChannel.Logic).Subscribe(dt => Simulate());
// ✅ 表现走变量步长（每帧，最高帧率）
GameGlobal.Lifecycle.GetUpdateObservable(UpdateChannel.Animation).Subscribe(dt => Animate());
```

```csharp
// ❌ 反例：把不稳定的 Time.deltaTime 直接用于逻辑结算 → 帧率波动导致手感不一致
Simulate(); // 在 MonoBehaviour.Update 里
```

### 1.3 暂停/时停 → 用 PushPause/PopPause，不碰 Time.timeScale

**场景**：打开菜单时冻结游戏逻辑，但菜单 UI 动画继续播。

```csharp
// ✅ 正确：PushPause 按 Profile 冻结 Logic/Effect/Input，UI 通道照常
await GameGlobal.Lifecycle.PushPause("MenuPause");   // 异步（首帧懒加载 Profile）
// ... 关闭菜单
GameGlobal.Lifecycle.PopPause("MenuPause");
```

```csharp
// ❌ 反例：直接改 Time.timeScale = 0 —— 会把物理/FixedUpdate/WaitForSeconds 也停掉，
// 且动画/UI 也跟着停，无法"只停逻辑不停UI"
Time.timeScale = 0;
```

> **为什么框架不直接改 `Time.timeScale`**：`Time.timeScale` 全局唯一，会影响 Unity 物理、`FixedUpdate`、`WaitForSeconds` 等非 Lifecycle 管理的部分，语义过重。Lifecycle 的暂停是"逻辑层概念"，只影响派发的 deltaTime。

### 1.4 子弹时间（慢动作）→ SetChannelTimeScale，冻结与减速解耦

**场景**：敌人进入慢动作，但不冻结。

```csharp
// ✅ 正确：Logic 通道 0.3 倍速（子弹时间），不冻结
GameGlobal.Lifecycle.SetChannelTimeScale(UpdateChannel.Logic, 0.3f);
// 恢复正常
GameGlobal.Lifecycle.SetChannelTimeScale(UpdateChannel.Logic, 1.0f);
```

> **注意**：`PushPause` 冻结（IsPaused），`SetChannelTimeScale` 减速（TimeScale），二者正交、可共存。例如"时停技能"= `PushPause("EnemyLogic")`（冻结敌人）+ `SetChannelTimeScale(PlayerLogic, 0.5f)`（玩家慢动作），语义清晰。

### 1.5 异步取消 → 用 CTS 工厂，不手动管理 CancellationTokenSource

**场景**：异步加载资源，GameObject 销毁时自动取消。

```csharp
// ✅ 正确：绑定 GameObject 生命周期，销毁自动 Cancel + Dispose
var ct = GameGlobal.Lifecycle.CreateLinkedToken(gameObject);
var asset = await GameGlobal.ResMgr.LoadAssetAsync<GameObject>("Enemy", ct);
```

```csharp
// ❌ 反例：手动 new CTS 却忘记 Dispose → 高频泄漏
var cts = new CancellationTokenSource();
try { await LoadAsync(cts.Token); }
finally { cts.Dispose(); }   // 容易忘，一漏就泄漏
```

> **为什么**：框架 CTS 工厂内部池化 CTS，且绑定 GameObject 销毁自动取消，从机制上消灭"手写 CTS 忘记 Dispose"这类高频内存泄漏。

### 1.6 暂停状态感知 → 订阅 ObserveChannelPaused

**场景**：某个系统需要在通道暂停瞬间做处理（如暂停时播放音效）。

```csharp
// ✅ 正确：订阅通道暂停状态变化流
GameGlobal.Lifecycle.ObserveChannelPaused(UpdateChannel.Logic)
    .Subscribe(paused => { if (paused) PlayPauseSound(); })
    .AddTo(this);
```

```csharp
// ❌ 反例：不用 R3 流，每帧轮询 GameGlobal.Lifecycle.IsChannelPaused(...)
if (GameGlobal.Lifecycle.IsChannelPaused(UpdateChannel.Logic)) { ... } // 在 Update 里每帧查
```

---

## 2. 高频误用反例汇总（AI 思维惯性）

| # | 误用模式 | 正确做法 | 踩坑来源 |
|---|---------|---------|---------|
| 1 | 用 `MonoBehaviour.Update` 驱动逻辑 | 订阅 `GetUpdateObservable(Logic)` 或 `RegisterTick` | 约束清单铁律-更新走Lifecycle |
| 2 | 直接改 `Time.timeScale = 0` 做暂停 | `PushPause(profileName)` 按通道冻结 | 审查 P0-时停/暂停 |
| 3 | 用 `WaitForSeconds` / 协程做延迟 | `UniTask.Delay` + 取消 token | 约束清单铁律-禁用协程 |
| 4 | 手写 `new CancellationTokenSource()` 不 Dispose | `Lifecycle.CreateLinkedToken(gameObject)` | 审查 P0-CTS 泄漏 |
| 5 | 一帧卡顿未防追帧 | 框架内置 `MaxCatchUpPerFrame`（业务无需管，但别用 MonoBehaviour 自写累加器替代） | 设计 §4.3 死亡螺旋 |
| 6 | 逻辑结算用 `Time.deltaTime`（变量步长） | 用 `GetDeltaTime(Logic)`（固定步长） | 设计 §4 帧率分离 |
| 7 | 想"只冻敌人不冻玩家"就改基础通道枚举 | 业务层扩展 `[Flags]` 枚举（如 `EnemyLogic = 1<<8`），框架不代劳 | 设计 §14.3 角色维度 |
| 8 | 把"冻结"与"减速"混为一谈 | `PushPause`（冻结）+ `SetChannelTimeScale`（减速）分开用 | 设计 §6.1 解耦 |

---

## 3. 边界与不做什么

- Lifecycle **不做**场景加载编排（那是 Level/Scene Manager）、UI 暂停菜单（UI Framework）、音频暂停逻辑（Audio Manager）。**别在 Lifecycle 里塞这些。**
- Lifecycle 只提供 8 个基础通道，**没有角色维度**（PlayerLogic/EnemyLogic 需业务扩展枚举）。别指望框架帮你拆"玩家/敌人"——那是业务层 `[Flags]` 扩展的事。
- Lifecycle 懒加载 PauseProfile（首次 `PushPause` 才通过 ResMgr 加载）。**别在 `Init` 时假设 Profile 已就绪**，正确姿势是 `await PushPause`。

---

## 4. 与设计契约的关系

- 接口/语义以《Lifecycle模块功能设计指南》为准，本文只补充"用法正例"，不改变契约。
- 若发现本文与设计契约冲突，**以设计契约为准**，并考虑更新本文。

---

*本文档随 PumpGF 框架分发。由 2026-08 项目实战反哺。*
