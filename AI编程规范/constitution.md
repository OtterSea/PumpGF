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
| 关卡/场景加载 | LevelManager | `LoadLevelAsync` / `RegisterLevel` | LevelSceneManager指南 |

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

```
GameGlobal.EnsureInitialized 顺序：
  1. LifecycleMgr    （最先，提供 Update/CTS）
  2. PoolMgr
  3. ResMgr
  4. ConfigMgr
  5. EventBus
  6. GameDataStore
  7. SaveMgr
  8. Scheduler
  9. UIManager
  10. AudioMgr
  11. InputMgr
  12. LocalizationMgr
  13. CameraMgr
  14. EntityManager
  15. LevelManager
  16. DebugConsole     （最后）
```

> 新增模块时按依赖关系插入合适位置。

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

---

## 6. 规范引用

| 规范 | 文档 |
|------|------|
| 开发编程规范 | [开发编程规范.md](./开发编程规范.md) |
| R3 与 UniTask 用法 | [R3与UniTask使用指南.md](./R3与UniTask使用指南.md) |
| 对象池用法 | [对象池使用指南.md](./对象池使用指南.md) |
| KCC 用法 | [KCC使用指南.md](./KCC使用指南.md) |
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

**本文件是 AI 编码的最高约束。如有与各模块文档冲突，以本文件为准。**
