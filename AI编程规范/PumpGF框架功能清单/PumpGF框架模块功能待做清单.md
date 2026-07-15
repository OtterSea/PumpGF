# PumpGF 框架模块功能待做清单

## 1. 文档说明

-   **用途**：本文档是 PumpGF 框架开发的**总任务索引**。Coding AI 应读取本文档生成开发任务列表，并按优先级顺序逐个请求“模块制作细节文档”进行编码。
-   **约定**：每个模块在开发前，必须先由架构师（人类）与架构顾问 AI 讨论生成对应的 `XXX模块制作细节.md`，Coding AI **严禁**在没有细节文档的情况下凭空编写框架代码。
-   **状态标记**：⬜ 待设计 | 🟨 设计中 | ✅ 已完成 | 🔍 待审查 | ♻️ 待重构

## 2. 核心依赖库（无需封装，直接使用）

| 库 | 版本要求 | 核心用途 | 框架集成原则 |
| :--- | :--- | :--- | :--- |
| **UniTask** | Latest | 零分配异步/await，PlayerLoop 集成 | 所有异步操作统一返回 `UniTask<T>`；禁止使用 `async void`（除事件处理器外）；取消令牌必须传递。 |
| **R3** | Latest | 响应式扩展，事件流，ReactiveProperty | 所有状态变更、事件广播统一使用 R3；UI 绑定使用 `BindTo()`；集合监听使用 `ObservableList<T>`；订阅必须绑定生命周期自动销毁。 |
| **New Input System** | Latest | Action-based 输入管理 | 仅使用 Action 模式；禁止 `Input.GetKey` 等旧 API；输入动作定义在 `.inputactions` 资产中。 |
| **Addressables** | Latest | 异步资源加载、内存管理、热更预留 | 框架 ResMgr 的唯一底层实现；禁止直接使用 Resources.Load / AssetBundle API。 |
| **UnityEngine.Pool** | Built-in | GameObject/C# 对象复用 | 框架 PoolMgr 的唯一底层实现；禁止手写 List/Queue 池化逻辑。 |

## 3. 模块开发路线图

### Phase 0: 现有模块审查与重构
> **目标**：确保已有代码符合框架整体架构标准，消除技术债，为后续模块奠定一致基础。

| 序号 | 模块名称 | 状态 | 说明 |
| :--- | :--- | :--- | :--- |
| 0-1 | Resource Manager (ResMgr) | 🔍 待审查 | 基于 Addressables 重构/审查；异步加载接口、引用计数、缓存策略、SO 路径配置、UniTask 集成度、错误处理。 |
| 0-2 | Object Pool Manager (PoolMgr) | 🔍 待审查 | 基于 UnityEngine.Pool 重构/审查；对象生命周期、预热机制、自动回收、泛型接口、R3 事件通知。 |

### Phase 1: 核心基础设施层
> **目标**：搭建框架地基，所有后续模块依赖此层。必须最先完成且质量最高。

| 序号 | 模块名称 | 状态 | 核心职责 | Vibe Coding / AI 友好要点 |
| :--- | :--- | :--- | :--- | :--- |
| 1-1 | Lifecycle Manager | ⬜ 待设计 | 统一更新循环、场景切换钩子、全局暂停/恢复、UniTask PlayerLoop 绑定 | 提供 R3 ObservableUpdate/FixedUpdate；安全 CancellationTokenSource 工厂；避免业务脚本手写 Update。 |
| 1-2 | Event System | ⬜ 待设计 | 类型安全事件总线、模块解耦通信、全局/局部事件域 | 强类型 `EventBus<T>`；订阅自动绑定 GameObject/UniTask 生命周期；提供 `SubscribeWithState` 防闭包分配。 |
| 1-3 | Config Manager (SO) | ⬜ 待设计 | ScriptableObject 加载/缓存/校验、编辑器工具、Luban 预留接口 | 定义 `IConfigProvider` 抽象；SO 实现为默认 Provider；提供 `[ConfigKey]` 特性 + 编辑器自动生成强类型访问类；Luban 接入仅需新增 Provider。 |
| 1-4 | Save/Load System | ⬜ 待设计 | 异步序列化、多槽位、数据版本迁移、ISaveProvider 抽象 | JSON/Binary 可切换；Schema 版本号标记；UniTask 异步读写；提供 SaveDataBuilder 防 AI 写错序列化结构。 |
| 1-5 | Scheduler / Timer | ⬜ 待设计 | 延迟执行、周期定时器、暂停感知、时间缩放 | 基于 UniTask.Delay 封装；支持 Pause/Resume 自动挂起；所有计时器返回 IDisposable/CancellationToken。 |

### Phase 2: 游戏通用系统层
> **目标**：覆盖单机游戏高频需求，高度模块化，支持插拔替换。

| 序号 | 模块名称 | 状态 | 核心职责 | Vibe Coding / AI 友好要点 |
| :--- | :--- | :--- | :--- | :--- |
| 2-1 | UI Framework (MVVM) | ⬜ 待设计 | 栈式页面管理、异步打开/关闭、ViewModel 绑定、弹窗队列 | 强制 MVVM 分层；VM 使用 R3 ReactiveProperty；提供 UIBinder 组件自动绑定；模板生成器让 AI 只写 VM。 |
| 2-2 | Audio Manager | ⬜ 待设计 | BGM/SFX 分离、音量组、音频池、空间音效 | SO 配置 AudioClip 元数据；Fire-and-Forget 播放 API；R3 响应式音量/静音绑定；自动池化 AudioSource。 |
| 2-3 | Input Manager | ⬜ 待设计 | New Input System 封装、上下文切换、输入缓冲、重映射 | Action-based only；运行时 InputContext 热切换；提供 `IInputActionMap` 接口；R3 Observable 暴露输入事件。 |
| 2-4 | Localization | ⬜ 待设计 | 多语言文本/资源、动态切换、缺失回退、编辑器校验 | Key-SO 映射；ReactiveString 自动刷新 UI；编辑器缺失 Key 高亮警告；提供 T() 快捷访问方法。 |

### Phase 3: 玩法支撑层
> **目标**：提供通用玩法抽象，不包含具体业务逻辑。

| 序号 | 模块名称 | 状态 | 核心职责 | Vibe Coding / AI 友好要点 |
| :--- | :--- | :--- | :--- | :--- |
| 3-1 | FSM / HSM | ⬜ 待设计 | 层级状态机、条件转换、状态数据注入、SO 配置状态图 | 纯 C# 实现；支持 Enter/Exit/Update 生命周期；提供 StateMachineBuilder 流畅 API；禁止 Update 里写 if-else。 |
| 3-2 | Entity Component (ECS-Lite) | ⬜ 待设计 | 纯 C# 组合模式、Component 注册、Entity Builder、属性变更流 | 非 DOTS；IComponent 标记接口；EntityBuilder 链式创建；属性变更走 R3 Subject；提供 Query 过滤器。 |
| 3-3 | Level / Scene Manager | ⬜ 待设计 | 场景加载流程、过渡动画、关卡数据注入、Additive Loading | 与 Addressables/Lifecycle 联动；ILevel 接口标准化入口；提供 SceneTransition 可配置过渡效果。 |

### Phase 4: 工程化与 AI 协作层
> **目标**：提升框架可维护性与 AI 协作效率，贯穿整个开发周期。

| 序号 | 模块名称 | 状态 | 核心职责 | Vibe Coding / AI 友好要点 |
| :--- | :--- | :--- | :--- | :--- |
| 4-1 | Debug Console | ⬜ 待设计 | 运行时命令、日志分级、性能面板、Gizmos 可视化 | `[ConsoleCommand]` 特性标记；结构化日志；编辑器/真机双端可用；提供 CheatManager 快速验证。 |
| 4-2 | Editor Tools | ⬜ 待设计 | 菜单项、Inspector 扩展、批量操作、数据验证器 | 减少 AI 编辑器操作负担；一键检查/修复；自定义 Drawer 让 SO 配置更直观；Menu Item 触发框架工具。 |
| 4-3 | Testing Suite | ⬜ 待设计 | 单元测试框架、Mock 工具、集成测试 Runner | 每模块附带 Tests；TestHelper 简化 Unity Mock；AI 写完代码提示运行测试；CI 友好。 |
| 4-4 | Code Templates | ⬜ 待设计 | MonoBehaviour/VM/State/Table 代码模板 | .t4 / Roslyn Source Generator；AI 生成代码引用模板保证一致性；提供 Snippet 供 IDE 使用。 |

## 4. 给 Coding AI 的执行指令

1.  **任务生成**：读取本清单，为每个 ⬜/🔍 状态的模块生成独立开发任务卡。
2.  **前置检查**：开始任何模块编码前，**必须**确认对应的 `XXX模块制作细节.md` 已存在于 `AI编程规范/` 目录。若不存在，暂停并提醒人