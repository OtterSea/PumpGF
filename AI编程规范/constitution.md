# PumpGF 框架宪法（Constitution）

> **本文件是 AI 编码的最高指导纲领**。Coding AI 在编码前**必须阅读本文件**，了解框架提供什么能力、什么场景用什么模块、什么必须做、什么禁止做。
> 本文件与 [框架说明.md](./框架说明.md) 的区别：框架说明是"框架是什么"，宪法是"AI 编码时怎么做"。

---

## 0. 环境前提（AI 生成代码前必读）

> **⚠️ 本框架仅支持 Unity 2022.2+ / 团结引擎 1.9.x（基于 2022 LTS）。**
> 在 Unity 2021 或更早版本生成代码会产生大规模兼容性错误。

生成代码时可安全使用的 API（Unity 2022.2+ 特性）：
- `Component.destroyCancellationToken` / `GameObject.destroyCancellationToken`
- `Dictionary<K,V>.Remove(key, out value)`（.NET Standard 2.1）
- C# 9 语法（target-typed new、record、init-only 等）

**类型体系约束（不要混淆）：**
- 事件流对外暴露 → 用 `R3.Observable<T>`（**不是** `System.IObservable<T>`）
- 响应式属性 → 用 `R3.ReactiveProperty<T>` / `R3.ReadOnlyReactiveProperty<T>`
- 异步 → 用 `UniTask` / `UniTaskVoid`（**不是** `Task` / `async void`）

**常见坑清单**：详见 [`README.md#已知坑合集`](../README.md#已知坑合集踩过的雷)。

---

## 1. 框架定位

PumpGF 是可迁移的 Unity 游戏开发代码框架，补全引擎缺失的工程化能力。AI 编码时应**优先使用框架模块**，不重复造轮子。

---

## 2. 架构原则

| 原则 | 说明 |
|------|------|
| **数据驱动** | 数据集中在 GameDataStore，UI/逻辑订阅数据变化 |
| **组合优于继承** | 用 Entity + Component 组合，不用深继承链 |
| **事件解耦** | 跨模块通信用 EventBus，不直接引用 |
| **配置驱动** | 参数走 SO 配置，不硬编码 |
| **SOLID + DRY** | 单一职责、开闭原则、不重复 |
| **池化一切** | 频繁创建销毁的对象走 PoolMgr 或内部池 |

---

## 3. 框架能力路由表（核心）

**遇到以下需求时，使用对应模块与 API，不要自行实现：**

### 3.1 核心基础设施（Phase 1）

| 需求场景 | 使用模块 | 关键 API | 文档 |
|----------|----------|----------|------|
| 加载/卸载资源 | ResMgr | `LoadAssetAsync` / `InstantiateAsync` / `Release` | ResMgr指南 |
| 跨模块事件通信 | EventBus | `Publish<T>` / `Subscribe<T>` / `GetDomain` | EventSystem指南 |
| 游戏数据管理 | GameDataStore | `ReactiveProperty` / `Execute(Command)` | GameDataStore指南 |
| 存档读写 | SaveMgr | `SaveAsync` / `LoadAsync` / `GetSlotList` | SaveLoad指南 |
| 延迟/定时/周期 | Scheduler | `Delay` / `ScheduleRepeat` / `CreateGroup` | Scheduler指南 |
| 更新循环/暂停/时间缩放 | Lifecycle | `GetUpdateObservable` / `PushPause` / `SetChannelTimeScale` | Lifecycle指南 |
| 跨对象就绪/等待初始化完成 | Readiness | `MarkReady<T>` / `await WaitUntilReady<T>(ct)` / `TryGet` | 时序竞态最佳实践 |
| 配置加载与访问 | ConfigMgr | `Get<T>` / `GameConfigs.XXX` | ConfigManager指南 |
| 对象池 | PoolMgr | `Get` / `Release` / `HasPool` | 对象池指南 |

### 3.2 通用系统层（Phase 2）

| 需求场景 | 使用模块 | 关键 API | 文档 |
|----------|----------|----------|------|
| UI 页面/弹窗/HUD | UIManager | `Push` / `Pop` / `ShowPopup` / `ShowHUD` | UIFramework指南 |
| 音频播放（BGM/SFX） | AudioMgr | `Play` / `Play3D` / `StopBgm` | AudioManager指南 |
| 玩家输入 | InputMgr | `OnActionPerformed` / `GetAxisValue` / `Push(Context)` | InputManager指南 |
| 多语言文本 | LocalizationMgr | `GetText` / `L.T()` / `GetReactiveString` | Localization指南 |
| 相机管理 | CameraMgr | `SetActiveCamera` / `Shake` / `SetFollow` | CameraManager指南 |

### 3.3 玩法支撑层（Phase 3）

| 需求场景 | 使用模块 | 关键 API | 文档 |
|----------|----------|----------|------|
| 状态机/层级状态 | StateMachineBuilder | `Create` / `State` / `TransitionTo` / `Build` | FSM_HSM指南 |
| 实体/组件管理 | EntityManager | `Create` / `Query<T>` / `Get<T>` | EntityComponent指南 |
| 关卡/场景加载 | LevelManager | `LoadLevelAsync` / `RegisterLevel`（旧单阶段） | LevelSceneManager指南 |
| 关卡/场景加载（新关卡，推荐） | LevelManager | `LoadLevelPhasedAsync` / `RegisterPhasedLevel`（`IPhasedLevel` 两阶段：Preload→Activate） | LevelSceneManager指南 / 时序竞态最佳实践 |
| 场景内跨对象装配 | SceneBootstrapper | `ISceneInitializable`（`Priority` + `InitializeAsync`） | 时序竞态最佳实践 |

### 3.4 工程化层（Phase 4）

| 需求场景 | 使用模块 | 关键 API | 文档 |
|----------|----------|----------|------|
| 日志 | Log | `Log.Debug` / `Log.Info` / `Log.Warning` / `Log.Error` | DebugConsole指南 |
| 调试命令 | DebugConsole | `[ConsoleCommand]` / `Execute` | DebugConsole指南 |
| Gizmos 可视化 | DebugConsole | `RegisterGizmos(IGizmosDrawable)` | DebugConsole指南 |

### 3.5 Vendor 库（直接使用，不封装）

| 需求场景 | 使用库 | 关键 API | 文档 |
|----------|--------|----------|------|
| 异步/await | UniTask | `UniTask.Delay` / `WhenAll` / `ToUniTask` | R3与UniTask指南 |
| 响应式/事件流/属性 | R3 | `ReactiveProperty` / `Subscribe` / `Observable` | R3与UniTask指南 |
| 缓动动画 | DOTween | `DOFade` / `DOMove` / `DOScale` | DOTween文档 |
| 相机系统 | Cinemachine | `VirtualCamera` / `ImpulseSource` | Cinemachine文档 |
| 3D 角色移动 | KCC | `KinematicCharacterMotor` / `ICharacterController` | KCC使用指南 |

---

## 4. 模块初始化顺序

`GameGlobal.EnsureInitialized` 按依赖顺序初始化（与 `Runtime/Core/GameGlobal.cs` 保持一致）：

```
  1. LifecycleMgr    （最先，提供 Update 通道/CTS 给后续模块）
  2. Readiness       （就绪注册表，无依赖，最早可用，供后续模块/业务注册与等待就绪）
  3. Scheduler       （依赖 Lifecycle 的 Update 通道）
  4. PoolMgr         （无依赖）
  5. ResMgr          （依赖 PoolMgr）
  6. EventBus        （无依赖）
  7. ConfigMgr       （依赖 ResMgr）
  8. GameDataStore   （依赖 EventBus）
  9. SaveMgr         （依赖 GameDataStore）
  10. UIManager      （依赖 ResMgr/GameDataStore）
  11. LocalizationMgr（依赖 ResMgr）
  12. AudioMgr       （依赖 ResMgr/Scheduler）
  13. InputMgr       （依赖 UIManager 联动 hooks）
  14. EntityManager  （无强依赖）
  15. LevelManager   （依赖 ResMgr/Lifecycle/UIManager）
  16. DebugConsole   （最后，依赖其他模块指标；Release 时为空壳）
```

> 未列入本表的模块尚未接入 `GameGlobal` init 链：StateMachineBuilder 为纯 C# 库（非 IModule），业务直接创建；CameraMgr 待后续批次接入。
> 新增模块时按依赖关系插入合适位置，并**同步更新本表与代码**（见第 8 节）。

---

## 5. AI 编码必须遵守的约束

### ✅ 必须做

| 约束 | 说明 |
|------|------|
| 异步用 UniTask | `UniTask.Delay` / `await`，不用协程（IEnumerator） |
| 跨模块通信用 EventBus | `Publish<T>` / `Subscribe<T>`，不直接引用其他模块 |
| 数据放 GameDataStore | 可持久化数据放数据域，不散落各处 |
| UI 走 UIManager | `Push` / `Pop`，不手动 `Instantiate` UI |
| 资源走 ResMgr | `LoadAssetAsync` / `InstantiateAsync`，不直接 Addressables |
| 输入用 New Input System | `InputMgr.OnActionPerformed`，不用 `Input.GetKey` |
| 日志用 Log 类 | `Log.Info("Tag", "msg")`，不用 `Debug.Log` |
| 配置用 ConfigMgr | `GameConfigs.XXX`，不硬编码 |
| 状态机用 StateMachineBuilder | 不在 Update 写 if-else 状态分支 |
| 订阅绑定生命周期 | `.AddTo(this)` / `.AddTo(ref Bag)`，不裸订阅 |
| 事件用 readonly struct | 零分配，传 Id 不传大对象 |
| 资源用完 Dispose | `AssetHandle.Dispose()` / `handle.AddTo(this)` |
| 跨对象就绪/初始化时序 | 用 `Readiness` / `SceneBootstrapper` / `IPhasedLevel` 三道防线（见时序竞态最佳实践） |

### ❌ 禁止做

| 禁止 | 替代方案 |
|------|----------|
| `Input.GetKey` / `Input.GetKeyDown` | `InputMgr.GetButtonValue` / `OnActionPerformed` |
| `Resources.Load` / `Resources.LoadAsync` | `ResMgr.LoadAssetAsync` |
| 直接 `Addressables.LoadAssetAsync` | `ResMgr.LoadAssetAsync`（引用计数） |
| `Debug.Log` / `Debug.LogWarning` | `Log.Debug` / `Log.Warning` |
| 协程 `StartCoroutine(IEnumerator)` | `UniTask` / `async UniTaskVoid` |
| `async void` | `async UniTaskVoid` + `.Forget()` |
| Update 里 if-else 状态分支 | `StateMachineBuilder` + `State` 类 |
| 硬编码字符串/魔法数字 | SO 配置 / 常量 / 枚举 |
| 跨模块直接引用 | `EventBus.Publish/Subscribe` |
| 业务自己 `Instantiate` UI | `UIManager.Push/ShowPopup` |
| 直接改 `Time.timeScale` 暂停 | `Lifecycle.PushPause` |
| `GameObject.Find` 查找实体 | `EntityManager.Query<T>` |
| Awake/Start 获取**运行时动态对象**引用 | `await Readiness.WaitUntilReady<T>` 或 `ISceneInitializable.InitializeAsync` |
| `WaitUntil(() => x != null)` 轮询等待就绪 | `await Readiness.WaitUntilReady<T>(ct)` |

---

## 6. 规范引用

| 规范 | 文档 |
|------|------|
| 开发编程规范 | [开发编程规范.md](./开发编程规范.md) |
| R3 与 UniTask 用法 | [R3与UniTask使用指南.md](./R3与UniTask使用指南.md) |
| 对象池用法 | [对象池使用指南.md](./对象池使用指南.md) |
| KCC 用法 | [KCC使用指南.md](./KCC使用指南.md) |
| 时序竞态与初始化（三道防线） | [时序竞态与初始化最佳实践.md](./时序竞态与初始化最佳实践.md) |
| 约束铁律速查 | [PumpGF框架约束清单.md](./PumpGF框架约束清单.md) |
| 各模块设计指南 | [PumpGF框架功能清单/](./PumpGF框架功能清单/) |

---

## 7. SpecKit 工作流（AI 编码流程参考）

AI 接到复杂需求时，可参考 [SpecKit 工作流](./speckit/speckit工作流.md) 先分析再编码：

1. **specify**：分析需求，明确要做什么
2. **plan**：设计技术方案
3. **tasks**：拆解为可执行任务
4. **implement**：按任务编码

小需求可直接编码（参考 `speckit.simple` 流程）。

> **业务层的 SpecKit 产出**存放在业务项目根目录的 `.specify/`，**不放入框架包**。

---

## 8. 文档同步纪律（元规则，AI 必读）

> **改了框架代码却不更新文档，等于没改**——下一个 Coding AI 会按过时文档生成错误代码。
> 本节是约束 Coding AI **自身行为**的元规则，优先级与第 5 节等同。

### 8.1 触发条件

凡是修改了 `Packages/PumpGF/` 下的框架源码（`Runtime/` 或 `Editor/`），提交前**必须**按下表逐条核对是否需要同步文档：

| 改动类型 | 必查并同步的文档 |
|----------|------------------|
| 新增/删除/重命名模块（`IModule` 实现） | 本宪章第 3 节路由表 + 第 4 节初始化顺序；`框架说明.md` 模块表 |
| 新增/修改 public API（方法/属性/签名） | 对应模块功能设计指南；本宪章第 3 节 API 列；`PumpGF框架约束清单.md` 速查表 |
| 新增框架级硬约束（禁止/必须模式） | `PumpGF框架约束清单.md`（铁律 + 速查表）；`开发编程规范.md` 必读指引 |
| 新增接口/抽象（如 `IPhasedLevel`、`ISceneInitializable`） | `框架说明.md` 运行时层；对应模块设计指南 API 契约 |
| 新增最佳实践/反模式专题 | 新增专题文档；`框架说明.md` 文档索引；`开发编程规范.md` 必读指引；本宪章第 6 节 |
| 改 `GameGlobal` 初始化/释放顺序 | 本宪章第 4 节；`框架说明.md` |

### 8.2 文档清单（必须与代码保持一致）

| 文档 | 职责 | 代码锚点 |
|------|------|----------|
| `constitution.md`（本文件） | AI 编码最高纲领：能力路由 + 约束 + 初始化顺序 | `Runtime/Core/GameGlobal.cs`、`Runtime/` 全模块 |
| `框架说明.md` | 框架是什么：能力总览 + 运行时层 + 文档索引 | `Runtime/` 目录结构 |
| `PumpGF框架约束清单.md` | 铁律 + 速查表 | 所有 public API 与禁止项 |
| `开发编程规范.md` | 编码流程规范入口 | — |
| `PumpGF框架功能清单/<模块>模块功能设计指南.md` | 单模块 API 契约与用法 | 对应模块 `.cs` |
| `时序竞态与初始化最佳实践.md` | 时序防线专题 | `Readiness/`、`Scene/`、`Level/IPhasedLevel.cs` |

### 8.3 判定原则

- **API 变了 → 文档必须变**：方法名/参数/返回类型变化，对应设计指南的"API 契约"必须同步。
- **行为变了 → 文档必须变**：即使签名没变，语义变化（如新增异步阶段、改初始化顺序）也要同步。
- **仅内部实现变了 → 文档不必变**：重构、性能优化、不影响对外契约的 bug 修复，不强制更新。
- **拿不准 → 更新**：宁可多写一行，也不要让下一个 AI 按错误文档生成代码。

---

**本文件是 AI 编码的最高约束。如有与各模块文档冲突，以本文件为准。**
