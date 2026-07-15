# Localization 模块功能设计指南

> **文档定位**：本文件是 Localization 模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、API 契约与数据结构，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的公开 API 语义**。
>
> **模块编号**：2-4。
> **前置依赖**：ResMgr（加载 LocaleSO/资源）、R3、TMP（字体）、ConfigMgr。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 |
|--------|------|
| 1. 语言数据存储 | SO + JSON 两者都支持（`ILocaleProvider` 抽象）；先做中文，预留多语言 |
| 2. UI 绑定模式 | ReactiveString（MVVM）+ LocalizeText 组件（直接挂 UI）双模式 |
| 3. T() 快捷方法 | 提供 `LocalizationMgr.GetText(key)` + 别名 `L.T(key)` + 格式化 `L.T(key, args)` |
| 4. 缺失回退 | 当前语言 → 默认语言（中文）→ key 名 + Warning |
| 5. 多语言资源 | 非文本资源按语言 Label 组织 Addressables |
| 6. 字体切换 | LocaleSO 配置字体，切换语言时全局切换 |
| 7. 编辑器校验 | 菜单"Tools/PumpGF/Validate Localization"校验 key 完整性 |
| 风格 | `LocalizationMgr : IModule`，纳入 `GameGlobal`，纯 C# 类 |
| 底层 | R3（ReactiveProperty/ReactiveString）+ TMP（字体） |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**Localization 是多语言文本与资源的统一管理中枢**——
通过 `ILocaleProvider` 抽象隔离数据源（SO/JSON），提供 ReactiveString 自动刷新 UI，
缺失 key 自动回退，编辑器校验完整性。但**不实现具体翻译内容**（那是美术/翻译的事）。

### 1.2 职责清单

| 职责 | 说明 |
|------|------|
| 多语言文本管理 | key → 多语言文本映射，通过 Provider 加载 |
| 动态切换 | 运行时切换语言，UI 自动刷新（ReactiveString/LocalizeText） |
| 缺失回退 | 当前语言缺失 → 默认语言 → key 名 + Warning |
| ReactiveString | 响应式字符串，语言切换自动更新（MVVM 绑定） |
| LocalizeText 组件 | 直接挂 UI 组件，指定 key 自动刷新（简单文本） |
| T() 快捷方法 | `L.T(key)` / `L.T(key, args)` 简洁访问 |
| 多语言资源 | 非文本资源按语言 Label 组织，提供 `GetLocalizedResourceKey` |
| 字体切换 | LocaleSO 配置字体，切换语言全局切换 |
| 编辑器校验 | 校验所有 LocaleSO 的 key 完整性 |

### 1.3 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ 翻译内容制作 | 美术/翻译负责 |
| ❌ 机器翻译 | 不在范围 |
| ❌ RTL（从右到左）布局 | 本阶段不做（阿拉伯文等需特殊布局） |
| ❌ 字体动态生成 | TMP 自身能力 |
| ❌ 在线翻译下载 | 未来需求 |

---

## 2. 整体架构

```
┌─────────────────────────────────────────┐
│            业务逻辑层                    │
│  L.T("Welcome")                          │
│  LocalizationMgr.GetReactiveString(key)  │
└──────────────────┬──────────────────────┘
                   │ GetText(key) / ReactiveString
                   ▼
┌─────────────────────────────────────────┐
│        LocalizationMgr (IModule)         │
│  ┌──────────────────────────────────┐   │
│  │ ILocaleProvider (数据源抽象)     │   │
│  │  ├─ SOLocaleProvider (默认)      │   │
│  │  └─ JSONLocaleProvider (可选)    │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ LocaleCache (已加载语言缓存)      │   │
│  │  Language → LocaleData            │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ CurrentLanguage (ReactiveProperty)│   │
│  │  切换时通知所有 ReactiveString     │   │
│  └──────────────────────────────────┘   │
└──────────────────┬──────────────────────┘
                   │ LoadAssetAsync<LocaleSO>
                   ▼
┌─────────────────────────────────────────┐
│              ResMgr / Addressables       │
└─────────────────────────────────────────┘

        │ 绑定 UI
        ▼
┌─────────────────┐    ┌─────────────────┐
│ ReactiveString  │    │ LocalizeText    │
│ (MVVM 绑定)     │    │ (组件直接挂)    │
└─────────────────┘    └─────────────────┘
```

---

## 3. ILocaleProvider 抽象

### 3.1 接口定义

```
public interface ILocaleProvider
{
    UniTask<LocaleData> LoadLocaleAsync(Language lang, CancellationToken ct);
    UniTask PreloadAsync(Language lang, CancellationToken ct);
    bool IsLocaleLoaded(Language lang);
    void Unload(Language lang);
}
```

### 3.2 LocaleData（运行时语言数据）

```
public class LocaleData
{
    public Language Language;
    public TMP_FontAsset Font;                    // 该语言字体
    public Dictionary<string, string> Texts;      // key → text
}
```

- 运行时统一结构，与数据源（SO/JSON）无关。
- Provider 负责从数据源构建 LocaleData。

### 3.3 SOLocaleProvider（默认实现）

- 通过 ResMgr 加载 `LocaleSO`（key 如 `"Locale/ChineseSimplified"`）。
- 从 LocaleSO 的 `List<LocaleEntry>` 构建 `Dictionary<string, string>`。
- 设置 Font。

### 3.4 JSONLocaleProvider（可选实现）

- 通过 ResMgr 加载 JSON 文件。
- 反序列化为 LocaleData。
- 适合翻译团队用 Excel/CSV 协作（导出 JSON）。
- 本阶段可只留接口，实现可选。

### 3.5 Provider 切换

```
LocalizationMgr.SetProvider(new SOLocaleProvider());
// 或未来
LocalizationMgr.SetProvider(new JSONLocaleProvider());
```

---

## 4. LocaleSO 结构

### 4.1 定义

```
[CreateAssetMenu(menuName = "PumpGF/Localization/Locale")]
public class LocaleSO : ScriptableObject
{
    public Language Language;
    public TMP_FontAsset Font;                  // 该语言字体
    public List<LocaleEntry> Entries;          // key → text（编辑器友好）
}

[Serializable]
public class LocaleEntry
{
    public string Key;
    [TextArea] public string Text;
}
```

### 4.2 编辑器使用

- 每语言创建一个 LocaleSO（如 `ChineseSimplified.asset`、`English.asset`）。
- Inspector 中编辑 `List<LocaleEntry>`（key + 多行文本）。
- 设置该语言的字体（TMP_FontAsset）。

### 4.3 Addressables key 约定

| 语言 | Addressables key |
|------|-----------------|
| 简体中文 | `"Locale/ChineseSimplified"` |
| 英语 | `"Locale/English"` |
| 日语 | `"Locale/Japanese"` |

> key 前缀 `"Locale/"` 可配（`LocalizationConfig.AddressPrefix`）。

---

## 5. ReactiveString

### 5.1 设计

`ReactiveString` 持有 key，语言切换时自动更新文本，实现 `IReadOnlyReactiveProperty<string>`：

```
public class ReactiveString : IReadOnlyReactiveProperty<string>, IDisposable
{
    private readonly string _key;
    private readonly ReactiveProperty<string> _reactive;
    private readonly object[] _args;        // 格式化参数（可选）
    private IDisposable _subscription;

    public ReactiveString(string key, params object[] args)
    {
        _key = key;
        _args = args;
        _reactive = new ReactiveProperty<string>(GetFormattedText());
        _subscription = LocalizationMgr.OnLanguageChanged
            .Subscribe(_ => _reactive.Value = GetFormattedText());
    }

    private string GetFormattedText()
    {
        var text = LocalizationMgr.GetText(_key);
        return _args != null && _args.Length > 0
            ? string.Format(text, _args)
            : text;
    }

    // 更新格式化参数（如玩家名变化时）
    public void UpdateArgs(params object[] args) { ... }

    // IReadOnlyReactiveProperty<string> 实现
    public string CurrentValue => _reactive.CurrentValue;
    public IDisposable Subscribe(Action<string> onNext) => _reactive.Subscribe(onNext);
    // ...

    public void Dispose() => _subscription?.Dispose();
}
```

### 5.2 使用场景（MVVM）

```
public class HUDViewModel : ViewModel
{
    public ReadOnlyReactiveProperty<string> WelcomeText { get; }

    public HUDViewModel()
    {
        var reactiveStr = new ReactiveString("Welcome", player.Name);
        WelcomeText = reactiveStr.ToReadOnlyReactiveProperty();
        // 语言切换时自动刷新
    }
}

// View 绑定
vm.WelcomeText.Subscribe(text => welcomeLabel.text = text).AddTo(this);
```

---

## 6. LocalizeText 组件

### 6.1 设计

直接挂 UI 组件（TMP_Text），指定 key，语言切换自动刷新：

```
public class LocalizeText : MonoBehaviour
{
    [SerializeField] private string _key;
    [SerializeField] private TMP_Text _target;
    [SerializeField] private bool _autoFindTarget = true;

    void OnEnable()
    {
        if (_target == null && _autoFindTarget)
            _target = GetComponent<TMP_Text>();
        Refresh();
        LocalizationMgr.OnLanguageChanged.Subscribe(_ => Refresh()).AddTo(this);
    }

    public void SetKey(string key)
    {
        _key = key;
        Refresh();
    }

    private void Refresh()
    {
        if (!string.IsNullOrEmpty(_key) && _target != null)
            _target.text = LocalizationMgr.GetText(_key);
    }
}
```

### 6.2 使用场景（简单文本）

- 不需要 ViewModel 的静态文本（如按钮标签、标题）。
- 直接挂 LocalizeText 组件，填 key，自动刷新。
- 与 ReactiveString 互补：复杂动态文本用 ReactiveString，静态文本用 LocalizeText。

---

## 7. T() 快捷方法

### 7.1 API

```
public static class L
{
    public static string T(string key)
        => GameGlobal.Localization.GetText(key);

    public static string T(string key, params object[] args)
        => string.Format(GameGlobal.Localization.GetText(key), args);
}
```

### 7.2 使用

```
// 简单文本
string welcome = L.T("Welcome");

// 格式化文本
string msg = L.T("KillCount", killCount);  // "击杀了 {0} 个敌人"
```

---

## 8. 缺失回退

### 8.1 回退链

```
GetText(key):
  1. 当前语言 LocaleData 查找
  2. 找到 → 返回文本
  3. 未找到 → 默认语言 LocaleData 查找
  4. 找到 → 返回文本 + Warning（当前语言缺失 key）
  5. 未找到 → 返回 key 名 + Warning（默认语言也缺失）
```

### 8.2 默认语言

- 默认语言可配（`LocalizationConfig.DefaultLanguage`，默认 `ChineseSimplified`）。
- 默认语言 LocaleData 启动时预加载。

---

## 9. 多语言资源（非文本）

### 9.1 按语言 Label 组织

非文本资源（贴图/音频/预制体）按语言 Label 组织 Addressables：

```
Addressables 结构：
  Locale/
    ChineseSimplified/
      UI_Logo (sprite)
      BGM_Title (audioClip)
    English/
      UI_Logo (sprite)
      BGM_Title (audioClip)
```

### 9.2 GetLocalizedResourceKey

```
string GetLocalizedResourceKey(string resourceKey);
// 返回 "Locale/{当前语言}/{resourceKey}"
```

业务用此 key 通过 ResMgr 加载：

```
string logoKey = LocalizationMgr.GetLocalizedResourceKey("UI_Logo");
// 当前中文 → "Locale/ChineseSimplified/UI_Logo"
Sprite logo = await ResMgr.LoadAssetAsync<Sprite>(logoKey);
```

### 9.3 切换语言时重新加载

语言切换时，非文本资源不会自动刷新（不像文本那样有 ReactiveString）。
业务需订阅 `OnLanguageChanged`，手动重新加载资源。

---

## 10. 字体切换

### 10.1 LocaleSO 配置字体

每个 LocaleSO 配置该语言的 `TMP_FontAsset`。

### 10.2 切换时全局刷新

语言切换时：
1. 更新 `TMP_Settings.defaultFontAsset`（新创建的 TMP_Text 用新字体）。
2. 通知所有 LocalizeText 组件刷新（设置 `_target.font = newFont` + 刷新文本）。
3. 不挂 LocalizeText 的 TMP_Text 由业务自行处理（订阅 `OnFontChanged`）。

### 10.3 OnFontChanged 事件

```
IObservable<TMP_FontAsset> OnFontChanged { get; }
```

业务可订阅，自行切换字体。

---

## 11. 编辑器校验

### 11.1 校验菜单

菜单"Tools/PumpGF/Validate Localization"：
- 扫描所有 LocaleSO。
- 校验 key 完整性（某 key 在 A 语言有但 B 语言缺失）。
- 校验重复 key。
- 输出报告：缺失 key 列表 + 重复 key 列表。
- Debug.LogError 列出所有问题。

### 11.2 预留 Inspector 高亮

- 本阶段做菜单校验。
- Inspector 缺失 key 高亮警告留给 Phase 4 Editor Tools。

---

## 12. API 契约（公开接口）

### 12.1 LocalizationMgr

```
class LocalizationMgr : IModule
{
    // ── 语言管理 ──
    ReactiveProperty<Language> CurrentLanguage { get; }
    IObservable<Language> OnLanguageChanged { get; }
    IObservable<TMP_FontAsset> OnFontChanged { get; }

    UniTask SetLanguageAsync(Language lang, CancellationToken ct = default);
    Language DefaultLanguage { get; }
    Language[] SupportedLanguages { get; }

    // ── 文本访问 ──
    string GetText(string key);
    string GetText(string key, params object[] args);

    // ── ReactiveString ──
    ReactiveString GetReactiveString(string key, params object[] args);

    // ── 多语言资源 ──
    string GetLocalizedResourceKey(string resourceKey);

    // ── Provider ──
    void SetProvider(ILocaleProvider provider);

    // ── 查询 ──
    bool IsLocaleLoaded(Language lang);
    bool HasKey(string key);

    // ── IModule ──
    void Init();
    void Dispose();
}
```

### 12.2 ILocaleProvider

```
interface ILocaleProvider
{
    UniTask<LocaleData> LoadLocaleAsync(Language lang, CancellationToken ct);
    UniTask PreloadAsync(Language lang, CancellationToken ct);
    bool IsLocaleLoaded(Language lang);
    void Unload(Language lang);
}
```

### 12.3 Language 枚举

```
public enum Language
{
    ChineseSimplified,  // 简体中文（默认）
    English,
    Japanese,
    // 游戏可扩展
}
```

### 12.4 L 快捷类

```
static class L
{
    static string T(string key);
    static string T(string key, params object[] args);
}
```

---

## 13. 使用示例（伪代码）

### 13.1 简单文本（LocalizeText 组件）

```
// UI prefab 上挂 LocalizeText 组件
// Inspector 填 key = "Btn_Start"
// 自动显示当前语言的"开始游戏" / "Start Game"
```

### 13.2 动态文本（ReactiveString）

```
// ViewModel 中
public class HUDViewModel : ViewModel
{
    public ReadOnlyReactiveProperty<string> KillCountText { get; }

    public HUDViewModel()
    {
        // 带格式化参数
        var reactiveStr = new ReactiveString("KillCount", 0);
        KillCountText = reactiveStr.ToReadOnlyReactiveProperty();

        // 击杀数变化时更新参数
        GameGlobal.GameData.Player.KillCount
            .Subscribe(count => reactiveStr.UpdateArgs(count))
            .AddTo(ref Bag);
    }
}
```

### 13.3 代码中获取文本

```
string welcome = L.T("Welcome");
string msg = L.T("KillCount", 42);  // "击杀了 42 个敌人"
```

### 13.4 切换语言

```
// 切换到英语
await GameGlobal.Localization.SetLanguageAsync(Language.English);
// 所有 ReactiveString 和 LocalizeText 自动刷新
```

### 13.5 多语言资源

```
string logoKey = GameGlobal.Localization.GetLocalizedResourceKey("UI_Logo");
Sprite logo = await GameGlobal.ResMgr.LoadAssetAsync<Sprite>(logoKey);

// 语言切换时重新加载
GameGlobal.Localization.OnLanguageChanged
    .Subscribe(_ => ReloadLocalizedResources())
    .AddTo(this);
```

### 13.6 LocaleSO 配置示例

```
// ChineseSimplified.asset
Language: ChineseSimplified
Font: TMP_ChineseFont
Entries:
  - Key: Btn_Start     Text: 开始游戏
  - Key: Btn_Settings   Text: 设置
  - Key: Welcome        Text: 欢迎，{0}
  - Key: KillCount      Text: 击杀了 {0} 个敌人

// English.asset
Language: English
Font: TMP_EnglishFont
Entries:
  - Key: Btn_Start     Text: Start Game
  - Key: Btn_Settings   Text: Settings
  - Key: Welcome        Text: Welcome, {0}
  - Key: KillCount      Text: Killed {0} enemies
```

---

## 14. 实现检查清单

- [ ] `LocalizationMgr : IModule`，纳入 GameGlobal
- [ ] `ILocaleProvider` 接口 + `SOLocaleProvider` 默认实现
- [ ] `JSONLocaleProvider` 预留（可只留接口）
- [ ] `LocaleSO` + `LocaleEntry` 结构定义
- [ ] `LocaleData` 运行时结构（Dictionary + Font）
- [ ] `Language` 枚举（ChineseSimplified 默认）
- [ ] `CurrentLanguage` ReactiveProperty
- [ ] `SetLanguageAsync` 异步切换（加载新语言 + 释放旧语言）
- [ ] `GetText(key)` + 缺失回退（当前 → 默认 → key 名）
- [ ] `ReactiveString` 类（响应语言切换 + 格式化参数）
- [ ] `LocalizeText` MonoBehaviour 组件
- [ ] `L.T(key)` / `L.T(key, args)` 快捷方法
- [ ] `GetLocalizedResourceKey(key)` 资源 key 拼接
- [ ] 字体切换（OnFontChanged + LocalizeText 刷新）
- [ ] 编辑器校验菜单"Tools/PumpGF/Validate Localization"
- [ ] 启动时加载默认语言
- [ ] `Dispose` 清理缓存与订阅
- [ ] 所有公开 API 有中文 XML 注释
- [ ] 无硬编码（语言 key 前缀、默认语言走配置）

---

## 15. 依赖关系

| 依赖项 | 方向 | 说明 |
|--------|------|------|
| ResMgr | 引用 | 加载 LocaleSO、多语言资源 |
| R3 | 引用 | ReactiveProperty/ReactiveString |
| TMP (TextMeshPro) | 引用 | 字体（TMP_FontAsset）、文本组件 |
| GameGlobal | 被引用 | 暴露 LocalizationMgr |

> **初始化顺序**：... → LocalizationMgr（在 ResMgr 之后）
> LocalizationMgr 依赖 ResMgr 加载 LocaleSO。

---

## 16. 后续模块依赖本模块的接口

| 后续模块 | 使用的 Localization 接口 |
|----------|------------------------|
| UI Framework (2-1) | LocalizeText 组件、ReactiveString 绑定 |
| Audio Manager (2-2) | 多语言音频资源 |
| 业务层 | `L.T()` 文本访问、多语言资源加载 |

---

## 17. 关键使用规范（业务层 Coding AI 必读）

### 17.1 禁止硬编码文本

- ❌ 禁止：`text.text = "开始游戏"`
- ✅ 正确：挂 LocalizeText 组件 + key，或用 `L.T("Btn_Start")`

### 17.2 静态文本用 LocalizeText 组件

- 按钮标签、标题等静态文本直接挂 LocalizeText。
- 不需要 ViewModel。

### 17.3 动态文本用 ReactiveString

- 带参数的文本（如"击杀了 {0} 个敌人"）用 ReactiveString + 格式化参数。
- 绑定到 ViewModel，语言切换自动刷新。

### 17.4 多语言资源用 GetLocalizedResourceKey

- ❌ 禁止：硬编码 `"Locale/Chinese/UI_Logo"`
- ✅ 正确：`LocalizationMgr.GetLocalizedResourceKey("UI_Logo")`

### 17.5 缺失 key 会在编辑器校验时报告

- 定期运行"Tools/PumpGF/Validate Localization"检查完整性。

---

**文档结束。实现阶段请严格遵循本契约。**
