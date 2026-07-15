# Editor Tools 模块功能设计指南

> **文档定位**：本文件是 Editor Tools 模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、工具清单与 API 契约，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的工具入口语义**。
>
> **模块编号**：4-2。
> **前置依赖**：所有 Runtime 模块（Editor Tools 是各模块编辑器辅助的汇总）。
> **特殊说明**：本模块**不是 IModule**，不纳入 GameGlobal，纯编辑器扩展。Editor 代码放 `Editor/` 文件夹，用独立 asmdef，不打包进 Release。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 |
|--------|------|
| 1. 菜单组织 | 统一 `PumpGF/` 菜单（无 Tools 前缀），按功能分类 |
| 2. 自定义 Drawer | 通用 Drawer（[Required]/[UniqueId]/[ConfigKey]）+ 校验菜单，两者都做 |
| 3. 数据验证器 | 统一入口 `PumpGF/Validate/All`，一键全量校验，弹出报告窗口 |
| 4. 批量操作 | 批量设置 Addressables Label、批量检查 key、批量重命名 |
| 5. SO 创建向导 | `PumpGF/Create/` 菜单创建 SO + 自动设置 Addressables |
| 6. Editor 程序集 | 独立 `PumpGF.Editor` asmdef，引用 Runtime asmdef |
| 7. AI 辅助 | 关键工具提供静态 API 入口，AI 可代码调用，不依赖 GUI 交互 |
| 风格 | 纯编辑器扩展，非 IModule |
| 底层 | Unity Editor API（MenuItem/PropertyDrawer/EditorWindow） |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**Editor Tools 是框架各模块编辑器辅助工具的汇总**——
提供统一菜单入口、自定义 Drawer、数据验证、批量操作、SO 创建向导，
减少 AI/人类的编辑器操作负担。但**不实现业务配置内容**（那是业务层的事）。

### 1.2 职责清单

| 职责 | 说明 |
|------|------|
| 统一菜单 | `PumpGF/` 菜单，按功能分类 |
| 代码生成 | GameConfigs/UI Template/UI Bindings 生成器 |
| 数据校验 | 一键校验所有配置，报告窗口 |
| 自定义 Drawer | [Required]/[UniqueId]/[ConfigKey] 等通用 Drawer |
| 批量操作 | Addressables Label、key 检查、重命名 |
| SO 创建向导 | 创建框架 SO + 自动设置 Addressables |
| API 入口 | 关键工具提供静态 API，AI 可代码调用 |

### 1.3 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ 业务配置编辑器 | 业务自行实现 |
| ❌ 可视化节点编辑器 | 过重，未来需求 |
| ❌ 运行时功能 | 纯编辑器，不打包进 Release |
| ❌ 替代 Unity 原生 Inspector | 只增强，不替代 |

---

## 2. 整体架构

```
┌─────────────────────────────────────────┐
│            编辑器菜单 PumpGF/            │
│  ┌──────────┐ ┌──────────┐ ┌─────────┐ │
│  │ Generate/│ │ Validate/│ │Create/  │ │
│  └──────────┘ └──────────┘ └─────────┘ │
│  ┌──────────────────┐ ┌──────────────┐│
│  │ Addressables/     │ │ Settings/    ││
│  └──────────────────┘ └──────────────┘│
└──────────────────┬──────────────────────┘
                   │ 调用
                   ▼
┌─────────────────────────────────────────┐
│           PumpGFEditor (静态 API)        │
│  ├─ GenerateGameConfigs()               │
│  ├─ GenerateUITemplate()                │
│  ├─ ValidateAll()                      │
│  ├─ SetAddressablesLabel()              │
│  └─ CreateSO<T>()                       │
└──────────────────┬──────────────────────┘
                   │ 引用
                   ▼
┌─────────────────────────────────────────┐
│         Runtime 模块 (各 IModule)        │
│  ConfigMgr / LocalizationMgr /          │
│  UIManager / AudioMgr / ...              │
└─────────────────────────────────────────┘

        │ 自定义 Drawer
        ▼
┌─────────────────────────────────────────┐
│         Inspector 增强                    │
│  [Required] / [UniqueId] / [ConfigKey]  │
└─────────────────────────────────────────┘
```

---

## 3. 菜单组织（PumpGF/）

### 3.1 完整菜单树

```
PumpGF/
  ├─ Generate/
  │   ├─ GameConfigs              生成 ConfigManager 强类型访问类
  │   ├─ UI Template              生成 UI View + ViewModel 骨架
  │   └─ UI Bindings from Prefab  扫描 prefab Auto_ 组件生成绑定代码
  ├─ Validate/
  │   ├─ All Configs              一键校验所有配置
  │   ├─ Localization             校验多语言完整性
  │   └─ Addressables             校验 key 一致性
  ├─ Addressables/
  │   ├─ Set Label               批量设置 Label
  │   └─ Check Keys               检查配置 key 与 Addressables 一致性
  ├─ Create/
  │   ├─ Audio Config             创建 AudioConfigSO + 自动设置 Addressables
  │   ├─ Locale                   创建 LocaleSO + 自动设置 Addressables
  │   ├─ Pause Profile            创建 PauseProfile + 自动设置 Addressables
  │   └─ Lifecycle Config         创建 LifecycleConfig + 自动设置 Addressables
  └─ Settings/
      └─ PumpGF Settings          框架全局设置（路径前缀、默认值等）
```

### 3.2 菜单约定

- 所有框架编辑器工具归入 `PumpGF/` 菜单。
- 按功能分类（Generate/Validate/Addressables/Create/Settings）。
- 业务自定义工具不归入此菜单（业务自建菜单）。

---

## 4. 各模块预留工具汇总

### 4.1 工具来源映射

| 来源模块 | 预留工具 | 菜单位置 | API 入口 |
|----------|----------|----------|----------|
| Config Manager | GameConfigs 生成器 | `PumpGF/Generate/GameConfigs` | `PumpGFEditor.GenerateGameConfigs()` |
| Config Manager | 配置校验 | `PumpGF/Validate/All Configs` | `PumpGFEditor.ValidateAll()` |
| Localization | 多语言校验 | `PumpGF/Validate/Localization` | `PumpGFEditor.ValidateLocalization()` |
| UI Framework | UI 模板生成器 | `PumpGF/Generate/UI Template` | `PumpGFEditor.GenerateUITemplate(name)` |
| UI Framework | Auto_ 绑定生成 | `PumpGF/Generate/UI Bindings from Prefab` | `PumpGFEditor.GenerateUIBindings(prefab)` |
| Audio Manager | AudioConfigSO 创建 | `PumpGF/Create/Audio Config` | `PumpGFEditor.CreateAudioConfig()` |

### 4.2 工具实现责任

- **Editor Tools 模块**：提供菜单入口、API、报告窗口、Drawer 等通用基础设施。
- **各 Runtime 模块**：提供工具的具体逻辑（如 ConfigMgr 的校验规则、UIManager 的生成逻辑）。
- Editor Tools 调用 Runtime 模块的 API 完成工具功能。

---

## 5. 自定义 Drawer

### 5.1 通用特性与 Drawer

| 特性 | 用途 | Drawer 行为 |
|------|------|-------------|
| `[Required]` | 标记必填字段 | 为 null/空时红色高亮 + 错误图标 |
| `[UniqueId]` | 标记唯一 key 字段 | 列表内重复时红色高亮 |
| `[ConfigKey]` | 标记配置 key 字段 | 与 Addressables 不一致时黄色警告 |
| `[NonNegative]` | 标记非负数字段 | 负数时红色高亮 |

### 5.2 实现方式

- 用 `PropertyDrawer` + `PropertyAttribute`。
- `OnGUI` 中检查字段值，不合法时改颜色 + 绘制图标。
- 不改变字段布局，只增加视觉提示。

### 5.3 SO 整体校验 Drawer

- 实现 `IConfigValidator` 的 SO，在 Inspector 顶部显示校验结果。
- 绿色"✓ 校验通过"或红色"✗ N 个错误"。
- 点击展开错误列表。

---

## 6. 数据验证器

### 6.1 统一验证入口

```
PumpGF/Validate/All Configs
```

- 一键校验所有配置：ConfigMgr 的 [Config] SO、Localization 的 LocaleSO、AudioMgr 的 AudioConfigSO。
- 弹出 `ValidationReportWindow` 报告窗口。

### 6.2 ValidationReportWindow

```
┌─ PumpGF 校验报告 ─────────────────┐
│                                     │
│  ✓ Config Manager    (12/12 通过)  │
│  ✗ Localization      (2 个缺失)     │
│    ├─ English 缺失 key: "Btn_Start"│
│    └─ English 缺失 key: "Welcome"  │
│  ✗ Audio             (1 个重复)     │
│    └─ key "AttackHit" 重复          │
│  ✓ Addressables      (全部一致)     │
│                                     │
│  [全部修复]  [关闭]                 │
└─────────────────────────────────────┘
```

- 按模块分类显示结果。
- 展开/折叠错误详情。
- 可选"全部修复"（自动修复可修复的问题，如去除重复 key）。

### 6.3 验证器注册

各模块注册自己的验证器：
```
PumpGFEditor.RegisterValidator(new ConfigValidator());
PumpGFEditor.RegisterValidator(new LocalizationValidator());
PumpGFEditor.RegisterValidator(new AudioValidator());
```

Editor Tools 统一调用所有注册的验证器，汇总报告。

---

## 7. 批量操作工具

### 7.1 Addressables 批量

| 工具 | 菜单 | 功能 |
|------|------|------|
| Set Label | `PumpGF/Addressables/Set Label` | 选中多个资源 → 批量设置 Addressables Label |
| Check Keys | `PumpGF/Addressables/Check Keys` | 检查配置中引用的 key 是否都在 Addressables 中存在 |

### 7.2 批量重命名

- 选中多个 SO → 批量重命名（前缀/后缀/替换）。
- `PumpGF/Addressables/Rename`（或归入通用工具）。

---

## 8. SO 创建向导

### 8.1 Create 菜单

| 菜单 | 创建 | 自动设置 |
|------|------|----------|
| `PumpGF/Create/Audio Config` | AudioConfigSO | Addressables key + 路径 |
| `PumpGF/Create/Locale` | LocaleSO | Addressables key `"Locale/{Language}"` |
| `PumpGF/Create/Pause Profile` | PauseProfile | Addressables key + 路径 |
| `PumpGF/Create/Lifecycle Config` | LifecycleConfig | Addressables key `"LifecycleConfig"` |

### 8.2 创建流程

1. 弹出创建对话框（输入名称/参数）。
2. 在指定路径创建 SO 资产。
3. 自动设置 Addressables 分组与 key。
4. 可选自动注册到对应配置（如 AudioConfigSO 自动加入 AudioMgr 配置列表）。

### 8.3 API 入口

```
PumpGFEditor.CreateSO<AudioConfigSO>("Audio/NewAudioConfig", "Audio/NewAudioConfig");
PumpGFEditor.CreateSO<LocaleSO>("Locale/English", "Locale/English");
```

---

## 9. API 入口（AI 可调用）

### 9.1 设计原则

- 所有菜单工具同时提供静态 API 入口。
- AI 可通过代码调用，不依赖 GUI 交互。
- API 返回结果（成功/失败 + 详情），便于 AI 判断。

### 9.2 PumpGFEditor 静态类

```
public static class PumpGFEditor
{
    // ── 生成 ──
    static void GenerateGameConfigs();
    static void GenerateUITemplate(string pageName);
    static void GenerateUIBindings(GameObject prefab);

    // ── 校验 ──
    static ValidationReport ValidateAll();
    static ValidationReport ValidateLocalization();
    static ValidationReport ValidateAddressables();

    // ── 验证器注册 ──
    static void RegisterValidator(IValidator validator);

    // ── Addressables ──
    static void SetAddressablesLabel(IList<UnityEngine.Object> assets, string label);
    static bool CheckAddressablesKey(string key);

    // ── SO 创建 ──
    static T CreateSO<T>(string path, string addressablesKey) where T : ScriptableObject;
}
```

### 9.3 AI 使用示例

```
// AI 生成代码后调用校验
var report = PumpGFEditor.ValidateAll();
if (report.HasErrors)
{
    foreach (var error in report.Errors)
        Debug.LogError(error);
}

// AI 创建新 Locale SO
PumpGFEditor.CreateSO<LocaleSO>("Assets/Configs/Locale/Japanese.asset", "Locale/Japanese");
```

---

## 10. Editor 程序集组织

### 10.1 asmdef 结构

```
Packages/com.pumpgf.framework/
  ├─ Runtime/
  │   ├─ PumpGF.Runtime.asmdef       ← Runtime 程序集
  │   └─ ...（各模块代码）
  └─ Editor/
      ├─ PumpGF.Editor.asmdef         ← Editor 程序集
      │   (references: PumpGF.Runtime)
      └─ ...（编辑器代码）
```

### 10.2 asmdef 配置

```
PumpGF.Editor.asmdef:
  name: PumpGF.Editor
  references: PumpGF.Runtime
  includePlatforms: Editor  ← 仅编辑器平台
  defines: UNITY_EDITOR
```

- Editor 代码不打包进 Release。
- Runtime 代码不引用 Editor 代码（单向依赖）。

---

## 11. API 契约（公开接口）

### 11.1 PumpGFEditor

```
static class PumpGFEditor
{
    // 生成
    static void GenerateGameConfigs();
    static void GenerateUITemplate(string pageName);
    static void GenerateUIBindings(GameObject prefab);

    // 校验
    static ValidationReport ValidateAll();
    static ValidationReport ValidateLocalization();
    static ValidationReport ValidateAddressables();

    // 验证器
    static void RegisterValidator(IValidator validator);

    // Addressables
    static void SetAddressablesLabel(IList<Object> assets, string label);
    static bool CheckAddressablesKey(string key);

    // SO 创建
    static T CreateSO<T>(string path, string addressablesKey) where T : ScriptableObject;
}
```

### 11.2 IValidator

```
interface IValidator
{
    string Name { get; }
    ValidationReport Validate();
}
```

### 11.3 ValidationReport

```
class ValidationReport
{
    bool HasErrors { get; }
    bool HasWarnings { get; }
    IReadOnlyList<ValidationIssue> Issues { get; }
    void FixAll();  // 自动修复可修复的问题
}

struct ValidationIssue
{
    string Module;      // 模块名
    IssueType Type;      // Error/Warning
    string Message;      // 描述
    Object Target;       // 相关资产（可点击跳转）
    bool AutoFixable;    // 是否可自动修复
}
```

### 11.4 通用特性

```
[AttributeUsage(AttributeTargets.Field)]
class RequiredAttribute : PropertyAttribute { }

[AttributeUsage(AttributeTargets.Field)]
class UniqueIdAttribute : PropertyAttribute { }

[AttributeUsage(AttributeTargets.Field)]
class ConfigKeyAttribute : PropertyAttribute { }

[AttributeUsage(AttributeTargets.Field)]
class NonNegativeAttribute : PropertyAttribute { }
```

---

## 12. 使用示例（伪代码）

### 12.1 菜单操作

```
// 人类：点击菜单 PumpGF/Generate/GameConfigs
// AI：代码调用
PumpGFEditor.GenerateGameConfigs();
// → 扫描所有 [Config] 标记 → 生成 GameConfigs.cs
```

### 12.2 校验

```
// 一键校验
var report = PumpGFEditor.ValidateAll();
if (report.HasErrors)
{
    // 弹出报告窗口
    ValidationReportWindow.Show(report);
}

// 或 AI 直接读取
foreach (var issue in report.Issues)
    Debug.Log($"[{issue.Module}] {issue.Type}: {issue.Message}");
```

### 12.3 自定义验证器

```
public class MyModuleValidator : IValidator
{
    public string Name => "MyModule";

    public ValidationReport Validate()
    {
        var report = new ValidationReport();
        // 校验逻辑...
        if (someError)
            report.AddError("配置错误: XXX", target: someAsset, autoFixable: false);
        return report;
    }
}

PumpGFEditor.RegisterValidator(new MyModuleValidator());
```

### 12.4 SO 创建

```
// 创建 AudioConfigSO 并自动设置 Addressables
PumpGFEditor.CreateSO<AudioConfigSO>(
    "Assets/Configs/Audio/BattleAudio.asset",
    "AudioConfig/Battle");
```

### 12.5 批量设置 Label

```
// 选中多个资源后
var assets = Selection.objects;
PumpGFEditor.SetAddressablesLabel(assets, "UI/Common");
```

---

## 13. 实现检查清单

- [ ] `PumpGF.Editor` asmdef 创建，引用 Runtime
- [ ] `PumpGF/` 菜单树完整（Generate/Validate/Addressables/Create/Settings）
- [ ] `[MenuItem]` 标注所有菜单项
- [ ] `PumpGFEditor` 静态 API 类（生成/校验/创建/批量）
- [ ] `IValidator` 接口 + `ValidationReport` + `ValidationIssue`
- [ ] `RegisterValidator` 验证器注册机制
- [ ] `ValidationReportWindow` EditorWindow（报告显示）
- [ ] 通用特性（Required/UniqueId/ConfigKey/NonNegative）+ PropertyDrawer
- [ ] GameConfigs 生成器（扫描 [Config] 生成 GameConfigs.cs）
- [ ] UI Template 生成器（生成 View+VM 骨架）
- [ ] UI Bindings 生成器（扫描 Auto_ 组件）
- [ ] 校验菜单（All/Localization/Addressables）
- [ ] Addressables 批量工具（Set Label/Check Keys）
- [ ] SO 创建向导（Audio Config/Locale/Pause Profile/Lifecycle Config）
- [ ] API 入口可被 AI 代码调用（不依赖 GUI）
- [ ] Editor 代码不引用运行时 IModule（用 Editor 期反射/分析）
- [ ] 所有菜单项有中文 tooltip
- [ ] 无硬编码（路径前缀走 Settings）

---

## 14. 依赖关系

| 依赖项 | 方向 | 说明 |
|--------|------|------|
| PumpGF.Runtime | 引用 | 各 Runtime 模块的类型/特性 |
| Unity Editor API | 引用 | MenuItem/PropertyDrawer/EditorWindow |
| Addressables Editor | 引用 | Addressables 分组/Label 设置 |
| 无 IModule 依赖 | — | Editor Tools 不依赖 GameGlobal 初始化 |

> **注意**：Editor Tools 在编辑器期运行，不依赖运行时 GameGlobal。
> 校验工具通过反射分析 SO 资产，不依赖运行时模块实例化。

---

## 15. 后续模块依赖本模块的接口

| 后续模块 | 使用的 Editor Tools 接口 |
|----------|------------------------|
| Testing Suite (4-3) | 测试可调用 `PumpGFEditor.ValidateAll()` 验证配置完整性 |
| 业务层 | 菜单工具、API 入口 |

---

## 16. 关键使用规范（业务层 Coding AI 必读）

### 16.1 编辑器工具归入 PumpGF/ 菜单

- ❌ 禁止：业务工具放 `Tools/` 或其他菜单
- ✅ 正确：框架工具放 `PumpGF/`，业务工具可自建菜单

### 16.2 关键工具提供 API 入口

- ❌ 禁止：只做菜单，不提供代码 API（AI 无法调用）
- ✅ 正确：菜单 + `PumpGFEditor.XXX()` 静态 API

### 16.3 校验用统一验证器

- ❌ 禁止：各模块独立写校验菜单
- ✅ 正确：实现 `IValidator` + `RegisterValidator`，统一入口 `PumpGF/Validate/All`

### 16.4 SO 创建用向导

- ❌ 禁止：手动创建 SO + 手动设置 Addressables
- ✅ 正确：`PumpGF/Create/` 向导或 `PumpGFEditor.CreateSO<T>()`

### 16.5 Editor 代码与 Runtime 分离

- Editor 代码放 `Editor/` 文件夹 + 独立 asmdef。
- Runtime 不引用 Editor 代码。

---

**文档结束。实现阶段请严格遵循本契约。**
