# Code Templates 模块功能设计指南

> **文档定位**：本文件是 Code Templates 模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、模板骨架与 API 契约，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的模板骨架语义**。
>
> **模块编号**：4-4。
> **前置依赖**：Editor Tools（生成菜单入口）、各 Runtime 模块（模板引用的类型）。
> **特殊说明**：本模块**不是 IModule**，不纳入 GameGlobal，纯编辑器扩展。模板定义 + 生成入口归入 Editor Tools。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 |
|--------|------|
| 1. 模板覆盖范围 | 9 种：MonoBehaviour/View/ViewModel/State/ConfigTableSO/Command/Event/Component/ConfigSO |
| 2. 生成方式 | 编辑器菜单（`PumpGF/Generate/Code Template/`）+ `CodeTemplateGenerator.Generate()` API |
| 3. IDE Snippet | 提供 VS Code snippet 文件（`.vscode/pumpgf.code-snippets`） |
| 4. 与 Editor Tools 集成 | 生成入口归入 Editor Tools 的 `PumpGF/Generate/` 菜单 |
| 5. 模板规范 | 统一：PumpGF 命名空间 / PascalCase / 中文 XML 注释 / DisposableBag 预置 |
| 6. 生成时配置 | 可选配置（View 的 Page/Popup/Hud、ConfigSO 的 Validator、State 的构造注入等） |
| 风格 | 纯编辑器扩展，非 IModule |
| 底层 | Unity Editor API + 文本模板 |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**Code Templates 是框架代码的统一骨架生成器**——
覆盖 9 种常见代码类型，AI/人类按模板生成骨架，保证结构/注释/命名空间一致。
但**不实现业务逻辑**（骨架只填占位，逻辑由业务写）。

### 1.2 职责清单

| 职责 | 说明 |
|------|------|
| 模板定义 | 9 种代码模板的骨架内容 |
| 菜单生成 | `PumpGF/Generate/Code Template/` 菜单入口 |
| API 生成 | `CodeTemplateGenerator.Generate()` 供 AI 代码调用 |
| IDE Snippet | VS Code snippet 文件，手动快速插入 |
| 生成配置 | 生成时可选配置（接口/参数/Validator 等） |
| 统一规范 | 命名空间/注释/结构统一 |

### 1.3 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ Source Generator | 调试难，编辑器菜单更简单可靠 |
| ❌ T4 模板 | 需 T4 工具链，编辑器菜单更直接 |
| ❌ 自动生成业务逻辑 | 只生成骨架，逻辑由业务/AI 填 |
| ❌ 运行时代码生成 | 纯编辑器期 |

---

## 2. 整体架构

```
┌─────────────────────────────────────────┐
│            生成入口                      │
│  ┌──────────────┐  ┌─────────────────┐ │
│  │ 编辑器菜单    │  │ API 调用        │ │
│  │PumpGF/Generate│  │CodeTemplateGen │ │
│  └──────────────┘  └─────────────────┘ │
└──────────────────┬──────────────────────┘
                   │ 生成
                   ▼
┌─────────────────────────────────────────┐
│       CodeTemplateGenerator             │
│  ├─ TemplateType 枚举（9 种）          │
│  ├─ TemplateConfig（生成配置）          │
│  └─ 模板内容（骨架字符串）              │
└──────────────────┬──────────────────────┘
                   │ 输出 .cs 文件
                   ▼
┌─────────────────────────────────────────┐
│         Assets/Scripts/{Category}/       │
│  {ClassName}.cs（骨架 + 占位注释）       │
└─────────────────────────────────────────┘

        │ IDE Snippet（补充）
        ▼
┌─────────────────────────────────────────┐
│  .vscode/pumpgf.code-snippets            │
│  pgf-mono / pgf-view / pgf-vm / ...     │
└─────────────────────────────────────────┘
```

---

## 3. 模板覆盖范围（9 种）

| 模板 | 类型名 | 用途 | 来源模块 |
|------|--------|------|----------|
| **MonoBehaviour** | `TemplateType.MonoBehaviour` | 通用 MonoBehaviour 脚本 | 通用 |
| **View** | `TemplateType.View` | UI View（MVVM） | UI Framework |
| **ViewModel** | `TemplateType.ViewModel` | UI ViewModel | UI Framework |
| **State** | `TemplateType.State` | FSM 状态类 | FSM/HSM |
| **ConfigTableSO** | `TemplateType.ConfigTableSO` | 配置表 | Config Manager |
| **Command** | `TemplateType.Command` | GameDataStore 命令 | GameDataStore |
| **Event** | `TemplateType.Event` | EventBus 事件 struct | Event System |
| **Component** | `TemplateType.Component` | Entity 组件 | Entity Component |
| **ConfigSO** | `TemplateType.ConfigSO` | 单例配置 SO | Config Manager |

---

## 4. 各模板骨架定义

### 4.1 MonoBehaviour 模板

```
using UnityEngine;
using R3;
using Cysharp.Threading.Tasks;
using PumpGF;

/// <summary>
/// {Description}
/// </summary>
public class {ClassName} : MonoBehaviour
{
    private DisposableBag _bag;

    private void Awake()
    {
        // TODO: 初始化
    }

    private void OnDestroy()
    {
        _bag.Dispose();
    }
}
```

**可选配置：**
- `WithDisposableBag`（默认 true）：是否预置 `_bag` 字段
- `WithAwake`（默认 true）：是否预置 Awake
- `Description`：XML 注释描述

### 4.2 View 模板

```
using UnityEngine;
using R3;
using Cysharp.Threading.Tasks;
using PumpGF;

/// <summary>
/// {Description}
/// </summary>
public class {ViewName} : View<{ViewModelName}>{InterfaceClause}
{
    protected override void OnBind({ViewModelName} vm)
    {
        // TODO: 绑定逻辑
        // BindText(vm.Hp, hpText);
        // BindClick(attackButton, vm.OnAttack);
    }

    protected override UniTask PlayEnterAnimation(CancellationToken ct)
    {
        // TODO: 入场动画
        return UniTask.CompletedTask;
    }

    protected override UniTask PlayExitAnimation(CancellationToken ct)
    {
        // TODO: 出场动画
        return UniTask.CompletedTask;
    }
}
```

**可选配置：**
- `InterfaceClause`：`, IPage` / `, IPopup` / `, IHud` / 空
- `ViewModelName`：关联的 ViewModel 类名

### 4.3 ViewModel 模板

```
using R3;
using PumpGF;

/// <summary>
/// {Description}
/// </summary>
public class {ViewModelName} : ViewModel
{
    // TODO: ReactiveProperty 状态
    // public ReactiveProperty<int> Score { get; } = new(0);

    // TODO: ReadOnlyReactiveProperty（从 GameDataStore 转换）
    // public ReadOnlyReactiveProperty<float> Hp { get; }

    public {ViewModelName}()
    {
        // TODO: 订阅 GameDataStore
        // GameGlobal.GameData.Player.Hp.Subscribe(...).AddTo(ref Bag);
    }

    // TODO: 命令方法
    // public void OnAttackButton() { ... }
}
```

### 4.4 State 模板

```
using PumpGF;

/// <summary>
/// {Description}
/// </summary>
public class {StateName} : State
{
    private readonly {DependencyType} _dependency;

    public {StateName}({DependencyType} dependency)
    {
        _dependency = dependency;
    }

    public override void OnEnter()
    {
        // TODO: 进入状态
    }

    public override void OnUpdate(float dt)
    {
        // TODO: 状态更新
    }

    public override void OnExit()
    {
        // TODO: 退出状态
    }
}
```

**可选配置：**
- `WithConstructorInjection`（默认 true）：是否预置构造注入
- `DependencyType`：注入依赖类型名

### 4.5 ConfigTableSO 模板

```
using System;
using UnityEngine;
using PumpGF;

/// <summary>
/// {RowDescription}
/// </summary>
[Serializable]
public class {RowName}
{
    // TODO: 行字段
    // public int Id;
    // public string Name;
}

/// <summary>
/// {TableDescription}
/// </summary>
[ConfigTable("{ConfigKey}")]
public class {TableName} : ConfigTableSO<{RowName}, {KeyType}>
{
    protected override {KeyType} GetKey({RowName} row)
    {
        // TODO: 返回行的 key
        return default;
    }
}
```

**可选配置：**
- `RowName`：行类名
- `KeyType`：key 类型（int/string 等）
- `ConfigKey`：Addressables key

### 4.6 Command 模板

```
using PumpGF;

/// <summary>
/// {Description}
/// </summary>
public readonly struct {CommandName} : IDataCommand
{
    public readonly {ParamType} {ParamName};

    public {CommandName}({ParamType} {paramName})
    {
        {ParamName} = {paramName};
    }
}
```

**可选配置：**
- `ParamType` / `ParamName`：参数（可多个，或无参数）

### 4.7 Event 模板

```
using PumpGF;

/// <summary>
/// {Description}
/// </summary>
public readonly struct {EventName}
{
    public readonly {FieldType} {FieldName};

    public {EventName}({FieldType} {fieldName})
    {
        {FieldName} = {fieldName};
    }
}
```

> **注意**：Event 是纯 `readonly struct`，不需要实现接口（EventBus 的 `Subscribe<TEvent> where TEvent : struct`）。

### 4.8 Component 模板

```
using R3;
using UnityEngine;
using PumpGF;

/// <summary>
/// {Description}
/// </summary>
public class {ComponentName} : IComponent
{
    // TODO: 普通字段（配置值）
    // public float Speed;

    // TODO: ReactiveProperty（关心变更的属性）
    // public ReactiveProperty<float> Hp { get; } = new(100);
}
```

### 4.9 ConfigSO 模板

```
using System.Collections.Generic;
using UnityEngine;
using PumpGF;

/// <summary>
/// {Description}
/// </summary>
[Config("{ConfigKey}")]
public class {ConfigName} : ScriptableObject{ValidatorClause}
{
    // TODO: 配置字段
    // [Range(0, 9999)] public int MaxHp = 100;

{ValidatorMethods}
}
```

**可选配置：**
- `ValidatorClause`：`, IConfigValidator`（若 `WithValidator = true`）
- `ValidatorMethods`：若 `WithValidator`，预置 `Validate()` 方法骨架：

```
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        // TODO: 校验逻辑
        return errors;
    }
```

---

## 5. 生成方式

### 5.1 编辑器菜单

```
PumpGF/Generate/Code Template/
  ├─ MonoBehaviour
  ├─ View
  ├─ ViewModel
  ├─ State
  ├─ ConfigTableSO
  ├─ Command
  ├─ Event
  ├─ Component
  └─ ConfigSO
```

点击菜单 → 弹出配置对话框 → 输入类名 + 选择配置 → 生成 .cs 文件。

### 5.2 配置对话框

```
┌─ 生成 Code Template ─────────┐
│                                │
│  类型: [View         ▼]        │
│  类名: [PlayerHUDView       ]  │
│  命名空间: [PumpGF          ]  │
│  输出路径: [Assets/Scripts/UI]│
│                                │
│  ── View 配置 ──               │
│  ViewModel: [PlayerHUDViewModel]│
│  接口: (●) Page  ( ) Popup     │
│        ( ) HUD   ( ) 无        │
│                                │
│  [生成]  [取消]                │
└────────────────────────────────┘
```

### 5.3 API 入口

```
CodeTemplateGenerator.Generate(
    TemplateType.View,
    "PlayerHUDView",
    new TemplateConfig
    {
        Namespace = "PumpGF",
        OutputPath = "Assets/Scripts/UI",
        ViewModelName = "PlayerHUDViewModel",
        IsPage = true,
    });
```

---

## 6. IDE Snippet 支持

### 6.1 VS Code Snippet 文件

`.vscode/pumpgf.code-snippets`：

```
{
  "PumpGF MonoBehaviour": {
    "prefix": "pgf-mono",
    "body": [
      "using UnityEngine;",
      "using R3;",
      "using PumpGF;",
      "",
      "public class ${1:ClassName} : MonoBehaviour",
      "{",
      "    private DisposableBag _bag;",
      "",
      "    private void Awake()",
      "    {",
      "        // TODO: 初始化",
      "    }",
      "",
      "    private void OnDestroy()",
      "    {",
      "        _bag.Dispose();",
      "    }",
      "}"
    ]
  },
  "PumpGF View": {
    "prefix": "pgf-view",
    "body": [ ... ]
  },
  // ... 其他模板
}
```

### 6.2 Snippet 前缀

| 前缀 | 模板 |
|------|------|
| `pgf-mono` | MonoBehaviour |
| `pgf-view` | View |
| `pgf-vm` | ViewModel |
| `pgf-state` | State |
| `pgf-table` | ConfigTableSO |
| `pgf-command` | Command |
| `pgf-event` | Event |
| `pgf-component` | Component |
| `pgf-config` | ConfigSO |

### 6.3 其他 IDE

- VS Code snippet 文件为默认。
- Rider/Visual Studio 可后续适配（提供 .snippet 文件）。

---

## 7. 模板规范

### 7.1 统一规范

| 规范项 | 约定 |
|--------|------|
| 命名空间 | `PumpGF`（框架）或业务命名空间（可配） |
| 命名 | PascalCase |
| 注释 | 中文 XML 注释（`/// <summary>`） |
| using | 预置常用（`R3`/`PumpGF`/`UnityEngine`/`Cysharp.Threading.Tasks`） |
| DisposableBag | 需要生命周期管理的模板预置 `_bag` 字段 |
| 占位 | `// TODO:` 标记需填写的位置 |
| 文件名 | 与类名一致，`.cs` 后缀 |

### 7.2 文件输出路径

默认按模板类型分目录：

| 模板 | 默认路径 |
|------|----------|
| MonoBehaviour | `Assets/Scripts/` |
| View | `Assets/Scripts/UI/` |
| ViewModel | `Assets/Scripts/UI/` |
| State | `Assets/Scripts/FSM/` |
| ConfigTableSO | `Assets/Scripts/Config/` |
| Command | `Assets/Scripts/Data/Commands/` |
| Event | `Assets/Scripts/Events/` |
| Component | `Assets/Scripts/Entity/` |
| ConfigSO | `Assets/Scripts/Config/` |

路径可配。

---

## 8. API 契约（公开接口）

### 8.1 CodeTemplateGenerator

```
static class CodeTemplateGenerator
{
    // 生成代码文件
    static void Generate(TemplateType type, string className, TemplateConfig config = null);

    // 获取模板内容（不写文件，用于预览/snippet）
    static string GetTemplateContent(TemplateType type, string className, TemplateConfig config = null);
}
```

### 8.2 TemplateType 枚举

```
enum TemplateType
{
    MonoBehaviour,
    View,
    ViewModel,
    State,
    ConfigTableSO,
    Command,
    Event,
    Component,
    ConfigSO,
}
```

### 8.3 TemplateConfig

```
class TemplateConfig
{
    string Namespace { get; set; } = "PumpGF";
    string OutputPath { get; set; }   // 默认按类型分目录
    string Description { get; set; }  // XML 注释描述

    // View 配置
    string ViewModelName { get; set; }
    bool IsPage { get; set; }
    bool IsPopup { get; set; }
    bool IsHud { get; set; }

    // State 配置
    bool WithConstructorInjection { get; set; } = true;
    string DependencyType { get; set; }

    // ConfigTableSO 配置
    string RowName { get; set; }
    string KeyType { get; set; }
    string ConfigKey { get; set; }

    // ConfigSO 配置
    bool WithValidator { get; set; } = false;

    // MonoBehaviour 配置
    bool WithDisposableBag { get; set; } = true;
    bool WithAwake { get; set; } = true;

    // Command/Event 配置
    string ParamType { get; set; }
    string ParamName { get; set; }
}
```

---

## 9. 使用示例（伪代码）

### 9.1 菜单生成

```
// 人类：点击 PumpGF/Generate/Code Template/View
// → 弹出对话框 → 输入 PlayerHUDView + 选择 Page → 生成
```

### 9.2 API 生成（AI 调用）

```
// AI 生成 View + ViewModel 骨架
CodeTemplateGenerator.Generate(TemplateType.ViewModel, "PlayerHUDViewModel",
    new TemplateConfig { OutputPath = "Assets/Scripts/UI" });

CodeTemplateGenerator.Generate(TemplateType.View, "PlayerHUDView",
    new TemplateConfig
    {
        OutputPath = "Assets/Scripts/UI",
        ViewModelName = "PlayerHUDViewModel",
        IsPage = true,
    });
```

### 9.3 IDE Snippet

```
// VS Code 中输入 pgf-state + Tab
// → 插入 State 骨架，光标定位到类名
```

### 9.4 生成 ConfigTableSO

```
CodeTemplateGenerator.Generate(TemplateType.ConfigTableSO, "ItemTable",
    new TemplateConfig
    {
        RowName = "ItemRow",
        KeyType = "int",
        ConfigKey = "ItemTable",
        OutputPath = "Assets/Scripts/Config",
    });
```

---

## 10. 实现检查清单

- [ ] `TemplateType` 枚举（9 种）
- [ ] `TemplateConfig` 配置类（所有可选配置）
- [ ] `CodeTemplateGenerator.Generate()` + `GetTemplateContent()`
- [ ] 9 种模板骨架内容实现
- [ ] 编辑器菜单 `PumpGF/Generate/Code Template/`（9 个子菜单）
- [ ] 配置对话框（类型/类名/命名空间/路径/特有配置）
- [ ] 文件输出到指定路径
- [ ] VS Code snippet 文件（`.vscode/pumpgf.code-snippets`，9 个 snippet）
- [ ] 模板规范统一（命名空间/注释/using/DisposableBag）
- [ ] `// TODO:` 占位标记
- [ ] API 可被 AI 代码调用
- [ ] 所有模板有中文 XML 注释占位
- [ ] 无硬编码（路径/命名空间可配）

---

## 11. 依赖关系

| 依赖项 | 方向 | 说明 |
|--------|------|------|
| PumpGF.Runtime | 引用 | 模板引用的类型（View/State/IComponent 等） |
| Unity Editor API | 引用 | 菜单/对话框/文件写入 |
| 无 IModule 依赖 | — | 纯编辑器扩展 |

---

## 12. 关键使用规范（业务层 Coding AI 必读）

### 12.1 代码骨架用模板生成

- ❌ 禁止：手写 MonoBehaviour/View/State 等骨架
- ✅ 正确：用菜单或 API 生成骨架，再填逻辑

### 12.2 生成后检查 TODO

- 模板生成的代码含 `// TODO:` 标记。
- AI 应填充所有 TODO，不留空。

### 12.3 保持命名空间一致

- 框架代码用 `PumpGF` 命名空间。
- 业务代码用业务命名空间（可配）。

### 12.4 Snippet 与菜单互补

- AI 用菜单/API 生成（批量、精确）。
- 人类用 snippet 快速插入（单个、手动）。

### 12.5 模板遵循各模块设计指南

- View 模板遵循 UI Framework 指南（View<TViewModel> 基类）。
- State 模板遵循 FSM 指南（State 基类）。
- ConfigTableSO 模板遵循 Config Manager 指南。
- 不偏离各模块设计约定。

---

**文档结束。实现阶段请严格遵循本契约。**

---

## 附：框架设计阶段完成总结

Code Templates 是 PumpGF 框架设计阶段的**最后一个模块**。至此，全部模块设计文档已完成：

| Phase | 模块 | 编号 | 文档 |
|-------|------|------|------|
| Phase 0 | ResMgr（审查重构） | 0-1 | ResMgr模块功能设计指南.md |
| Phase 0 | PoolMgr（审查重构） | 0-2 | 对象池使用指南.md（已有） |
| Phase 1 | Lifecycle | 1-1 | Lifecycle模块功能设计指南.md |
| Phase 1 | Event System | 1-2 | EventSystem模块功能设计指南.md |
| Phase 1 | Config Manager | 1-3 | ConfigManager模块功能设计指南.md |
| Phase 1 | GameDataStore | 1-4 | GameDataStore模块功能设计指南.md |
| Phase 1 | Save/Load System | 1-5 | SaveLoadSystem模块功能设计指南.md |
| Phase 1 | Scheduler/Timer | 1-6 | SchedulerTimer模块功能设计指南.md |
| Phase 2 | UI Framework | 2-1 | UIFramework模块功能设计指南.md |
| Phase 2 | Audio Manager | 2-2 | AudioManager模块功能设计指南.md |
| Phase 2 | Input Manager | 2-3 | InputManager模块功能设计指南.md |
| Phase 2 | Localization | 2-4 | Localization模块功能设计指南.md |
| Phase 3 | FSM/HSM | 3-1 | FSM_HSM模块功能设计指南.md |
| Phase 3 | Entity Component | 3-2 | EntityComponent模块功能设计指南.md |
| Phase 3 | Level/Scene Manager | 3-3 | LevelSceneManager模块功能设计指南.md |
| Phase 4 | Debug Console | 4-1 | DebugConsole模块功能设计指南.md |
| Phase 4 | Editor Tools | 4-2 | EditorTools模块功能设计指南.md |
| Phase 4 | Testing Suite | 4-3 | TestingSuite模块功能设计指南.md |
| Phase 4 | Code Templates | 4-4 | CodeTemplates模块功能设计指南.md |

**设计阶段收官，可进入实现阶段。**
