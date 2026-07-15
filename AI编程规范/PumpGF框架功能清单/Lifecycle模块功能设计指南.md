# Lifecycle 模块功能设计指南

> **文档定位**：本文件是 Lifecycle 模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、API 契约与数据结构，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的公开 API 语义**。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 |
|--------|------|
| A. UpdateChannel 定义方式 | 代码定义 `[Flags]` 枚举，类型安全，游戏可扩展 |
| B. 框架基础通道集合 | 8 个：`Default` `Logic` `Animation` `UI` `Effect` `Input` `FixedUpdate` `LateUpdate` |
| C. 帧率分离 | 逻辑通道固定步长（默认 60Hz，可配）；动画/表现通道每帧变量步长；提供插值 Alpha |
| D. 暂停与时间缩放 | 每通道独立 `IsPaused`（布尔）+ `TimeScale`（浮点），二者解耦 |
| E. PauseProfile 加载 | 走 Addressables 懒加载，首次 `PushPause` 时才通过 ResMgr 加载 |
| LifecycleMgr 形式 | 纯 C# 类 + 隐藏驱动 MonoBehaviour（与 PoolMgr/ResMgr 风格一致） |
| 更新派发双轨 | R3 Observable（默认推荐）+ `RegisterTick`（零分配热路径） |
| 场景钩子 | R3 事件 + `ISceneLifecycle` 有序接口；Level/Scene Manager 在其上构建 |
| CTS 工厂 | 绑定 GameObject / 超时 / 链接，内部池化 CTS |
| 初始化顺序 | Lifecycle 最先初始化（PauseProfile 懒加载，不阻塞 ResMgr） |
| 编辑器模式 | 仅 Play Mode，不考虑 Edit Mode |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**Lifecycle 是游戏运行节奏的中枢调度器**——它是所有模块的"时钟源"与"生命周期钩子源"，
但**绝不持有业务状态**，只提供调度与通知。

### 1.2 职责清单

| 职责 | 说明 |
|------|------|
| 更新循环中枢 | 统一驱动 Update/FixedUpdate/LateUpdate，按 UpdateChannel 分组派发 |
| 帧率分离 | 逻辑通道走固定步长累加器（默认 60Hz），表现通道走每帧变量步长 |
| 暂停系统 | 基于 PauseProfile 的命名暂停域，支持多 Profile 叠加与引用计数 |
| 时间缩放 | 每通道独立 TimeScale，与 IsPaused 解耦 |
| 场景生命周期钩子 | 统一暴露场景加载/卸载事件 + 有序的 ISceneLifecycle 接口 |
| 应用焦点/后台/退出 | 统一暴露 OnFocus/OnPause/OnQuit 事件 |
| CancellationToken 工厂 | 提供安全的 CTS 工厂，池化 CTS，自动 Dispose |

### 1.3 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ 场景加载流程编排 | 由 Phase 3 Level/Scene Manager 负责 |
| ❌ UI 暂停菜单实现 | 由 Phase 2 UI Framework 负责 |
| ❌ 音频暂停/恢复具体逻辑 | 由 Phase 2 Audio Manager 负责 |
| ❌ 业务帧调度逻辑（如 AI 思考频率节流） | 业务自行用 R3 操作符处理 |
| ❌ 存档触发时机 | 由 Phase 1 Save/Load System 订阅 Lifecycle 事件 |
| ❌ 编辑器模式工作 | 仅 Play Mode |

---

## 2. 整体架构

```
┌─────────────────────────────────────────────────────────┐
│                  LifecycleDriver (MonoBehaviour)         │
│        (隐藏驱动节点，挂在 DontDestroyOnLoad 根下)        │
│   Update / FixedUpdate / LateUpdate → 回调 LifecycleMgr  │
└──────────────────────────┬──────────────────────────────┘
                           │ Tick(rawDeltaTime)
                           ▼
┌─────────────────────────────────────────────────────────┐
│                     LifecycleMgr (IModule)               │
│                                                          │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────┐  │
│  │ ChannelHub   │  │ PauseSystem  │  │ TimeScale     │  │
│  │ (累加器+派发) │  │ (Profile 栈) │  │ (每通道独立)  │  │
│  └──────────────┘  └──────────────┘  └───────────────┘  │
│                                                          │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────┐  │
│  │ SceneHooks   │  │ AppLifecycle │  │ CtsFactory    │  │
│  │ (场景钩子)   │  │ (焦点/后台)  │  │ (池化CTS)     │  │
│  └──────────────┘  └──────────────┘  └───────────────┘  │
└─────────────────────────────────────────────────────────┘
        │                              │
        ▼ R3 Observable / RegisterTick │ PushPause / CTS
┌─────────────────┐            ┌─────────────────┐
│  业务模块/脚本   │            │  PauseProfile   │
│  (UIMgr/AudioMgr│            │  (ScriptableObj)│
│   /业务脚本)    │            │  数据驱动暂停规则│
└─────────────────┘            └─────────────────┘
```

---

## 3. UpdateChannel 通道系统

### 3.1 通道枚举定义

使用 `[Flags]` 枚举，支持组合（如一个订阅者同时属于 `Logic | Animation`）。

**框架提供的 8 个基础通道：**

| 通道 | 步长类型 | 默认频率 | 用途 |
|------|----------|----------|------|
| `Default` | 变量步长 | 每帧 | 通用兜底，不属于任何特定分类的更新 |
| `Logic` | 固定步长 | 60Hz | 核心逻辑（伤害结算、状态机推进、判定） |
| `Animation` | 变量步长 | 每帧（最高帧率） | 动画表现层驱动 |
| `UI` | 变量步长 | 每帧 | UI 刷新、布局重建 |
| `Effect` | 变量步长 | 每帧 | 特效、粒子驱动 |
| `Input` | 变量步长 | 每帧 | 输入轮询 |
| `FixedUpdate` | Unity 原生 | `Time.fixedDeltaTime` | 物理 |
| `LateUpdate` | Unity 原生 | 每帧 | 后处理、相机跟随 |

> **扩展约定**：游戏层可直接向该枚举追加值（如 `PlayerLogic = 1<<8`）。
> 框架不限制扩展，但要求扩展值不与基础值冲突，且在 `LifecycleConfig` 中同步配置频率。

### 3.2 通道的运行时状态

每个通道维护以下运行时状态：

| 状态 | 类型 | 说明 |
|------|------|------|
| `IsPaused` | bool | 当前是否被暂停（由 PauseSystem 计算） |
| `TimeScale` | float | 该通道的时间缩放（独立于全局 Time.timeScale） |
| `Accumulator` | float | 固定步长累加器（仅固定步长通道使用） |
| `InterpolationAlpha` | float | 0~1 插值因子（固定步长通道的副产物，供表现层插值） |
| `DeltaTime` | float | 本帧该通道的有效增量时间 |

### 3.3 有效 DeltaTime 计算

```
有效 deltaTime = IsPaused ? 0 : rawDeltaTime * TimeScale
```

- `IsPaused = true` 时，固定步长通道累加器不增长，变量步长通道派发 `dt = 0`。
- `TimeScale = 0.3` 时，通道进入子弹时间（慢动作）但不冻结。
- 暂停（冻结）与慢动作（缩放）完全正交。

---

## 4. 帧率分离与固定步长累加器

### 4.1 为什么不用 Unity 的 FixedUpdate

1. `FixedUpdate` 与物理耦合，改 `Time.fixedDeltaTime` 会影响物理稳定性。
2. `FixedUpdate` 频率全局唯一，无法"逻辑 60Hz、AI 30Hz"分开。
3. `FixedUpdate` 在 `Time.timeScale = 0` 时停摆，与暂停语义冲突。

### 4.2 累加器算法（仅固定步长通道）

每帧 Update 执行：
1. `accumulator += effectiveDeltaTime`（`effectiveDeltaTime = rawDelta * TimeScale`，暂停时为 0）
2. `tickCount = 0`
3. `while (accumulator >= fixedDt && tickCount < MaxCatchUpPerFrame):`
   - 派发该通道的 `OnTick(fixedDt)`
   - `accumulator -= fixedDt`
   - `tickCount++`
4. `interpolationAlpha = accumulator / fixedDt`（0~1，供表现层插值）

### 4.3 MaxCatchUpPerFrame 防御设计

- 一帧卡顿 200ms 时，累加器会试图 tick 12 次逻辑帧追上，导致"追帧雪崩"——越追越卡。
- `MaxCatchUpPerFrame`（默认 5）限制单帧最多追帧次数，超出部分直接丢弃累加器余量。
- 这是防止死亡螺旋的关键防御。

### 4.4 插值 Alpha 的用途

逻辑 60Hz 但画面 144Hz 时，帧间渲染会"卡顿"。`InterpolationAlpha`（0~1）表示
"当前帧在两个逻辑帧之间的进度"，表现层用它做线性插值，画面丝滑。
对动作游戏至关重要。

### 4.5 变量步长通道

直接派发 `effectiveDeltaTime`，无累加器，无 Alpha。适用于动画、UI、特效等需要最高帧率的表现层。

---

## 5. 暂停系统（PauseProfile + 引用计数）

### 5.1 设计目标

支持以下场景：
- **时停技能**：玩家正常行动，敌人/AI/特效冻结（连攻击动作卡在半空）。
- **菜单暂停**：游戏逻辑停，但 UI 菜单动画继续播。
- **过场动画**：所有游戏逻辑停，过场表现层继续。
- **多暂停叠加**：时停期间打开菜单，关菜单后时停仍在。

### 5.2 PauseProfile 数据结构（ScriptableObject）

PauseProfile 是纯数据，定义"某个暂停上下文激活时，冻结哪些通道"。

```
PauseProfile:
  Name: string                  // 唯一标识，如 "TimeStop"、"MenuPause"
  PausedChannels: UpdateChannel // 标志位组合，要冻结的通道
  Description: string           // 设计师可读说明（可选）
```

**示例 Profile：**

| Profile Name | PausedChannels | 说明 |
|--------------|----------------|------|
| `TimeStop` | `Logic` | 仅冻结核心逻辑，表现层照常（时停视觉由表现层自己处理） |
| `MenuPause` | `Logic \| Effect \| Input` | 冻结逻辑/特效/输入，UI 照常 |
| `Cutscene` | `Logic \| Effect \| Input \| UI` | 全冻结（过场由独立通道驱动） |

> **注意**：是否冻结 `Animation` 取决于业务。时停若要敌人攻击动作卡在半空，
> 则 `TimeStop` 应包含 `Animation` 中敌人相关部分——但框架基础通道不含角色维度，
> 业务层需自行扩展枚举（如 `EnemyAnimation`）并在 Profile 中声明。

### 5.3 PauseProfile 加载（懒加载）

- Profile 通过 Addressables 加载（遵循框架"禁用 Resources"原则）。
- **懒加载策略**：首次 `PushPause(profileName)` 时才通过 `ResMgr.LoadAssetAsync<PauseProfile>` 加载。
- 加载后缓存到内存字典，后续 Push 同名 Profile 直接命中缓存。
- **不阻塞 Lifecycle 初始化**：LifecycleMgr.Init() 不加载任何 Profile。

### 5.4 暂停的叠加语义（引用计数）

- `PushPause(name)`：将某 Profile 压入暂停栈，其 PausedChannels 标记为"激活"。
- `PopPause(name)`：将某 Profile 弹出栈，引用计数减一。
- **通道冻结条件**：任一激活的 Profile 包含该通道（并集）。
- **通道恢复条件**：所有包含该通道的 Profile 均已 Pop。
- 这保证了"时停 + 菜单暂停"同时存在时，只有两者都 Pop，对应通道才恢复。

### 5.5 运行时暂停栈

```
PauseStack: Stack<{ProfileName, Profile}>
ChannelFrozenMask: UpdateChannel  // 当前所有激活 Profile 的并集
```

每次 Push/Pop 后重新计算 `ChannelFrozenMask`，并更新各通道 `IsPaused` 状态。
通道 `IsPaused` 变化时通过 R3 Subject 通知订阅者（如业务需要"暂停瞬间播放音效"）。

---

## 6. 时间缩放系统

### 6.1 每通道独立 TimeScale

- 每个 `UpdateChannel` 有独立的 `TimeScale`（`ReactiveProperty<float>`，默认 1.0）。
- `SetChannelTimeScale(channel, scale)` 修改指定通道缩放。
- PauseProfile **只控制 IsPaused，不碰 TimeScale**。
- 子弹时间（`EnemyLogic.timeScale = 0.3`）与时停（`EnemyLogic.isPaused = true`）可共存且语义清晰。

### 6.2 与 Unity Time.timeScale 的关系

- 框架**不直接修改** `Time.timeScale`（避免影响物理、FixedUpdate、WaitForSeconds）。
- 若业务需要全局慢动作影响物理，业务自行调用 `Time.timeScale`，Lifecycle 不接管。
- Lifecycle 的时间缩放是"逻辑层"概念，只影响 Lifecycle 派发的 deltaTime。

### 6.3 提供的时间查询 API

| API | 说明 |
|-----|------|
| `GetDeltaTime(channel)` | 指定通道本帧有效增量时间 |
| `GetFixedDeltaTime(channel)` | 指定固定步长通道的步长 |
| `GetInterpolationAlpha(channel)` | 指定通道的插值因子（0~1） |
| `GetTimeScale(channel)` | 指定通道的 TimeScale |
| `GetUnscaledDeltaTime()` | 原始 Time.unscaledDeltaTime（不受任何影响） |

---

## 7. 更新派发双轨

### 7.1 R3 Observable（默认推荐）

业务层订阅 R3 Observable，享受 R3 操作符能力（Where/Throttle/Sample/CombineLatest）。

**暴露的 Observable：**
- `IObservable<float> OnUpdate(UpdateChannel)` —— 按 channel 过滤的更新流
- `IObservable<float> OnFixedUpdate` —— Unity 原生 FixedUpdate
- `IObservable<float> OnLateUpdate` —— Unity 原生 LateUpdate

> `OnUpdate` 按通道分别派发，订阅时指定 channel，中枢内部按 channel 路由。
> 同一 channel 的多个订阅者按注册顺序调用（不保证跨 channel 顺序，跨 channel 顺序由通道本身语义保证）。

### 7.2 RegisterTick（零分配热路径）

为极致性能场景（如弹幕、粒子、海量同帧更新对象）提供零分配的直接回调注册。

```
IDisposable RegisterTick(UpdateChannel channel, Action<float> callback)
```

- 回调签名 `Action<float>`（float = deltaTime），无装箱。
- 返回 `IDisposable`，Dispose 即取消注册。
- 内部用 `List<Action<float>>` 按 channel 分桶，注册时入桶，不每帧分配。
- 与 R3 Observable 并存，业务按需选择：默认用 R3，热路径用 RegisterTick。

> **规范**：R3 Observable 与 RegisterTick 派发同一份数据，不重复计算 deltaTime。

### 7.3 优先级

- 同一 channel 内，RegisterTick 回调优先于 R3 Observable 派发（保证热路径最先生效）。
- 框架**不提供**跨 channel 的执行顺序保证——通道本身的语义（Logic 先于 Animation）即隐含顺序。
- 若业务需要"在所有 Logic 之后再执行某逻辑"，应订阅 `LateUpdate` 通道或自行用 R3 操作符排序。

---

## 8. 场景生命周期钩子

### 8.1 R3 事件（原始钩子）

封装 `SceneManager` 的事件，提供 R3 流：

| 事件 | 触发时机 |
|------|----------|
| `OnSceneLoading` | 场景开始加载 |
| `OnSceneLoaded` | 场景加载完成 |
| `OnSceneUnloading` | 场景开始卸载 |
| `OnSceneUnloaded` | 场景卸载完成 |

> **职责边界**：Lifecycle 只暴露"场景发生了切换"的原始通知，
> 不负责"加载哪个场景、怎么过渡"——那是 Phase 3 Level/Scene Manager 的事。

### 8.2 ISceneLifecycle 有序接口

模块实现 `ISceneLifecycle` 接口后注册到 Lifecycle，中枢保证**按注册顺序**调用。

```
interface ISceneLifecycle
{
    void OnSceneEnter(Scene scene);   // 场景激活后调用
    void OnSceneExit(Scene scene);     // 场景卸载前调用
}
```

- 注册：`LifecycleMgr.RegisterSceneLifecycle(ISceneLifecycle, int priority = 0)`
- 按 priority 排序（priority 相同则按注册顺序），保证如 AudioMgr 在 UIMgr 之前响应。
- Level/Scene Manager 在 Phase 3 实现时，可作为 ISceneLifecycle 的消费者构建高级流程。

### 8.3 加载模式

- `LoadSceneMode.Single`（替换当前）：触发 OnSceneExit（旧场景）→ OnSceneLoaded（新场景）→ OnSceneEnter（新场景）。
- `LoadSceneMode.Additive`（叠加）：仅触发 OnSceneLoaded + OnSceneEnter（新场景），不影响旧场景。
- 卸载：OnSceneUnloading → OnSceneExit → OnSceneUnloaded。

---

## 9. 应用焦点 / 后台 / 退出

统一封装 `OnApplicationFocus` / `OnApplicationPause` / `OnApplicationQuit`，暴露 R3 事件：

| 事件 | 触发时机 | 典型用途 |
|------|----------|----------|
| `OnApplicationFocusChanged` | 窗口获得/失去焦点 | 失焦时自动暂停（可选） |
| `OnApplicationPauseChanged` | 应用进入/退出后台 | 后台时停止音频播放、降低帧率 |
| `OnApplicationQuit` | 应用退出 | 触发最终存档、释放资源 |

- 这些事件由 `LifecycleDriver` MonoBehaviour 的 `OnApplicationFocus/Pause/Quit` 转发。
- Lifecycle **不实现**具体的后台省电逻辑，只通知。

---

## 10. CancellationToken 工厂

### 10.1 设计目标

解决 `new CancellationTokenSource()` 忘记 Dispose 的高频泄漏问题，提供托管的 CTS 创建。

### 10.2 API

| API | 说明 |
|-----|------|
| `CreateLinkedToken(GameObject owner)` | 绑定 GameObject 生命周期，GameObject 销毁时自动 Cancel + Dispose |
| `CreateTimeoutToken(TimeSpan timeout)` | 超时自动 Cancel + Dispose |
| `CreateLinkedToken(params CancellationToken[] tokens)` | 链接多个 token，任一取消则结果取消，自动 Dispose 内部 CTS |
| `CreateAppLifetimeToken()` | 绑定应用生命周期，退出时 Cancel |

### 10.3 池化策略

- 内部用 `ObjectPool<CancellationTokenSource>` 池化 CTS（遵循"池化一切"规范）。
- 租用 CTS 时重置状态，归还时确保已 Cancel。
- 池容量默认 32，可通过 `LifecycleConfig` 配置。

### 10.4 与 destroyCancellationToken 的关系

- Unity 2023+ 的 `MonoBehaviour.destroyCancellationToken` 是官方提供的 GameObject 生命周期 token。
- 框架的 `CreateLinkedToken(GameObject)` 是其补充：
  - 适用于非 MonoBehaviour 的纯 C# 类（无法访问 destroyCancellationToken）。
  - 适用于需要"多个 token 链接"的场景。

---

## 11. LifecycleMgr 的存在形式与初始化时序

### 11.1 存在形式

- `LifecycleMgr : IModule`，纯 C# 逻辑类（与 PoolMgr/ResMgr 风格一致）。
- 内部持有一个隐藏的 `LifecycleDriver : MonoBehaviour`：
  - 挂在与 PoolMgr 同一个 `DontDestroyOnLoad` 根节点下。
  - 名字带 `[PumpGF]` 前缀，`hideFlags = HideFlags.NotEditable`。
  - 只做一件事：在 `Update`/`FixedUpdate`/`LateUpdate` 里回调 `LifecycleMgr.Tick()`。
  - 同时转发 `OnApplicationFocus`/`OnApplicationPause`/`OnApplicationQuit`。
- 逻辑可单元测试（mock driver）。

### 11.2 初始化时序

`GameGlobal.EnsureInitialized` 中模块初始化顺序：

1. **LifecycleMgr**（最先）—— 提供更新中枢、CTS 工厂给后续模块用
2. PoolMgr
3. ResMgr
4. ...其他模块

> **注意**：LifecycleMgr 初始化时**不加载任何 PauseProfile**（懒加载），
> 因此不依赖 ResMgr，可安全排在最前。
> ResMgr 异步初始化若需要 token，已可使用 LifecycleMgr 的 CTS 工厂。

---

## 12. SO 配置格式

### 12.1 LifecycleConfig（ScriptableObject）

定义各固定步长通道的频率、TimeScale 默认值、防追帧上限等。

```
LifecycleConfig : ScriptableObject
{
    // 固定步长通道配置（key = 通道枚举值，value = 配置）
    List<ChannelConfig> FixedStepChannels;

    // 单帧最大追帧次数（防死亡螺旋）
    int MaxCatchUpPerFrame = 5;

    // CTS 池容量
    int CtsPoolCapacity = 32;

    // PauseProfile 的 Addressables key 前缀（如 "PauseProfiles/"）
    string PauseProfileAddressPrefix = "PauseProfiles/";
}

ChannelConfig
{
    UpdateChannel Channel;   // 通道
    int TickRate;            // 频率（Hz），如 60
    float DefaultTimeScale; // 默认时间缩放，如 1.0
}
```

### 12.2 PauseProfile（ScriptableObject）

见 §5.2。

### 12.3 配置加载方式

- `LifecycleConfig`：通过 Addressables 加载，key 固定为 `"LifecycleConfig"`。
  - 启动时同步加载（配置量小，可容忍首帧开销）。
  - 若加载失败，使用代码默认值并 Warning。
- `PauseProfile`：懒加载，见 §5.3。

---

## 13. API 契约（公开接口）

> 以下为**公开 API 契约**，实现时方法签名必须一致（参数名可微调，但语义不可变）。
> 内部私有实现自由组织。

### 13.1 更新派发

```
// R3 Observable（默认推荐）
IObservable<float> GetUpdateObservable(UpdateChannel channel);
IObservable<float> FixedUpdateObservable { get; }
IObservable<float> LateUpdateObservable { get; }

// 零分配热路径
IDisposable RegisterTick(UpdateChannel channel, Action<float> callback);
```

### 13.2 暂停

```
// 暂停栈操作（异步，因首次需懒加载 Profile）
UniTask PushPause(string profileName, CancellationToken ct = default);
void PopPause(string profileName);
bool IsPauseActive(string profileName);

// 通道暂停状态查询
bool IsChannelPaused(UpdateChannel channel);
IObservable<bool> ObserveChannelPaused(UpdateChannel channel); // 暂停状态变化流
```

### 13.3 时间缩放

```
void SetChannelTimeScale(UpdateChannel channel, float scale);
float GetTimeScale(UpdateChannel channel);
ReactiveProperty<float> GetTimeScaleReactive(UpdateChannel channel);
```

### 13.4 时间查询

```
float GetDeltaTime(UpdateChannel channel);
float GetFixedDeltaTime(UpdateChannel channel);
float GetInterpolationAlpha(UpdateChannel channel);
float GetUnscaledDeltaTime();
```

### 13.5 场景钩子

```
IObservable<Scene> OnSceneLoading { get; }
IObservable<Scene> OnSceneLoaded { get; }
IObservable<Scene> OnSceneUnloading { get; }
IObservable<Scene> OnSceneUnloaded { get; }

void RegisterSceneLifecycle(ISceneLifecycle listener, int priority = 0);
void UnregisterSceneLifecycle(ISceneLifecycle listener);
```

### 13.6 应用生命周期

```
IObservable<bool> OnApplicationFocusChanged { get; }
IObservable<bool> OnApplicationPauseChanged { get; }
IObservable<Unit> OnApplicationQuit { get; }
```

### 13.7 CTS 工厂

```
CancellationToken CreateLinkedToken(GameObject owner);
CancellationToken CreateTimeoutToken(TimeSpan timeout);
CancellationToken CreateLinkedToken(params CancellationToken[] tokens);
CancellationToken CreateAppLifetimeToken();
```

### 13.8 IModule 实现

```
void Init();
void Dispose();
```

---

## 14. 使用示例（伪代码，非最终实现）

### 14.1 业务层订阅更新

```
// 玩家逻辑（固定 60Hz）
GameGlobal.Lifecycle.GetUpdateObservable(UpdateChannel.Logic)
    .Subscribe(dt => TickPlayerLogic(dt))
    .AddTo(this);

// 敌人 AI（固定 60Hz）
GameGlobal.Lifecycle.RegisterTick(UpdateChannel.Logic, dt => TickEnemyAI(dt));

// UI 刷新（每帧变量）
GameGlobal.Lifecycle.GetUpdateObservable(UpdateChannel.UI)
    .Subscribe(dt => RefreshUI(dt))
    .AddTo(this);
```

### 14.2 触发时停

```
// 触发时停（异步，首次需加载 Profile）
await GameGlobal.Lifecycle.PushPause("TimeStop", ct);
// → 读取 PauseProfile "TimeStop" → 冻结 Logic 通道
// → 玩家逻辑也属于 Logic，如何区分？见 14.3

// 结束时停
GameGlobal.Lifecycle.PopPause("TimeStop");
```

### 14.3 角色维度暂停（业务扩展）

框架基础通道不含角色维度。若要"时停冻结敌人但不冻结玩家"，业务层扩展枚举：

```
// 业务层扩展（游戏代码，非框架代码）
[Flags]
public enum GameUpdateChannel  // 继承/包装框架 UpdateChannel
{
    PlayerLogic = 1 << 8,
    EnemyLogic  = 1 << 9,
}
```

> **框架边界说明**：框架只提供 8 个基础通道与暂停机制。
> 角色维度的拆分（PlayerLogic/EnemyLogic）由业务层自行扩展枚举并配置 Profile。
> 这是"组合优于继承"的体现——框架提供机制，业务提供策略。

### 14.4 子弹时间

```
// 敌人进入慢动作（不冻结，仅减速）
GameGlobal.Lifecycle.SetChannelTimeScale(UpdateChannel.Logic, 0.3f);
// → 逻辑通道 deltaTime 变为 0.3 倍，动画若也订阅同通道则同步减速
```

### 14.5 安全的异步取消

```
// 绑定 GameObject 生命周期
var ct = GameGlobal.Lifecycle.CreateLinkedToken(gameObject);
var asset = await GameGlobal.ResMgr.LoadAssetAsync<GameObject>("Enemy", ct);

// 超时取消
var timeoutCt = GameGlobal.Lifecycle.CreateTimeoutToken(TimeSpan.FromSeconds(5));
```

---

## 15. 实现检查清单

实现完成后，逐项自查：

- [ ] `LifecycleDriver` MonoBehaviour 存在且挂在 DontDestroyOnLoad 根节点，`hideFlags` 设置正确
- [ ] 8 个基础通道枚举值定义且不冲突，`[Flags]` 特性已加
- [ ] 固定步长通道累加器正确实现，`MaxCatchUpPerFrame` 生效
- [ ] 插值 Alpha 计算正确（0~1 范围）
- [ ] 变量步长通道直接派发 deltaTime
- [ ] PauseProfile 为 ScriptableObject，字段符合 §5.2
- [ ] PauseProfile 懒加载通过 ResMgr，加载后缓存
- [ ] 暂停栈 Push/Pop 引用计数正确，ChannelFrozenMask 并集计算正确
- [ ] 每通道 IsPaused 与 TimeScale 解耦，互不影响
- [ ] R3 Observable 与 RegisterTick 派发同一份数据
- [ ] ISceneLifecycle 按 priority 排序调用
- [ ] 应用焦点/后台/退出事件转发正确
- [ ] CTS 工厂池化，CreateLinkedToken(GameObject) 在 GameObject 销毁时 Cancel + Dispose
- [ ] LifecycleMgr.Init() 不加载任何 Profile（懒加载）
- [ ] GameGlobal.EnsureInitialized 中 Lifecycle 排在 PoolMgr 之前
- [ ] 所有公开 API 有中文 XML 注释
- [ ] 无硬编码、无魔法数字（频率等走 LifecycleConfig）
- [ ] 错误处理：Profile 加载失败、key 不存在等场景有 Warning + 降级

---

## 16. 依赖关系

| 依赖项 | 方向 | 说明 |
|--------|------|------|
| R3 | 引用 | Observable/ReactiveProperty/Subject |
| UniTask | 引用 | PushPause 异步、CTS 超时 |
| Addressables | 引用（懒加载时） | 加载 PauseProfile / LifecycleConfig |
| ResMgr | 引用（懒加载时） | 通过 `GameGlobal.ResMgr` 加载 Profile |
| PoolMgr | 引用（CTS 池化） | CTS ObjectPool |
| GameGlobal | 被引用 | 作为服务定位器暴露 LifecycleMgr |

> **循环依赖风险**：LifecycleMgr 初始化时依赖 PoolMgr（CTS 池），
> 但 Lifecycle 排在 PoolMgr 之前。**解法**：CTS 工厂在首次调用时才从 PoolMgr 取池，
> 而非 Init 时。若 PoolMgr 尚未初始化，CTS 工厂退化为直接 new（不池化）并 Warning。

---

## 17. 后续模块依赖本模块的接口

| 后续模块 | 使用的 Lifecycle 接口 |
|----------|----------------------|
| Event System (1-2) | 可能用 CTS 工厂、Update 通道（事件清理） |
| UI Framework (2) | OnUpdate(UI)、OnApplicationFocusChanged |
| Audio Manager (2) | ISceneLifecycle、PushPause |
| Save/Load System (1) | OnApplicationQuit、场景钩子 |
| Level/Scene Manager (3) | 场景钩子、ISceneLifecycle |
| AI / 背包 / 技能 | OnUpdate(Logic)、时间缩放、暂停状态 |

---

**文档结束。实现阶段请严格遵循本契约。**
