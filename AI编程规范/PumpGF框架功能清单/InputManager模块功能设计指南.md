# Input Manager 模块功能设计指南

> **文档定位**：本文件是 Input Manager 模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、API 契约与数据结构，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的公开 API 语义**。
>
> **模块编号**：2-3。
> **前置依赖**：New Input System（Unity 包）、R3、GameDataStore、UIManager（联动）。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 |
|--------|------|
| 1. InputContext 机制 | 栈式（Push/Pop），与 UIManager 页面栈呼应 |
| 2. 输入缓冲 | 提供缓冲机制（`BufferInput`/`ConsumeInput`），动作游戏刚需 |
| 3. 重映射 | 提供运行时重映射能力 + 持久化，业务按需使用 |
| 4. R3 暴露 | 离散输入用 `IObservable`，连续输入用 `ReadOnlyReactiveProperty` |
| 5. .inputactions 组织 | 一个文件，多 ActionMap（New Input System 推荐方式） |
| 6. InputContext 定义 | 枚举（类型安全，游戏可扩展） |
| 7. UIManager 联动 | `PageConfig` 增加 `InputContext` 字段，Push/Pop 页面时自动切换 |
| 8. Global ActionMap | 保留一个始终启用的 ActionMap（ESC 返回、截图、Debug 等） |
| 9. 触控/虚拟摇杆 | 仅用 New Input System 官方 On-Screen Controls（`OnScreenStick`/`OnScreenButton`），值经 InputAction 回流；**禁止引入 JoystickPack 等绕开 InputAction 的旧 `Input.GetAxis` 类插件**。详见 §15 |
| 风格 | `InputMgr : IModule`，纳入 `GameGlobal`，纯 C# 类 |
| 底层 | New Input System + R3 |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**Input Manager 是游戏输入的统一管理与分发中枢**——
基于 New Input System，通过栈式 InputContext 管理输入上下文切换，
用 R3 暴露输入事件，提供动作游戏的输入缓冲与按键重映射能力。

### 1.2 职责清单

| 职责 | 说明 |
|------|------|
| InputAction 封装 | 将 InputAction 封装为 R3 Observable / ReactiveProperty |
| InputContext 栈式管理 | Push/Pop 上下文，自动启用/禁用对应 ActionMap |
| Global ActionMap | 始终启用的全局动作（ESC、截图等） |
| 输入缓冲 | 预输入缓冲，动画可消费 |
| 重映射 | 运行时重绑定按键，持久化到 GameDataStore |
| R3 暴露 | 离散事件用 Observable，连续值用 ReactiveProperty |
| UIManager 联动 | 页面 Push/Pop 时自动切换 InputContext |

### 1.3 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ 旧 Input API 支持 | 仅 New Input System，禁止 `Input.GetKey` |
| ❌ 输入设备物理模拟 | New Input System 自身能力 |
| ❌ 手柄震动/触觉反馈 | 可后续扩展，本阶段不做 |
| ❌ 输入录制/回放 | 过重，未来需求 |
| ❌ 多人本地输入分屏 | 本阶段不做（单机） |
| ❌ 第三方虚拟摇杆插件（JoystickPack 等） | 绕开 InputAction / 依赖旧 `Input.GetAxis`，破坏"一切输入走 InputMgr"架构，且引入付费/维护外依赖，损害框架可迁移性。触控改用官方 On-Screen Controls，见 §15 |
| ❌ 具体游戏的触控布局与玩法语义（瞄准/技能环等） | 属业务层，框架只提供移动摇杆等通用基建，见 §15.5 |

---

## 2. 整体架构

```
┌─────────────────────────────────────────┐
│            业务逻辑层                    │
│  InputMgr.OnActionPerformed("Attack")   │
│  InputMgr.GetAxisValue("Move")           │
│  InputMgr.ConsumeInput("Attack")         │
└──────────────────┬──────────────────────┘
                   │ R3 Observable / ReactiveProperty
                   ▼
┌─────────────────────────────────────────┐
│           InputMgr (IModule)             │
│  ┌──────────────────────────────────┐   │
│  │ ContextStack (栈式上下文)         │   │
│  │  栈顶 → 启用对应 ActionMap        │   │
│  │  Global → 始终启用                │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ InputBuffer (输入缓冲)            │   │
│  │  actionName → bufferTimestamp     │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ RemapSystem (重映射)              │   │
│  │  actionName → bindingPath         │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ R3 暴露层                         │   │
│  │  Observable + ReactiveProperty   │   │
│  └──────────────────────────────────┘   │
└──────────────────┬──────────────────────┘
                   │ 封装
                   ▼
┌─────────────────────────────────────────┐
│        New Input System                  │
│  .inputactions → InputActionMap          │
└─────────────────────────────────────────┘

        │ Push/Pop InputContext 联动
        ▼
┌─────────────────┐
│   UIManager     │  (PageConfig.InputContext)
└─────────────────┘
```

---

## 3. .inputactions 组织

### 3.1 文件结构

一个 `.inputactions` 文件（如 `PumpGF.inputactions`），包含多个 ActionMap：

| ActionMap | 对应 InputContext | 说明 |
|-----------|-------------------|------|
| `Global` | （始终启用） | ESC 返回、截图、Debug 开关等全局操作 |
| `Battle` | `Battle` | 战斗输入（攻击、闪避、技能、移动） |
| `Menu` | `Menu` | 菜单导航（上下左右、确认、返回） |
| `Dialog` | `Dialog` | 对话推进 |
| `Cutscene` | `Cutscene` | 过场跳过 |

### 3.2 约定

- **InputContext 枚举值名 = ActionMap 名**（约定优先，免去注册映射）。
  - `InputContext.Battle` → 启用 `"Battle"` ActionMap。
  - `InputContext.Menu` → 启用 `"Menu"` ActionMap。
- `Global` ActionMap 始终启用，不对应任何 InputContext。
- 游戏可扩展 InputContext 枚举值，并在 .inputactions 中添加对应 ActionMap。

### 3.3 InputContext 枚举

```
public enum InputContext
{
    None,        // 无上下文（仅 Global）
    Battle,      // 战斗
    Menu,        // 菜单
    Dialog,      // 对话
    Cutscene,    // 过场动画
    Debug,       // 调试
}
```

> 游戏层可直接向枚举追加值（如 `Inventory = 5`），并在 .inputactions 添加对应 ActionMap。

---

## 4. InputContext 栈式管理

### 4.1 栈结构

```
ContextStack: Stack<InputContext>
```

- `Push(context)`：压入新上下文，禁用当前上下文的 ActionMap，启用新上下文的 ActionMap + Global。
- `Pop()`：弹出栈顶，恢复下层上下文的 ActionMap + Global。
- `PopAll()`：清空栈，仅保留 Global。

### 4.2 切换流程

```
Push(Menu):
  1. 禁用当前栈顶（Battle）的 "Battle" ActionMap
  2. 压入 Menu
  3. 启用 "Menu" ActionMap
  4. "Global" ActionMap 保持启用

Pop():
  1. 禁用栈顶（Menu）的 "Menu" ActionMap
  2. 弹出 Menu
  3. 启用新栈顶（Battle）的 "Battle" ActionMap
  4. "Global" 保持启用
```

### 4.3 Global ActionMap

- 始终启用，不受 Push/Pop 影响。
- 用于全局操作：ESC 返回（触发 UIManager.Pop）、截图、Debug 开关。
- 业务可在此 ActionMap 添加全局动作。

### 4.4 当前上下文查询

```
InputContext CurrentContext { get; }   // 栈顶上下文（空栈返回 None）
int ContextStackDepth { get; }
bool IsContextActive(InputContext context);
```

---

## 5. R3 暴露

### 5.1 离散输入（Observable）

按键按下/抬起等离散事件用 `IObservable`：

```
// 动作执行时触发（按键按下瞬间）
IObservable<InputAction.CallbackContext> OnActionPerformed(string actionName);

// 动作开始（同 performed，语义别名）
IObservable<InputAction.CallbackContext> OnActionStarted(string actionName);

// 动作取消（按键抬起）
IObservable<InputAction.CallbackContext> OnActionCanceled(string actionName);
```

**使用示例：**
```
InputMgr.OnActionPerformed("Attack")
    .Subscribe(_ => player.Attack())
    .AddTo(this);

InputMgr.OnActionPerformed("Attack")
    .ThrottleFrame(5)  // 5 帧内只触发一次（防连按）
    .Subscribe(_ => player.Attack())
    .AddTo(this);
```

### 5.2 连续输入（ReactiveProperty）

摇杆方向、按键按住等连续值用 `ReadOnlyReactiveProperty`：

```
// 按钮状态（按下/抬起）
ReadOnlyReactiveProperty<bool> GetButtonValue(string actionName);

// 2D 轴值（摇杆方向）
ReadOnlyReactiveProperty<Vector2> GetAxisValue(string actionName);

// 1D 轴值（扳机）
ReadOnlyReactiveProperty<float> GetTriggerValue(string actionName);
```

**使用示例：**
```
InputMgr.GetAxisValue("Move")
    .Subscribe(move => player.Move(move))
    .AddTo(this);

InputMgr.GetButtonValue("Sprint")
    .Where(sprinting => sprinting)
    .Subscribe(_ => player.StartSprint())
    .AddTo(this);
```

### 5.3 上下文自动过滤

- R3 暴露的输入事件**自动受 InputContext 过滤**。
- 当前上下文未启用的 ActionMap，其动作不触发 Observable。
- 业务无需手动判断"当前能否输入"。

---

## 6. 输入缓冲

### 6.1 适用场景

动作游戏的预输入：
- 玩家在攻击动画播放中按下"闪避"，动画结束后自动闪避。
- 玩家提前按"跳跃"，落地后自动跳跃。
- 缓冲时间窗口内有效（如 0.2 秒），超时丢弃。

### 6.2 API

```
// 缓冲一个输入（通常在 InputAction performed 时调用）
void BufferInput(string actionName);

// 消费缓冲的输入（动画状态机调用）
// 返回 true 表示有有效缓冲输入并已消费，false 表示无
bool ConsumeInput(string actionName);

// 清空所有缓冲（如切换上下文时）
void ClearBuffer();

// 配置缓冲时间窗口
void SetBufferWindow(float seconds);  // 默认 0.2 秒
```

### 6.3 使用示例

```
// 输入侧：攻击键按下时缓冲
InputMgr.OnActionPerformed("Attack")
    .Subscribe(_ => InputMgr.BufferInput("Attack"))
    .AddTo(this);

// 动画侧：攻击动画可取消时检查缓冲
if (InputMgr.ConsumeInput("Attack"))
    PlayNextAttack();  // 连击
else if (InputMgr.ConsumeInput("Dodge"))
    PlayDodge();       // 闪避取消
```

### 6.4 自动缓冲（可选）

InputMgr 可配置"自动缓冲"模式：
- 所有 performed 自动调 `BufferInput`。
- 业务只需 `ConsumeInput`，无需手动 `BufferInput`。
- 默认关闭（避免所有输入都缓冲浪费），按需开启。

---

## 7. 重映射

### 7.1 能力提供（业务按需使用）

框架提供运行时重映射能力，**是否启用由业务决定**。

### 7.2 API

```
// 重映射指定动作的绑定
// bindingIndex: 该动作的第几个 binding（0=主绑定，1=副绑定）
// newBindingPath: 新的绑定路径（如 "<Gamepad>/buttonSouth"）
void RemapBinding(string actionName, int bindingIndex, string newBindingPath);

// 获取当前绑定路径
string GetBindingPath(string actionName, int bindingIndex);

// 重置为默认绑定
void ResetBinding(string actionName);

// 重置所有绑定
void ResetAllBindings();
```

### 7.3 持久化

- 重映射后保存到 GameDataStore。
- 加载时从 GameDataStore 恢复。

```
// GameDataStore 中的设置数据
public class SettingsData
{
    public Dictionary<string, string> InputBindings;  // actionName → bindingPath
}
```

### 7.4 重映射 UI 钩子

框架不实现重映射 UI，但提供监听输入的 API 供业务构建重映射界面：

```
// 开始监听下一个输入设备，用于重映射
// 返回的 Observable 在用户按下任意键时发出 bindingPath
IObservable<string> StartRebind(string actionName, int bindingIndex);
void CancelRebind();
```

**使用示例：**
```
// 玩家点击"重映射攻击键"按钮
InputMgr.StartRebind("Attack", 0)
    .Subscribe(newPath =>
    {
        InputMgr.RemapBinding("Attack", 0, newPath);
        GameGlobal.GameData.Settings.InputBindings["Attack"] = newPath;
        Debug.Log($"攻击键已重映射为 {newPath}");
    })
    .AddTo(this);
```

---

## 8. 与 UIManager 联动

### 8.1 PageConfig 扩展

`PageConfig` 增加 `InputContext` 字段：

```
struct PageConfig
{
    bool HideUnderlying;
    bool CloseOnBack;
    InputContext? InputContext;  // null = 不切换上下文
}
```

### 8.2 自动联动

- `UIManager.Push(pageId, config)` 时，若 `config.InputContext` 非空，自动 `InputMgr.Push(context)`。
- `UIManager.Pop()` 时，若页面有 InputContext，自动 `InputMgr.Pop()`。
- 业务可在 `PageConfig` 中设 `InputContext = null` 禁用自动联动（手动控制）。

### 8.3 示例

```
// 打开设置页面，自动切换到 Menu 上下文
await GameGlobal.UIManager.Push("UI/SettingsPage",
    new PageConfig { InputContext = InputContext.Menu });

// 关闭页面，自动 Pop InputContext 回到 Battle
await GameGlobal.UIManager.Pop();
```

---

## 9. API 契约（公开接口）

### 9.1 InputMgr

```
class InputMgr : IModule
{
    // ── 上下文栈 ──
    void Push(InputContext context);
    void Pop();
    void PopAll();
    InputContext CurrentContext { get; }
    int ContextStackDepth { get; }
    bool IsContextActive(InputContext context);

    // ── R3 暴露（离散）──
    IObservable<InputAction.CallbackContext> OnActionPerformed(string actionName);
    IObservable<InputAction.CallbackContext> OnActionStarted(string actionName);
    IObservable<InputAction.CallbackContext> OnActionCanceled(string actionName);

    // ── R3 暴露（连续）──
    ReadOnlyReactiveProperty<bool> GetButtonValue(string actionName);
    ReadOnlyReactiveProperty<Vector2> GetAxisValue(string actionName);
    ReadOnlyReactiveProperty<float> GetTriggerValue(string actionName);

    // ── 输入缓冲 ──
    void BufferInput(string actionName);
    bool ConsumeInput(string actionName);
    void ClearBuffer();
    void SetBufferWindow(float seconds);
    bool AutoBuffer { get; set; }  // 自动缓冲开关

    // ── 重映射 ──
    void RemapBinding(string actionName, int bindingIndex, string newBindingPath);
    string GetBindingPath(string actionName, int bindingIndex);
    void ResetBinding(string actionName);
    void ResetAllBindings();
    IObservable<string> StartRebind(string actionName, int bindingIndex);
    void CancelRebind();

    // ── 查询 ──
    bool HasAction(string actionName);
    bool IsActionEnabled(string actionName);  // 当前上下文是否启用
    InputScheme CurrentInputScheme { get; }   // 当前输入方案（KeyboardMouse/Gamepad/Touch），见 §15.3；供业务据此切换瞄准与触控 UI 显隐

    // ── IModule ──
    void Init();
    void Dispose();
}
```

### 9.2 InputContext 枚举

```
public enum InputContext
{
    None,
    Battle,
    Menu,
    Dialog,
    Cutscene,
    Debug,
}
```

### 9.3 InputScheme 枚举

```
// 当前活跃输入方案，用于业务据设备切换瞄准/触控 UI 显隐（见 §15.3）
public enum InputScheme
{
    KeyboardMouse,
    Gamepad,
    Touch,
}
```

---

## 10. 使用示例（伪代码）

### 10.1 订阅离散输入

```
// 攻击
InputMgr.OnActionPerformed("Attack")
    .Subscribe(_ => player.Attack())
    .AddTo(this);

// 闪避（带节流）
InputMgr.OnActionPerformed("Dodge")
    .ThrottleFrame(10)
    .Subscribe(_ => player.Dodge())
    .AddTo(this);
```

### 10.2 订阅连续输入

```
// 移动
InputMgr.GetAxisValue("Move")
    .Subscribe(move => player.Move(move))
    .AddTo(this);

// 视角
InputMgr.GetAxisValue("Look")
    .Subscribe(look => camera.Rotate(look))
    .AddTo(this);
```

### 10.3 输入缓冲（动作游戏连击）

```
// 输入侧：缓冲攻击输入
InputMgr.OnActionPerformed("Attack")
    .Subscribe(_ => InputMgr.BufferInput("Attack"))
    .AddTo(this);

// 动画侧：攻击动画可取消帧检查
if (InputMgr.ConsumeInput("Attack"))
    PlayCombo();        // 连击
else if (InputMgr.ConsumeInput("Dodge"))
    PlayDodgeCancel();  // 闪避取消
```

### 10.4 上下文切换

```
// 进入战斗
InputMgr.Push(InputContext.Battle);

// 打开菜单（自动联动 UIManager）
await GameGlobal.UIManager.Push("UI/PauseMenu",
    new PageConfig { InputContext = InputContext.Menu });

// 关闭菜单（自动 Pop 回 Battle）
await GameGlobal.UIManager.Pop();
```

### 10.5 重映射

```
// 玩家点击"重映射攻击键"
InputMgr.StartRebind("Attack", 0)
    .Subscribe(newPath =>
    {
        InputMgr.RemapBinding("Attack", 0, newPath);
        GameGlobal.GameData.Settings.InputBindings["Attack"] = newPath;
    })
    .AddTo(this);

// 启动时恢复重映射
foreach (var kvp in GameGlobal.GameData.Settings.InputBindings)
    InputMgr.RemapBinding(kvp.Key, 0, kvp.Value);
```

### 10.6 Global ActionMap（ESC 返回）

```
// Global ActionMap 中的 ESC 动作，始终响应
InputMgr.OnActionPerformed("UI_Back")  // 定义在 Global ActionMap
    .Subscribe(_ => GameGlobal.UIManager.Pop().Forget())
    .AddTo(this);
```

---

## 11. 实现检查清单

- [ ] `InputMgr : IModule`，纳入 GameGlobal
- [ ] 加载 `.inputactions` 资产，持有 InputActionAsset 引用
- [ ] InputContext 枚举定义
- [ ] ContextStack 栈式管理（Push/Pop/PopAll）
- [ ] Global ActionMap 始终启用
- [ ] Push 时禁用旧 ActionMap + 启用新 ActionMap
- [ ] `OnActionPerformed`/`OnActionStarted`/`OnActionCanceled` 返回 R3 Observable
- [ ] `GetButtonValue`/`GetAxisValue`/`GetTriggerValue` 返回 ReadOnlyReactiveProperty
- [ ] 输入自动受 InputContext 过滤（未启用的 ActionMap 不触发）
- [ ] `BufferInput`/`ConsumeInput`/`ClearBuffer` 输入缓冲
- [ ] 缓冲时间窗口可配（`SetBufferWindow`）
- [ ] `AutoBuffer` 自动缓冲开关
- [ ] `RemapBinding`/`GetBindingPath`/`ResetBinding` 重映射
- [ ] `StartRebind`/`CancelRebind` 重映射 UI 钩子
- [ ] 重映射持久化到 GameDataStore
- [ ] `CurrentInputScheme` 输入方案查询（KeyboardMouse/Gamepad/Touch，§15.3）
- [ ] 触控接入仅用官方 On-Screen Controls，值经 InputAction 回流（§15，禁止第三方摇杆插件）
- [ ] UIManager `PageConfig.InputContext` 联动
- [ ] `Init` 时加载默认 InputContext（如 Battle）
- [ ] `Dispose` 清理所有订阅与缓冲
- [ ] 所有公开 API 有中文 XML 注释
- [ ] 无硬编码（缓冲窗口等走配置）

---

## 12. 依赖关系

| 依赖项 | 方向 | 说明 |
|--------|------|------|
| New Input System | 引用 | 核心输入系统 |
| R3 | 引用 | Observable/ReactiveProperty |
| GameDataStore | 引用 | 重映射持久化 |
| UIManager | 双向引用 | PageConfig 联动 InputContext |
| GameGlobal | 被引用 | 暴露 InputMgr |

> **初始化顺序**：... → InputMgr（在 UIManager 之前或之后均可，联动在运行时发生）
> InputMgr 不依赖 UIManager 初始化，运行时 Push 页面时联动。

---

## 13. 后续模块依赖本模块的接口

| 后续模块 | 使用的 InputMgr 接口 |
|----------|---------------------|
| UI Framework (2-1) | PageConfig.InputContext 联动、UI 导航输入 |
| Audio Manager (2-2) | 输入反馈音效 |
| FSM/HSM (3-1) | 输入缓冲消费、状态转换触发 |
| Level/Scene Manager (3-3) | 过场跳过输入 |
| 业务层 | 所有玩家输入 |

---

## 14. 关键使用规范（业务层 Coding AI 必读）

### 14.1 禁止旧 Input API

- ❌ 禁止：`Input.GetKey(KeyCode.Space)`
- ✅ 正确：`InputMgr.GetButtonValue("Jump")`

### 14.2 输入必须通过 InputMgr 订阅

- ❌ 禁止：直接读 `InputAction.performed += handler`
- ✅ 正确：`InputMgr.OnActionPerformed("Attack").Subscribe(...)`

### 14.3 动作名必须与 .inputactions 一致

- `OnActionPerformed("Attack")` 的 `"Attack"` 必须是 .inputactions 中定义的 action 名。
- 拼错 → 无响应 + Warning。

### 14.4 动作游戏用输入缓冲

- 预输入用 `BufferInput`，动画可取消帧用 `ConsumeInput`。
- 不要在 Update 里手动计时器实现缓冲。

### 14.5 上下文切换优先用 UIManager 联动

- ❌ 禁止：手动 `InputMgr.Push(Menu)` + `UIManager.Push`
- ✅ 正确：`UIManager.Push(pageId, new PageConfig { InputContext = Menu })`

### 14.6 重映射是可选功能

- 框架提供能力，业务按需启用。
- 不需要重映射的项目可忽略此 API。

---

## 15. 触控输入与虚拟摇杆（移动平台）

> 面向 Android / WebGL 触控发布。本节确立"触控输入怎么接入框架"的**唯一正确路径**，
> 并划清框架层与业务层的职责边界。核心原则：**触控只是又一个喂给 InputAction 的输入设备，
> 下游消费方（`InputMgr` / `ICharacterInputSource` / FSM）完全无感知。**

### 15.1 选型决策：官方 On-Screen Controls，拒绝第三方摇杆插件

| 方案 | 结论 | 理由 |
|------|------|------|
| **New Input System 官方 `OnScreenStick` / `OnScreenButton`** | ✅ 采用 | 随 Input System 包自带、免费、官方维护、零额外依赖；控件值直接写入 InputAction，天然融入本模块 |
| JoystickPack 等第三方摇杆插件 | ❌ 禁止 | 基于旧 `Input.GetAxis`，自行维护 `Direction` 值，需业务**直接读组件**，**完全绕开 InputAction 与 InputMgr**；破坏"一切输入走 InputMgr、禁止直接读设备"的架构铁律，且引入外部依赖损害框架可迁移性 |

> **为什么官方方案零成本融入**：`OnScreenStick` 挂在 UI 控件上，配置 `Control Path` 指向某个设备控件（如 `<Gamepad>/leftStick`），
> 玩家拖动时它把归一化向量**注入 InputAction**。于是 `InputMgr.GetAxisValue("Move")` 收到的值与真实手柄左摇杆**完全同源同形**，
> `PlayerInputSource` / `ICharacterInputSource` **一行都不用改**。这正是本模块"输入源与消费方经 InputAction 解耦"的架构红利。

### 15.2 架构接入点（值的流向）

```
[屏幕虚拟摇杆 OnScreenStick]  ─┐
[真实手柄左摇杆 leftStick]    ─┼─► InputAction "Move" ─► InputMgr.GetAxisValue("Move")
[键盘 WASD 合成 Vector2]      ─┘                              │
                                                              ▼
                                              ICharacterInputSource.Move（消费方无感知设备来源）
```

- 触控设备与实体设备**共用同一个 Action**（如 "Move"），或按需绑定到独立 ControlScheme，二选一（见 §15.4）。
- 无论哪种，最终都以 `InputAction.ReadValue<Vector2>()` 收口，`InputMgr` 与业务读值方式不变。

### 15.3 框架层提供的通用触控基建（可迁移、不绑玩法）

框架**只**沉淀与具体游戏无关的通用能力：

| 基建 | 说明 | 归属 |
|------|------|------|
| 官方 On-Screen Controls 接入约定 | 本节规范本身；虚拟摇杆值必须经 InputAction 回流，禁止业务直接读控件组件 | InputMgr 规范 |
| 当前输入方案查询（建议提供） | `InputMgr` 暴露 `CurrentInputScheme`（`KeyboardMouse` / `Gamepad` / `Touch`），供业务据此切换瞄准/UI 显隐等表现（把散落在业务 `PlayerInputSource` 的 `Gamepad.current.wasUpdatedThisFrame` 设备检测上收为框架能力） | InputMgr |
| 触控层显隐与 SafeArea 适配 | 竖屏安全区适配、按平台/输入方案自动显隐触控 UI 根节点 | UI Framework 模块（非本模块，另行沉淀） |

> **移动摇杆是框架承诺提供的通用基建**：一个绑定到 "Move" Action 的 `OnScreenStick` 布局约定 + 预制体约定，
> 可随框架迁移到任意项目直接复用。

### 15.4 ControlScheme 约定（二选一，业务据平台取舍）

- **方案 A（推荐，最省事）**：`OnScreenStick.controlPath = <Gamepad>/leftStick`，直接复用现有 Battle map 的 "Move" Action，
  触控与手柄同链路，无需新增 ControlScheme。适合"触控与手柄行为一致"的动作游戏。
- **方案 B（需要区分设备时）**：新增独立 `Touch` ControlScheme + 虚拟设备绑定，配合 §15.3 的 `CurrentInputScheme` 做设备分流。
  适合需要"检测到触控时切换专属 UI/瞄准"的项目。

### 15.5 业务层职责（框架不代劳）

以下属**具体游戏**决定，框架不提供、不预设：

- 触控 UI 的**具体布局**（摇杆位置/大小、按钮排布、竖屏适配细节）。
- **瞄准 / 朝向的触控语义**（右摇杆瞄准 / 依赖自动瞄准 / 攻击摇杆二合一 等）——各游戏手感诉求不同，由项目自定。
- 触控专属的技能环、连招板、手势等玩法级交互。
- 是否需要为触控新增 `ICharacterInputSource` 实现：**通常不需要**——因为移动/攻击/冲刺的值都经 InputAction 回流，
  现有 `PlayerInputSource` 直接可用；仅当项目要引入"触控独有语义"（如触控瞄准）时，才在业务层扩展实现。

### 15.6 关键规范（触控接入 Coding AI 必读）

1. ❌ 禁止引入 JoystickPack 及任何绕开 InputAction 的第三方摇杆/触控输入插件。
2. ❌ 禁止业务代码直接读取 `OnScreenStick` / `OnScreenButton` 组件的值；一律经 `InputMgr.GetAxisValue / GetButtonValue` 读取对应 Action。
3. ✅ 虚拟摇杆的 `Control Path` 绑定到与实体设备同源的 Action（如 "Move"），保证下游消费方无感知。
4. ✅ 触控 UI 的显隐 / SafeArea 适配走 UI Framework，不在输入模块内硬编码布局。
5. ✅ 需要按设备分流表现时，用框架 `CurrentInputScheme`，禁止在业务里各自零散检测 `Gamepad.current` / `Touchscreen.current`。

---

**文档结束。实现阶段请严格遵循本契约。**
