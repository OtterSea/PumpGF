# Input Manager 最佳实践

> **文档定位**：给 Coding AI 提供 InputMgr 在**真实业务场景下的标准正例**及高频误用反例。与《InputManager模块功能设计指南》(契约)配合使用：设计指南回答"接口是什么"，本文回答"业务该怎么用"。
> **写作依据**：2026-08 项目实战反哺，融合《踩坑记录》《代码质量审查报告》真实误用。
> **优先读**：AI 接输入相关需求时先读本文再写代码。

---

## 0. 一句话原则

**一切输入都走 `InputMgr`（经 InputAction 封装），禁止 `Input.GetKey`、禁止直接读 `InputAction.performed`、禁止第三方摇杆插件。**

---

## 1. 标准正例集

### 1.1 离散输入（按键）→ `OnActionPerformed`

**场景**：玩家按攻击键 → 攻击。

```csharp
// ✅ 正确
GameGlobal.InputMgr.OnActionPerformed("Attack")
    .Subscribe(_ => player.Attack())
    .AddTo(this);
```

```csharp
// ❌ 反例 1：旧 Input API
if (Input.GetKeyDown(KeyCode.Space)) Attack();   // 违反铁律，绕过 New Input System

// ❌ 反例 2：直接订阅 InputAction 事件，绕开 InputMgr 的上下文过滤
inputActions.Battle.Attack.performed += OnAttack;  // 不自动受 Stack 上下文过滤
```

> **为什么**：`OnActionPerformed` 返回的 Observable **自动受 InputContext 过滤**——当前上下文未启用该 ActionMap 时，事件不触发。直接订阅 `InputAction.performed` 会绕过这层过滤，需要业务自己判断"当前能否输入"，散落各处。

### 1.2 连续输入（移动/视角）→ `GetAxisValue`

**场景**：摇杆移动、视角旋转。

```csharp
// ✅ 正确
GameGlobal.InputMgr.GetAxisValue("Move")
    .Subscribe(move => player.Move(move))
    .AddTo(this);

GameGlobal.InputMgr.GetAxisValue("Look")
    .Subscribe(look => camera.Rotate(look))
    .AddTo(this);
```

```csharp
// ❌ 反例：在 Update 里手动读 InputSystem 事件后自己存值
float x = Input.GetAxisRaw("Horizontal");  // 旧 API + 绕开框架
```

> **为什么用 ReactiveProperty 不用 Observable**：连续值（摇杆）是"状态"而非"事件"，用 `ReadOnlyReactiveProperty` 才能在订阅时立即拿到当前值，且新订阅者能读到最新状态。离散事件用 Observable，二者不可混用。

### 1.3 按住判断 → `GetButtonValue` + `Where`

**场景**：按住冲刺、按住蓄力。

```csharp
// ✅ 正确
GameGlobal.InputMgr.GetButtonValue("Sprint")
    .Where(sprinting => sprinting)   // 只在按下为真时触发
    .Subscribe(_ => player.StartSprint())
    .AddTo(this);
```

### 1.4 动作游戏连击 → 输入缓冲 BufferInput/ConsumeInput

**场景**：攻击动画播放中预输入闪避，动画结束自动接上。

```csharp
// 输入侧：攻击键按下时缓冲
GameGlobal.InputMgr.OnActionPerformed("Attack")
    .Subscribe(_ => GameGlobal.InputMgr.BufferInput("Attack"))
    .AddTo(this);

// 动画侧：可取消帧检查缓冲
if (GameGlobal.InputMgr.ConsumeInput("Attack"))
    PlayCombo();        // 连击
else if (GameGlobal.InputMgr.ConsumeInput("Dodge"))
    PlayDodgeCancel();  // 闪避取消
```

```csharp
// ❌ 反例：自己在 Update 里写计时器实现预输入
float lastAttackTime;
void Update() { if (Input.GetKey(KeyCode.Space)) lastAttackTime = Time.time; }
// 还要自己管时间窗口、自己判断是否过期 —— 全部重复造轮子
```

> **为什么**：`BufferInput`/`ConsumeInput` 内置缓冲窗口（默认 0.2s，可 `SetBufferWindow` 调）。手动计时不仅重复，还容易漏清空/超时逻辑出错。

### 1.5 打开页面自动切换上下文 → UIManager 联动

**场景**：打开设置页，自动进入 Menu 输入上下文。

```csharp
// ✅ 正确：UIManager 自动 Push/Pop InputContext
await GameGlobal.UIManager.Push("UI/SettingsPage",
    new PageConfig { InputContext = InputContext.Menu });
// 关闭时自动 Pop 回 Battle
await GameGlobal.UIManager.Pop();
```

```csharp
// ❌ 反例：手动分开调 InputMgr.Push 和 UIManager.Push —— 容易漏调 Pop，导致上下文卡死
GameGlobal.InputMgr.Push(InputContext.Menu);
await GameGlobal.UIManager.Push("UI/SettingsPage");
// 若关闭页面时忘了 InputMgr.Pop() → Battle 输入永远收不到，菜单关闭后角色无法移动
```

> **为什么**：手动分调极易"漏配对称的 Pop"，上下文栈就永久错位。用 UIManager 联动的 `PageConfig.InputContext`，让框架保证 Push/Pop 配对。

### 1.6 防连按节流 → `ThrottleFrame`

**场景**：攻击按钮误触连按。

```csharp
// ✅ 正确
GameGlobal.InputMgr.OnActionPerformed("Attack")
    .ThrottleFrame(5)   // 5 帧内只触发一次
    .Subscribe(_ => player.Attack())
    .AddTo(this);
```

### 1.7 重映射（可选）→ RemapBinding + 持久化

**场景**：设置界面改按键。

```csharp
// 玩家点击"重映射攻击键"
GameGlobal.InputMgr.StartRebind("Attack", 0)
    .Subscribe(newPath =>
    {
        GameGlobal.InputMgr.RemapBinding("Attack", 0, newPath);
        GameGlobal.GameData.Settings.InputBindings["Attack"] = newPath;
    })
    .AddTo(this);

// 启动时恢复
foreach (var kvp in GameGlobal.GameData.Settings.InputBindings)
    GameGlobal.InputMgr.RemapBinding(kvp.Key, 0, kvp.Value);
```

---

## 2. 高频误用反例汇总（AI 思维惯性）

| # | 误用模式 | 正确做法 | 踩坑来源 |
|---|---------|---------|---------|
| 1 | `Input.GetKeyDown` / `Input.GetAxis` | `OnActionPerformed` / `GetAxisValue` | 铁律-禁用旧 API |
| 2 | 直接订阅 `InputAction.performed` | `OnActionPerformed`（受上下文过滤） | 铁律-输入走 InputMgr |
| 3 | Update 里手动做输入缓冲/计时 | `BufferInput`/`ConsumeInput` | 设计 §6 输入缓冲 |
| 4 | 手动 `Push` + `UIManager.Push` 分调 | UIManager 联动的 `PageConfig.InputContext` | 高频上下文卡死 |
| 5 | 动作名拼错（`"attack"` vs `"Attack"`） | 与 `.inputactions` 定义严格一致 | 设计 §14.3 拼错无响应 |
| 6 | 引入 JoystickPack 等第三方摇杆插件 | 官方 On-Screen Controls，值经 InputAction 回流 | 设计 §15.1 禁止插件 |
| 7 | 业务直接读 `OnScreenStick` 组件值 | `GetAxisValue("Move")` 统一读 Action | 设计 §15.6 规范项 |
| 8 | 订单不区分事件/状态混用 Observable/ReactiveProperty | 离散→Observable，连续→ReactiveProperty | 设计 §5 |
| 9 | 业务零散检测 `Gamepad.current` 判断设备 | 用框架 `CurrentInputScheme` | 设计 §15.3 上收能力 |

---

## 3. 边界与不做什么

- InputMgr **不做**：旧 Input API 支持、手柄震动、输入录制/回放、多人本地分屏。
- InputMgr **不预设具体游戏的触控布局**（摇杆位置/技能环），那是业务层。
- 触控 UI 显隐 / SafeArea 适配走 **UI Framework**，**不**在 InputMgr 内硬编码。
- 重映射是**可选能力**，不需要时忽略相关 API。
- `CurrentInputScheme`（KeyboardMouse/Gamepad/Touch）供业务切换瞄准/UI 显隐，**禁止**在业务里散落检测设备。

---

## 4. 与设计契约的关系

- 接口/语义以《InputManager模块功能设计指南》为准；本文只加"用法正例"，不改变契约。
- 契约中 §14、§15.6 的"关键使用规范"是**强制**的，本文与之对齐。
- 若冲突，以设计契约为准。

---

*本文档随 PumpGF 框架分发。由 2026-08 项目实战反哺。*
