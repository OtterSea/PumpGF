# UI Framework (MVVM) 模块功能设计指南

> **文档定位**：本文件是 UI Framework 模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、API 契约与数据结构，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的公开 API 语义**。
>
> **模块编号**：2-1。
> **前置依赖**：ResMgr（加载 UI prefab）、GameDataStore（数据源）、EventBus、Lifecycle、Scheduler。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 |
|--------|------|
| 1. UI 系统 | uGUI（Canvas + MonoBehaviour），底层先实现，Editor 工具之后做 |
| 2. ViewModel 生命周期 | View 创建/销毁时同步创建/销毁 ViewModel，同生共死 |
| 3. 绑定方式 | UIBinder 自动绑定 + View.OnBind 手动绑定两者都支持；支持 `Auto_` 命名约定 |
| 4. 页面栈 | 栈式管理，下层页面可配置"隐藏"或"保持显示" |
| 5. 弹窗 | 弹窗队列，一次显示一个，关闭后出列下一个，支持优先级 |
| 6. UI 资源 | UI prefab 走 Addressables（ResMgr），可预加载 |
| 7. 模板生成器 | 编辑器菜单生成 View + ViewModel 骨架 |
| 8. UIManager API | 基于字符串 ID（Addressables key） |
| 风格 | `UIManager : IModule`，纳入 `GameGlobal`，纯 C# 类 |
| 底层 | uGUI + R3（ReactiveProperty/BindTo/OnClickAsObservable） |
| Editor 工具 | `Auto_` 命名约定工作流预留需求说明（见 §14），本阶段不实现 |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**UI Framework 是基于 MVVM 的 UI 管理框架**——
强制 View/ViewModel/Model 三层分离，提供页面栈、弹窗队列、异步打开关闭、自动绑定。
但**不实现具体 UI 的业务逻辑**，只提供"UI 怎么组织、怎么流转、怎么绑定"的机制。

### 1.2 职责清单

| 职责 | 说明 |
|------|------|
| MVVM 分层 | View（MonoBehaviour）/ ViewModel（纯 C#）/ Model（GameDataStore） |
| 页面栈管理 | Push/Pop/Replace，下层可配置隐藏/显示 |
| 弹窗队列 | 一次一个，优先级排序，关闭后出列下一个 |
| 自由 HUD | 常驻 UI 独立管理 |
| 异步打开/关闭 | 加载→实例化→绑定→动画，全程 UniTask |
| 自动绑定 | UIBinder + `Auto_` 命名约定（基础能力） |
| 绑定辅助 | R3 绑定封装（BindText/BindClick 等） |
| UI 层级 | Canvas 分层管理（HUD/Page/Popup/Top） |
| 模板生成 | 编辑器生成 View+VM 骨架 |

### 1.3 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ 具体 UI 业务逻辑 | 业务层实现 |
| ❌ UI 动画系统 | 用 DOTween/Unity 原生，UI Framework 只提供动画钩子 |
| ❌ UI Toolkit 支持 | 本阶段仅 uGUI |
| ❌ `Auto_` Editor 工具完整实现 | 预留需求（见 §14），本阶段只提供运行时基础能力 |
| ❌ UI 本地化 | Phase 2 Localization 模块负责 |
| ❌ UI 音效 | Audio Manager 负责 |

---

## 2. 整体架构

```
┌─────────────────────────────────────────┐
│            业务逻辑层                    │
│  (技能/背包/设置 通过 ViewModel 操作)    │
└──────────────────┬──────────────────────┘
                   │ 命令/数据
                   ▼
┌─────────────────────────────────────────┐
│         ViewModel (纯 C#)                │
│  ReactiveProperty + ICommand            │
│  订阅 GameDataStore                      │
└──────────┬──────────────────┬──────────┘
            │ BindTo            │ OnClick
            ▼                    ▼
┌──────────────────────┐  ┌──────────────────┐
│   View (MonoBehaviour) │  │  UIBinder        │
│  uGUI 组件            │  │  Auto_ 命名约定  │
│  OnBind(vm)           │  │  绑定辅助方法    │
└──────────┬───────────┘  └──────────────────┘
           │ Push/Pop/ShowPopup
           ▼
┌─────────────────────────────────────────┐
│           UIManager (IModule)            │
│  ┌─────────┐ ┌────────┐ ┌────────────┐  │
│  │PageStack│ │PopupQ  │ │HUDManager  │  │
│  └─────────┘ └────────┘ └────────────┘  │
│  ┌──────────────────────────────────┐   │
│  │ AsyncOpener (加载/实例化/绑定/动画)│   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ LayerManager (Canvas 分层)       │   │
│  └──────────────────────────────────┘   │
└──────────────────────────┬──────────────┘
                           │ LoadAssetAsync
                           ▼
┌─────────────────────────────────────────┐
│              ResMgr / Addressables       │
└─────────────────────────────────────────┘
```

---

## 3. MVVM 三层设计

### 3.1 Model

- 即 GameDataStore 的数据域。
- ViewModel 订阅 GameDataStore 的 ReactiveProperty，转换为 UI 友好格式。
- 用户操作经 ViewModel 命令 → `GameDataStore.Execute(Command)`。

### 3.2 ViewModel（纯 C#）

```
public abstract class ViewModel : IDisposable
{
    protected DisposableBag Bag;

    // UIManager 在 View 销毁时调用
    public virtual void Dispose()
    {
        Bag.Dispose();
    }
}
```

**ViewModel 示例：**
```
public class PlayerHUDViewModel : ViewModel
{
    public ReadOnlyReactiveProperty<float> Hp { get; }
    public ReadOnlyReactiveProperty<float> MaxHp { get; }
    public ReadOnlyReactiveProperty<int> Level { get; }

    public PlayerHUDViewModel()
    {
        // 订阅 GameDataStore，转换为 UI 数据
        Hp = GameGlobal.GameData.Player.Hp.ToReadOnlyReactiveProperty();
        MaxHp = GameGlobal.GameData.Player.MaxHp.ToReadOnlyReactiveProperty();
        Level = GameGlobal.GameData.Player.Level.ToReadOnlyReactiveProperty();
    }

    // 命令（用户操作转发到 GameDataStore）
    public void OnAttackButton()
        => GameGlobal.GameData.Execute(new PlayerAttackCommand());
}
```

**ViewModel 规范：**
- 纯 C#，**不引用任何 UnityEngine.UI 组件**。
- 状态用 `ReactiveProperty` / `ReadOnlyReactiveProperty`。
- 命令用方法或 `ICommand`。
- 订阅 GameDataStore 用 `Bag` 托管生命周期。
- 可单元测试（不依赖 Unity）。

### 3.3 View（MonoBehaviour）

```
public abstract class View<TViewModel> : MonoBehaviour
    where TViewModel : ViewModel, new()
{
    public TViewModel ViewModel { get; private set; }

    // UIManager 加载实例化后调用
    internal void Initialize(TViewModel vm)
    {
        ViewModel = vm;
        OnBind(vm);
    }

    // 子类实现绑定逻辑
    protected abstract void OnBind(TViewModel vm);

    // UIManager 关闭时调用
    internal void Cleanup()
    {
        OnUnbind();
        ViewModel?.Dispose();
        ViewModel = null;
    }

    protected virtual void OnUnbind() { }
}
```

**View 规范：**
- MonoBehaviour，挂在 UI prefab 根节点。
- **不写业务逻辑**，只做"显示"和"转发用户操作到 ViewModel"。
- 绑定逻辑在 `OnBind` 中（手写或 UIBinder 自动）。

---

## 4. 绑定机制

### 4.1 手动绑定（OnBind）

子类在 `OnBind` 中手写 R3 绑定：

```
public class PlayerHUDView : View<PlayerHUDViewModel>
{
    [SerializeField] private TMP_Text _hpText;
    [SerializeField] private Slider _hpSlider;
    [SerializeField] private Button _attackButton;

    protected override void OnBind(PlayerHUDViewModel vm)
    {
        // 数据 → UI（自动刷新）
        vm.Hp.Subscribe(hp => _hpText.text = $"{hp:F0}")
            .AddTo(ref Bag);

        vm.Hp.CombineLatest(vm.MaxHp, (hp, max) => hp / max)
            .Subscribe(ratio => _hpSlider.value = ratio)
            .AddTo(ref Bag);

        // UI → 命令（用户操作转发）
        _attackButton.OnClickAsObservable()
            .Subscribe(_ => vm.OnAttackButton())
            .AddTo(ref Bag);
    }
}
```

### 4.2 绑定辅助方法

View 基类提供封装好的绑定辅助，减少样板：

```
// View 基类提供的辅助方法（示例）
protected IDisposable BindText<T>(
    ReadOnlyReactiveProperty<T> prop, TMP_Text text,
    Func<T, string> formatter = null)
{
    return prop.Subscribe(v => text.text = formatter?.Invoke(v) ?? v.ToString())
        .AddTo(ref Bag);
}

protected IDisposable BindSlider(
    ReadOnlyReactiveProperty<float> prop, Slider slider)
{
    return prop.Subscribe(v => slider.value = v)
        .AddTo(ref Bag);
}

protected IDisposable BindClick(Button button, Action onClick)
{
    return button.OnClickAsObservable()
        .Subscribe(_ => onClick())
        .AddTo(ref Bag);
}

protected IDisposable BindClick(Button button, ICommand command)
{
    return button.OnClickAsObservable()
        .Subscribe(_ => command.Execute())
        .AddTo(ref Bag);
}

protected IDisposable BindActive(
    ReadOnlyReactiveProperty<bool> prop, GameObject target)
{
    return prop.Subscribe(active => target.SetActive(active))
        .AddTo(ref Bag);
}

// 按名查找子物体组件（Auto_ 工作流基础能力）
protected T FindChild<T>(string name) where T : Component
{
    var tr = transform.Find(name);
    return tr != null ? tr.GetComponent<T>() : null;
}
```

### 4.3 UIBinder 组件（自动绑定）

`UIBinder` 是挂在 View 上的 MonoBehaviour 组件，Inspector 配置绑定关系：

- Inspector 里添加绑定条目（源属性名 → 目标组件 → 绑定类型）。
- View 初始化时 UIBinder 自动执行绑定。

> **本阶段 UIBinder 提供基础框架**，完整的可视化配置 Editor 可后续增强。
> 当前推荐用手动 OnBind + 绑定辅助方法（最灵活、最可靠）。

---

## 5. Auto_ 命名约定（运行时基础 + Editor 工具预留）

### 5.1 命名约定

UI prefab 中需要自动绑定的组件，GameObject 命名以 `Auto_` 前缀：

| 命名 | 组件类型 | 用途 |
|------|----------|------|
| `Auto_HpText` | TMP_Text | 显示 HP |
| `Auto_MaxHpText` | TMP_Text | 显示最大 HP |
| `Auto_HpSlider` | Slider | HP 进度条 |
| `Auto_AttackButton` | Button | 攻击按钮 |
| `Auto_IconImage` | Image | 图标 |
| `Auto_SoundToggle` | Toggle | 声音开关 |

**命名规则：** `Auto_<语义名>`，组件类型根据 GameObject 上挂的组件自动推断。

### 5.2 运行时基础能力（本阶段实现）

View 基类提供 `FindChild<T>(name)` 基础查找能力（见 §4.2）。
业务可用此方法按名查找组件，无需手动拖拽 Inspector：

```
protected override void OnBind(PlayerHUDViewModel vm)
{
    var hpText = FindChild<TMP_Text>("Auto_HpText");
    var attackBtn = FindChild<Button>("Auto_AttackButton");

    BindText(vm.Hp, hpText, hp => $"{hp:F0}");
    BindClick(attackBtn, vm.OnAttackButton);
}
```

> 这避免了 Inspector 拖拽引用（易丢失、不可批量），改用命名约定。
> 但绑定逻辑仍需手写（"绑到哪个 VM 属性"由业务决定）。

### 5.3 Editor 工具工作流（预留，见 §14）

完整的 `Auto_` 工作流需要 Editor 工具：
1. 扫描 prefab 的 `Auto_` 前缀组件。
2. 生成 View 子类代码（字段声明 + GetComponent + 绑定骨架）。
3. 业务在生成代码上补充回调逻辑。

**本阶段不实现 Editor 工具**，但底层已支持命名查找，未来工具可实现代码生成。

---

## 6. 页面栈管理

### 6.1 栈结构

```
PageStack: Stack<PageEntry>

struct PageEntry
{
    View View;              // 页面 View 实例
    string PageId;          // 页面 ID
    PageConfig Config;      // 配置（下层是否隐藏等）
}

struct PageConfig
{
    bool HideUnderlying;    // 打开此页时是否隐藏下层（默认 true）
    bool CloseOnBack;       // 是否响应返回键关闭（默认 true）
}
```

### 6.2 页面操作

```
Push(pageId, config = default, ct)    // 打开新页面压栈
Pop(ct)                                // 关闭当前页面，回到上层
Replace(pageId, config, ct)            // 替换当前页面（不增加栈深度）
PopTo(pageId, ct)                       // 弹出到指定页面（关闭其上的所有页面）
PopAll(ct)                              // 清空页面栈
```

### 6.3 下层页面处理

- `HideUnderlying = true`（默认）：打开新页面时隐藏下层（`SetActive(false)`，省渲染）。
- `HideUnderlying = false`：下层保持显示（如半透明遮罩下层）。
- Pop 时恢复下层显示。

### 6.4 页面接口标记

```
public interface IPage { }  // 标记 View 为页面
```

页面 View 实现此接口，UIManager 据此分类管理。

---

## 7. 弹窗队列

### 7.1 队列结构

```
PopupQueue: 优先级队列

struct PopupEntry
{
    View View;
    string PopupId;
    int Priority;          // 优先级，高优先级先显示
}
```

### 7.2 弹窗操作

```
ShowPopup(popupId, priority = 0, ct)   // 加入队列
ClosePopup(ct)                          // 关闭当前弹窗，出列下一个
CloseAllPopups(ct)                      // 清空队列
```

### 7.3 队列规则

- 一次只显示一个弹窗（当前弹窗）。
- 新弹窗加入队列，按优先级排序。
- 当前弹窗关闭后，出列下一个最高优先级弹窗显示。
- 同优先级按加入顺序（FIFO）。

### 7.4 弹窗接口标记

```
public interface IPopup { }  // 标记 View 为弹窗
```

---

## 8. 自由 HUD

### 8.1 HUD 管理

- HUD 不属于页面栈也不属于弹窗。
- 常驻显示，独立管理。
- 可同时显示多个 HUD（如血条 + 小地图 + 任务追踪）。

### 8.2 HUD 操作

```
ShowHUD(hudId, ct)    // 显示 HUD（已显示则跳过）
HideHUD(hudId, ct)    // 隐藏 HUD
bool IsHUDShown(hudId)
```

### 8.3 HUD 接口标记

```
public interface IHud { }  // 标记 View 为 HUD
```

---

## 9. UI 层级管理

### 9.1 Canvas 分层

UIManager 创建 4 个分层 Canvas，按 SortingOrder 排列：

| 层级 | SortingOrder | 用途 |
|------|-------------|------|
| HUD | 100~199 | 血条/小地图/常驻 |
| Page | 200~299 | 页面栈 |
| Popup | 300~399 | 弹窗队列 |
| Top | 400~499 | Loading/全局提示/Debug |

### 9.2 层级 Canvas 特性

- 每层一个 Canvas（或 CanvasGroup），独立 SortingOrder 范围。
- 页面/弹窗实例化时挂到对应层 Canvas 下。
- HUD 层 Canvas 永不隐藏，Page/Popup 层随栈/队列变化。

---

## 10. 异步打开/关闭流程

### 10.1 打开流程（Push/ShowPopup/ShowHUD 通用）

```
OpenAsync(viewId, ct):
  1. handle = await ResMgr.LoadAssetAsync<GameObject>(viewId, ct)  // 加载 prefab
  2. instance = await ResMgr.InstantiateAsync(viewId, ct)          // 实例化
  3. view = instance.GetComponent<View>()
  4. vm = new TViewModel()                                         // 创建 VM
  5. view.Initialize(vm)                                           // 绑定
  6. 挂到对应层 Canvas 下
  7. await PlayEnterAnimation(view, ct)                           // 入场动画
  8. 返回 view
```

### 10.2 关闭流程

```
CloseAsync(view, ct):
  1. await PlayExitAnimation(view, ct)    // 出场动画
  2. view.Cleanup()                        // 解绑 + Dispose VM
  3. ResMgr.Release(instance)              // 销毁/回池
```

### 10.3 动画钩子

View 可重写入场/出场动画：
```
public abstract class View<TViewModel> : MonoBehaviour
{
    // 默认无动画（立即完成），子类可重写
    protected virtual UniTask PlayEnterAnimation(CancellationToken ct) => UniTask.CompletedTask;
    protected virtual UniTask PlayExitAnimation(CancellationToken ct) => UniTask.CompletedTask;
}
```

### 10.4 竞态处理

- 打开中再次 Push 同页面 → 取消上次打开，重新打开（或排队）。
- 关闭中再次关闭 → 幂等，跳过。
- 动画未播完就被关闭 → 取消动画，直接执行关闭流程。

---

## 11. UIManager API

### 11.1 核心 API

```
class UIManager : IModule
{
    // ── 页面栈 ──
    UniTask Push(string pageId, PageConfig config = default, CancellationToken ct = default);
    UniTask Pop(CancellationToken ct = default);
    UniTask Replace(string pageId, PageConfig config = default, CancellationToken ct = default);
    UniTask PopTo(string pageId, CancellationToken ct = default);
    UniTask PopAll(CancellationToken ct = default);

    // ── 弹窗队列 ──
    UniTask ShowPopup(string popupId, int priority = 0, CancellationToken ct = default);
    UniTask ClosePopup(CancellationToken ct = default);
    UniTask CloseAllPopups(CancellationToken ct = default);

    // ── HUD ──
    UniTask ShowHUD(string hudId, CancellationToken ct = default);
    UniTask HideHUD(string hudId, CancellationToken ct = default);
    bool IsHUDShown(string hudId);

    // ── 查询 ──
    bool IsPageInStack(string pageId);
    int PageStackDepth { get; }
    bool IsPopupQueueEmpty { get; }
    int PopupQueueCount { get; }

    // ── 预加载 ──
    UniTask PreloadAsync(IReadOnlyList<string> viewIds, IProgress<float> progress = null, CancellationToken ct = default);

    // ── IModule ──
    void Init();
    void Dispose();
}
```

### 11.2 PageConfig

```
struct PageConfig
{
    bool HideUnderlying;    // 打开时是否隐藏下层（默认 true）
    bool CloseOnBack;       // 是否响应返回键（默认 true）
}
```

### 11.3 接口标记

```
public interface IPage { }
public interface IPopup { }
public interface IHud { }
```

---

## 12. ViewModel 基类

```
public abstract class ViewModel : IDisposable
{
    protected DisposableBag Bag;

    // 子类在构造时订阅 GameDataStore，用 Bag 托管
    // public MyViewModel()
    // {
    //     GameGlobal.GameData.Player.Hp.Subscribe(...).AddTo(ref Bag);
    // }

    public virtual void Dispose()
    {
        Bag.Dispose();
    }
}
```

**ViewModel 规范：**
- 纯 C#，不引用 UnityEngine.UI。
- 状态用 ReactiveProperty。
- 订阅用 Bag 托管，Dispose 时自动清理。
- 命令用方法或 ICommand。
- 可单元测试。

---

## 13. 模板生成器

### 13.1 生成内容

菜单"Tools/PumpGF/Generate UI Template"，输入页面名（如 `PlayerHUD`），生成：

```
// PlayerHUDViewModel.cs
public class PlayerHUDViewModel : ViewModel
{
    // TODO: 声明 ReactiveProperty 状态
    // TODO: 订阅 GameDataStore
    // TODO: 声明命令方法
}

// PlayerHUDView.cs
public class PlayerHUDView : View<PlayerHUDViewModel>, IHud
{
    // TODO: 声明 UI 组件字段（或用 Auto_ 查找）
    protected override void OnBind(PlayerHUDViewModel vm)
    {
        // TODO: 绑定逻辑
    }
}
```

### 13.2 生成位置

- 代码：`Assets/Scripts/UI/<PageName>/`
- Prefab 占位：`Assets/Prefabs/UI/<PageName>.prefab`（空 Canvas + View 组件）

### 13.3 AI 工作流

1. 架构师生成模板。
2. AI 在 ViewModel 填状态订阅与命令。
3. AI 在 View 填组件字段与绑定。
4. 美术/策划编辑 prefab，用 `Auto_` 命名标记组件。
5. （未来）Auto_ Editor 工具扫描 prefab 生成绑定代码。

---

## 14. Editor 工具预留：Auto_ 工作流需求说明

> 本节是**Editor 工具的需求规格**，供后续 Phase 4 Editor Tools 实现。
> 本阶段不实现，但底层已支持命名查找基础能力。

### 14.1 工作流目标

1. 美术/策划编辑 UI prefab，给需要绑定的组件加 `Auto_` 前缀命名。
2. 点击菜单"Tools/PumpGF/Generate UI Bindings from Prefab"。
3. 工具扫描 prefab 的 `Auto_` 组件，自动生成/更新 View 子类代码：
   - 自动生成 `[SerializeField] private TMP_Text _hpText;` 字段。
   - 自动生成 `GetComponent`/`Find` 赋值代码。
   - 自动生成绑定骨架（`BindText(...)` / `BindClick(...)`）。
4. 业务层在生成代码上补充回调逻辑（VM 命令调用）。

### 14.2 命名约定

| 命名格式 | 说明 |
|----------|------|
| `Auto_<语义名>` | 自动绑定标记 |
| `Auto_HpText` | TMP_Text 组件，语义"HpText" |
| `Auto_AttackButton` | Button 组件，语义"AttackButton" |

**组件类型推断规则：**
- GameObject 上若有 `Button` 组件 → 识别为按钮。
- 若有 `TMP_Text` → 识别为文本。
- 若有 `Slider` → 识别为滑块。
- 若有 `Image` → 识别为图片。
- 若有 `Toggle` → 识别为开关。
- 多组件共存时按优先级取一个。

### 14.3 生成代码示例

输入 prefab（含 `Auto_HpText`、`Auto_AttackButton`），生成：

```
public class PlayerHUDView : View<PlayerHUDViewModel>, IHud
{
    // === Auto_ 自动生成字段（禁止手动编辑此区域）===
    // @AutoBegin
    [SerializeField] private TMP_Text _hpText;
    [SerializeField] private Button _attackButton;
    // @AutoEnd

    protected override void OnBind(PlayerHUDViewModel vm)
    {
        // === Auto_ 自动生成绑定（禁止手动编辑此区域）===
        // @AutoBindBegin
        BindText(vm.Hp, _hpText);  // TODO: 确认 VM 属性名
        BindClick(_attackButton, vm.OnAttackButton);  // TODO: 确认命令名
        // @AutoBindEnd

        // === 手动绑定区域（可自由编辑）===
        // TODO: 复杂绑定逻辑
    }
}
```

### 14.4 VM 属性映射规则（待定）

`Auto_HpText` 绑定到 ViewModel 的哪个属性？两种方案：

- **方案 A（命名约定）**：`Auto_HpText` → `vm.Hp`（去掉 `Auto_` 前缀和 `Text` 后缀）。
- **方案 B（配置表）**：Editor 工具里配置映射关系（`Auto_HpText` → `vm.Hp`）。

> 此映射规则在 Editor 工具实现时确定，底层不强制。

### 14.5 增量更新

- 工具重复运行时，只更新 `@AutoBegin`~`@AutoEnd` 和 `@AutoBindBegin`~`@AutoBindEnd` 区域。
- 手动编辑区域保留。
- 删除 prefab 上的 `Auto_` 组件时，对应生成代码标记为注释（不直接删，防误删业务代码）。

---

## 15. 使用示例（伪代码）

### 15.1 定义 ViewModel

```
public class SettingsViewModel : ViewModel, IPage
{
    public ReactiveProperty<float> BgmVolume { get; } = new(1f);
    public ReactiveProperty<float> SfxVolume { get; } = new(1f);

    public SettingsViewModel()
    {
        // 订阅 GameDataStore
        GameGlobal.GameData.Settings.BgmVolume
            .Subscribe(v => BgmVolume.Value = v)
            .AddTo(ref Bag);

        BgmVolume
            .Subscribe(v => GameGlobal.GameData.Settings.BgmVolume.Value = v)
            .AddTo(ref Bag);
    }
}
```

### 15.2 定义 View

```
public class SettingsView : View<SettingsViewModel>, IPage
{
    [SerializeField] private Slider _bgmSlider;
    [SerializeField] private Slider _sfxSlider;
    [SerializeField] private Button _closeButton;

    protected override void OnBind(SettingsViewModel vm)
    {
        // 数据 → UI
        vm.BgmVolume.Subscribe(v => _bgmSlider.value = v).AddTo(ref Bag);
        vm.SfxVolume.Subscribe(v => _sfxSlider.value = v).AddTo(ref Bag);

        // UI → 数据
        _bgmSlider.OnValueChangedAsObservable()
            .Subscribe(v => vm.BgmVolume.Value = v)
            .AddTo(ref Bag);

        _closeButton.OnClickAsObservable()
            .Subscribe(_ => GameGlobal.UIManager.Pop().Forget())
            .AddTo(ref Bag);
    }

    protected override UniTask PlayEnterAnimation(CancellationToken ct)
    {
        // 淡入动画
        return GetComponent<CanvasGroup>().DOFade(1f, 0.3f).ToUniTask(cancellationToken: ct);
    }
}
```

### 15.3 UIManager 使用

```
// 打开设置页面（压栈）
await GameGlobal.UIManager.Push("UI/SettingsPage", ct: destroyCancellationToken);

// 关闭当前页面（回到上层）
await GameGlobal.UIManager.Pop();

// 显示弹窗
await GameGlobal.UIManager.ShowPopup("UI/ConfirmDialog", priority: 0);

// 显示 HUD
await GameGlobal.UIManager.ShowHUD("UI/PlayerHUD");

// 预加载所有 UI
await GameGlobal.UIManager.PreloadAsync(
    new[] { "UI/SettingsPage", "UI/ConfirmDialog" },
    progress: Progress.Create(p => loadingBar.value = p));
```

### 15.4 Auto_ 命名查找（基础能力）

```
public class PlayerHUDView : View<PlayerHUDViewModel>, IHud
{
    private TMP_Text _hpText;
    private Button _attackButton;

    protected override void OnBind(PlayerHUDViewModel vm)
    {
        // 按命名查找（无需 Inspector 拖拽）
        _hpText = FindChild<TMP_Text>("Auto_HpText");
        _attackButton = FindChild<Button>("Auto_AttackButton");

        BindText(vm.Hp, _hpText, hp => $"{hp:F0}");
        BindClick(_attackButton, vm.OnAttackButton);
    }
}
```

---

## 16. 实现检查清单

- [ ] `UIManager : IModule`，纳入 GameGlobal
- [ ] `ViewModel` 抽象基类（DisposableBag 托管）
- [ ] `View<TViewModel>` 抽象基类（Initialize/OnBind/Cleanup/动画钩子）
- [ ] 绑定辅助方法（BindText/BindSlider/BindClick/BindActive 等）
- [ ] `FindChild<T>(name)` 命名查找基础能力
- [ ] `IPage`/`IPopup`/`IHud` 标记接口
- [ ] 页面栈（Push/Pop/Replace/PopTo/PopAll）
- [ ] `PageConfig`（HideUnderlying/CloseOnBack）
- [ ] 弹窗队列（ShowPopup/ClosePopup，优先级排序）
- [ ] HUD 管理（ShowHUD/HideHUD）
- [ ] Canvas 分层（HUD/Page/Popup/Top）
- [ ] 异步打开流程（加载→实例化→绑定→动画）
- [ ] 异步关闭流程（动画→解绑→销毁）
- [ ] 竞态处理（重复 Push/关闭中再关闭）
- [ ] `PreloadAsync` 批量预加载
- [ ] 模板生成器菜单（生成 View+VM 骨架）
- [ ] `Dispose` 清理所有页面/弹窗/HUD
- [ ] 所有公开 API 有中文 XML 注释
- [ ] 无硬编码（层级 SortingOrder 走配置）

---

## 17. 依赖关系

| 依赖项 | 方向 | 说明 |
|--------|------|------|
| ResMgr | 引用 | 加载 UI prefab、实例化 |
| GameDataStore | 引用 | ViewModel 订阅数据源 |
| EventBus | 引用 | UI 事件流转 |
| R3 | 引用 | ReactiveProperty/BindTo/OnClickAsObservable |
| UniTask | 引用 | 异步打开/关闭/动画 |
| DOTween | 可选引用 | UI 动画（业务自选，框架不强制） |
| GameGlobal | 被引用 | 暴露 UIManager |

> **初始化顺序**：... → UIManager（在 ResMgr/GameDataStore 之后）
> UIManager 依赖 ResMgr（加载 prefab）和 GameDataStore（VM 订阅数据）。

---

## 18. 后续模块依赖本模块的接口

| 后续模块 | 使用的 UIManager 接口 |
|----------|----------------------|
| Audio Manager (2-2) | 音量设置页面、音频 UI |
| Input Manager (2-3) | 输入设置页面、按键重映射 UI |
| Localization (2-4) | 多语言 UI 文本刷新 |
| Level/Scene Manager (3-3) | Loading UI、场景过渡 UI |
| Debug Console (4-1) | Debug 面板 UI |
| 业务层 | 所有业务 UI（背包/技能/设置等） |

---

## 19. 关键使用规范（业务层 Coding AI 必读）

### 19.1 严格遵守 MVVM 分层

- ❌ 禁止：View 里写业务逻辑（`button.onClick += () => player.Hp -= 10`）
- ✅ 正确：View 转发到 VM，VM 调 GameDataStore

### 19.2 ViewModel 不引用 UI 组件

- ❌ 禁止：`vm.HpText.text = ...`
- ✅ 正确：VM 用 ReactiveProperty，View 绑定

### 19.3 绑定必须托管生命周期

- ❌ 禁止：`vm.Hp.Subscribe(...)` 不 AddTo（泄漏）
- ✅ 正确：`.AddTo(ref Bag)`（VM 的 Bag）

### 19.4 页面用 Push/Pop，不要直接 Instantiate

- ❌ 禁止：业务自己 `Instantiate(prefab)` 创建 UI
- ✅ 正确：`UIManager.Push(pageId)`

### 19.5 UI prefab 走 Addressables

- ❌ 禁止：UI prefab 放 Resources
- ✅ 正确：Addressables，key 即 UIManager 的 viewId

### 19.6 Auto_ 命名规范（推荐）

- 需要绑定的组件用 `Auto_` 前缀命名，便于未来 Editor 工具自动生成。
- 当前用 `FindChild<T>("Auto_Xxx")` 查找，未来工具生成代码。

### 19.7 异步操作必须传 CancellationToken

- `Push`/`Pop`/`ShowPopup` 等异步 API 必传 ct，防页面销毁后回调悬空。

---

**文档结束。实现阶段请严格遵循本契约。**
