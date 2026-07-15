# Debug Console 模块功能设计指南

> **文档定位**：本文件是 Debug Console 模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、API 契约与数据结构，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的公开 API 语义**。
>
> **模块编号**：4-1。
> **前置依赖**：R3、UniTask、UIManager（控制台 UI）、EntityManager（实体数指标）。
> **条件编译**：Debug Console 主体用 `#if DEBUG` 包裹，Release 包剔除。Log 类始终保留。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 |
|--------|------|
| 1. 命令注册 | `[ConsoleCommand]` 特性标记 + 启动时反射扫描自动注册 |
| 2. 日志分级 | `Log` 类，4 级（Debug/Info/Warning/Error）+ Tag，Release 过滤到 Warning+ |
| 3. 性能面板 | 预置 FPS/内存/实体数/DrawCall + `IDebugPanel` 扩展接口（业务自定义面板） |
| 4. 作弊命令 | **不预置**，只提供 `[ConsoleCommand]` 机制，业务自行实现作弊命令类 |
| 5. Gizmos | `IGizmosDrawable` 注册系统，框架统一调用 OnDrawGizmos，可开关 |
| 6. Console UI | uGUI 控制台面板（命令输入/历史/日志）+ IMGUI 性能面板 |
| 7. 开关方式 | 编辑器快捷键 `` ` ``（反引号），真机三指点击屏幕顶部 |
| 8. Release 处理 | DebugConsole 主体 `#if DEBUG` 剔除；Log 类保留但过滤到 Warning+ |
| 风格 | `DebugConsole : IModule`，纳入 `GameGlobal` |
| 底层 | uGUI + IMGUI + R3 |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**Debug Console 是运行时调试与作弊验证的统一工具**——
提供命令系统、日志分级、性能面板、Gizmos 可视化，编辑器/真机双端可用。
但**不实现具体作弊逻辑**（业务通过 [ConsoleCommand] 自行实现）。

### 1.2 职责清单

| 职责 | 说明 |
|------|------|
| 命令系统 | `[ConsoleCommand]` 特性反射注册，参数自动解析 |
| 日志分级 | `Log` 类，4 级 + Tag，可配过滤级别 |
| 性能面板 | 预置指标 + `IDebugPanel` 扩展接口 |
| Gizmos 系统 | `IGizmosDrawable` 注册，统一 OnDrawGizmos |
| Console UI | uGUI 控制台面板（输入/历史/日志） |
| 双端可用 | 编辑器快捷键 + 真机手势开关 |
| Release 剔除 | `#if DEBUG` 条件编译 |

### 1.3 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ 预置作弊命令 | 业务自行实现（[ConsoleCommand] 机制已足够） |
| ❌ Profiler 替代 | 不做完整性能分析，只做轻量指标 |
| ❌ 编辑器窗口 | 用 uGUI 统一，不做 EditorWindow（Phase 4-2 Editor Tools 负责） |
| ❌ 日志持久化 | 本阶段不做日志文件写入 |
| ❌ 远程调试 | 未来需求 |

---

## 2. 整体架构

```
┌─────────────────────────────────────────┐
│            业务逻辑层                    │
│  [ConsoleCommand] 标记的方法            │
│  Log.Debug("tag", "msg")               │
│  IGizmosDrawable 实现                   │
└──────────────────┬──────────────────────┘
                   │ 反射注册 / 调用
                   ▼
┌─────────────────────────────────────────┐
│          DebugConsole (IModule)          │
│  ┌──────────────────────────────────┐   │
│  │ CommandSystem                     │   │
│  │  反射扫描 [ConsoleCommand]         │   │
│  │  命令名 → MethodInfo + 参数       │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ LogSystem                         │   │
│  │  FilterLevel + Tag 过滤           │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ PanelSystem (IDebugPanel)         │   │
│  │  预置 StatsPanel + 业务自定义      │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ GizmosSystem (IGizmosDrawable)    │   │
│  └──────────────────────────────────┘   │
└──────┬──────────┬──────────┬────────────┘
       │ uGUI     │ IMGUI     │ Gizmos
       ▼          ▼          ▼
┌──────────┐ ┌──────────┐ ┌──────────┐
│ ConsoleUI│ │ StatsPanel│ │GizmosDriver│
│ (输入/历史)│ │ (OnGUI)  │ │(OnDrawGizmos)│
└──────────┘ └──────────┘ └──────────┘
```

---

## 3. 命令系统

### 3.1 [ConsoleCommand] 特性

```
[AttributeUsage(AttributeTargets.Method)]
public class ConsoleCommandAttribute : Attribute
{
    public string CommandName { get; }
    public string Description { get; }

    public ConsoleCommandAttribute(string name, string description = null)
    {
        CommandName = name;
        Description = description;
    }
}
```

### 3.2 命令定义示例（业务实现）

```
// 业务层自定义作弊命令类
public static class GameCheats
{
    [ConsoleCommand("add_gold", "添加金币 amount")]
    public static void AddGold(int amount)
    {
        GameGlobal.GameData.Inventory.Gold.Value += amount;
        Log.Info("Cheat", $"添加 {amount} 金币");
    }

    [ConsoleCommand("god_mode", "无敌模式开关")]
    public static void GodMode()
    {
        GameGlobal.GameData.Player.SetInvincible(
            !GameGlobal.GameData.Player.IsInvincible);
    }

    [ConsoleCommand("load_level", "加载关卡 levelKey")]
    public static void LoadLevel(string levelKey)
    {
        GameGlobal.LevelManager.LoadLevelAsync(levelKey).Forget();
    }
}
```

> **框架不预置任何作弊命令**，业务通过 `[ConsoleCommand]` 自行实现。

### 3.3 反射注册

- DebugConsole 初始化时扫描所有程序集，找 `[ConsoleCommand]` 标记的方法。
- 注册到命令表：`Dictionary<string, CommandInfo>`。
- `CommandInfo` 含 MethodInfo + 参数列表 + 描述。

### 3.4 参数自动解析

支持基本类型参数自动解析：

| 命令输入 | 方法签名 | 解析 |
|----------|----------|------|
| `add_gold 100` | `AddGold(int amount)` | amount = 100 |
| `set_hp 50.5` | `SetHp(float hp)` | hp = 50.5f |
| `load_level Battle` | `LoadLevel(string key)` | key = "Battle" |
| `god_mode` | `GodMode()` | 无参数 |
| `enable_debug true` | `EnableDebug(bool enabled)` | enabled = true |

- 解析失败 → 控制台显示错误 + 命令用法。
- 支持类型：`int`/`float`/`string`/`bool`。

### 3.5 命令执行

```
DebugConsole.Execute("add_gold 100");
// → 查找命令 "add_gold"
// → 解析参数 "100" → int 100
// → 调用 AddGold(100)
```

### 3.6 命令历史与自动补全

- 命令历史：上/下键浏览。
- 自动补全：输入前缀 + Tab 补全。
- `help` 命令：列出所有命令 + 描述。

---

## 4. 日志分级

### 4.1 Log 类

```
public static class Log
{
    public static LogLevel FilterLevel { get; set; } =
#if DEBUG
        LogLevel.Debug;
#else
        LogLevel.Warning;
#endif

    public static void Debug(string tag, string msg, Object context = null);
    public static void Info(string tag, string msg, Object context = null);
    public static void Warning(string tag, string msg, Object context = null);
    public static void Error(string tag, string msg, Object context = null);
}
```

### 4.2 LogLevel 枚举

```
public enum LogLevel
{
    Debug,    // 开发期详细日志
    Info,     // 常规信息
    Warning,  // 警告
    Error,    // 错误
}
```

### 4.3 Tag 分类

- 每条日志带 Tag（如 "Audio"、"Combat"、"Save"）。
- 可按 Tag 过滤：`Log.SetTagFilter("Audio", enabled: false)`。
- 控制台 UI 可切换 Tag 显示。

### 4.4 输出格式

```
[Audio] BGM 切换到 Battle
[Combat] 玩家受到 30 点伤害
[Save] 存档写入成功
```

- 内部调用 `UnityEngine.Debug.Log/LogWarning/LogError`，保留 Unity Console 跳转能力。
- `context` 参数支持点击日志跳转 GameObject。

### 4.5 Release 模式

- Log 类始终保留（Release 也要记日志）。
- Release 模式 `FilterLevel = Warning`，Debug/Info 日志被过滤。
- 可运行时调整：`Log.FilterLevel = LogLevel.Info`。

---

## 5. 性能面板

### 5.1 预置 StatsPanel

预置性能面板（IMGUI OnGUI），显示：

| 指标 | 来源 |
|------|------|
| FPS | `1f / Time.unscaledDeltaTime` |
| Frame Time | `Time.unscaledDeltaTime * 1000` ms |
| GC Memory | `System.GC.GetTotalMemory(false) / 1024 / 1024` MB |
| Unity Memory | `UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()` |
| Entity Count | `EntityManager.Count` |
| Draw Calls | `UnityStats.drawCalls`（编辑器） |
| Screen | `Screen.width x Screen.height` |

### 5.2 IDebugPanel 扩展接口

业务可注册自定义面板：

```
public interface IDebugPanel
{
    string Title { get; }         // 面板标题
    void OnGUI(Rect rect);        // 在指定区域内绘制（GUILayout）
}
```

注册：
```
DebugConsole.RegisterPanel(new PoolStatsPanel());
DebugConsole.RegisterPanel(new AudioStatsPanel());
DebugConsole.UnregisterPanel(panel);
```

业务示例：
```
public class PoolStatsPanel : IDebugPanel
{
    public string Title => "对象池状态";

    public void OnGUI(Rect rect)
    {
        GUILayout.BeginArea(rect);
        GUILayout.Label($"PoolMgr 池数: {GameGlobal.PoolMgr.PoolCount}");
        foreach (var info in GameGlobal.PoolMgr.GetPoolInfos())
            GUILayout.Label($"  {info.Key}: {info.ActiveCount}/{info.TotalCount}");
        GUILayout.EndArea();
    }
}
```

### 5.3 面板布局

- 性能面板用 IMGUI 绘制，叠加在屏幕角落（可拖动）。
- 多个面板可切换显示（Tab 键或点击标题）。
- 可全局开关。

---

## 6. Gizmos 注册系统

### 6.1 IGizmosDrawable 接口

```
public interface IGizmosDrawable
{
    void DrawGizmos();
}
```

### 6.2 注册

```
DebugConsole.RegisterGizmos(enemyVisionDrawer);
DebugConsole.UnregisterGizmos(enemyVisionDrawer);
```

### 6.3 GizmosDriver

框架提供 `GizmosDriver` MonoBehaviour（类似 LifecycleDriver）：
- 挂在 DontDestroyOnLoad 根节点。
- `OnDrawGizmos` 中遍历所有注册的 `IGizmosDrawable`，调用 `DrawGizmos()`。
- 可全局开关、按 tag 开关。
- **仅编辑器生效**（`OnDrawGizmos` 在打包后不调用）。

### 6.4 业务示例

```
public class EnemyVisionGizmos : IGizmosDrawable
{
    private readonly Enemy _enemy;

    public void DrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(_enemy.Position, _enemy.AgroRange);
    }
}
```

---

## 7. Console UI（uGUI）

### 7.1 面板内容

- **命令输入框**：TMP_InputField，输入命令 + 回车执行。
- **命令历史**：显示最近命令，上/下键浏览。
- **日志显示**：滚动显示 Log 输出，可按 Tag 过滤。
- **Tab 键**：自动补全命令名。

### 7.2 管理

- Console UI 走 UIManager（作为特殊 HUD 或 Top 层）。
- 打开时不暂停游戏（调试用）。
- 可半透明叠加。

### 7.3 真机适配

- 真机用触摸输入（TMP_InputField 支持触摸键盘）。
- 命令历史用上下滑动浏览。

---

## 8. 开关方式

### 8.1 编辑器

- 快捷键 `` ` ``（反引号，即 Tab 上方键）打开/关闭控制台。
- 可配（`DebugConsole.Config.ToggleKey`）。

### 8.2 真机

- 三指点击屏幕顶部打开/关闭控制台。
- 可配手势（`DebugConsole.Config.ToggleGesture`）。

### 8.3 代码开关

```
DebugConsole.Show();
DebugConsole.Hide();
DebugConsole.Toggle();
```

---

## 9. Release 模式处理

### 9.1 条件编译

```
#if DEBUG
    // DebugConsole 主体（UI/命令/Gizmos/面板）
    // GizmosDriver
    // 反射扫描
#endif
```

- Release 包完全剔除 DebugConsole 主体。
- 减小包体积、防作弊。

### 9.2 Log 类保留

- Log 类不用 `#if DEBUG`，始终保留。
- Release 模式 `FilterLevel = Warning`，自动过滤 Debug/Info。
- 业务代码中的 `Log.Debug(...)` 调用保留但被过滤（无输出）。

### 9.3 [ConsoleCommand] 标记

- 特性本身保留（特性不增加运行时开销）。
- 反射扫描在 `#if DEBUG` 内，Release 不扫描。
- Release 包中命令方法存在但不被注册/调用。

---

## 10. API 契约（公开接口）

### 10.1 DebugConsole

```
class DebugConsole : IModule
{
    // ── 命令 ──
    void Execute(string commandLine);           // 执行命令
    IReadOnlyList<CommandInfo> GetCommands();    // 所有已注册命令
    IReadOnlyList<string> GetCommandHistory();   // 命令历史

    // ── 面板 ──
    void RegisterPanel(IDebugPanel panel);
    void UnregisterPanel(IDebugPanel panel);

    // ── Gizmos ──
    void RegisterGizmos(IGizmosDrawable drawable);
    void UnregisterGizmos(IGizmosDrawable drawable);
    bool GizmosEnabled { get; set; }

    // ── 开关 ──
    void Show();
    void Hide();
    void Toggle();
    bool IsVisible { get; }

    // ── 配置 ──
    DebugConsoleConfig Config { get; set; }

    // ── IModule ──
    void Init();
    void Dispose();
}
```

### 10.2 Log

```
static class Log
{
    static LogLevel FilterLevel { get; set; }
    static void SetTagFilter(string tag, bool enabled);
    static void ClearTagFilters();

    static void Debug(string tag, string msg, Object context = null);
    static void Info(string tag, string msg, Object context = null);
    static void Warning(string tag, string msg, Object context = null);
    static void Error(string tag, string msg, Object context = null);
}
```

### 10.3 ConsoleCommandAttribute

```
[AttributeUsage(AttributeTargets.Method)]
class ConsoleCommandAttribute : Attribute
{
    string CommandName { get; }
    string Description { get; }
}
```

### 10.4 IDebugPanel

```
interface IDebugPanel
{
    string Title { get; }
    void OnGUI(Rect rect);
}
```

### 10.5 IGizmosDrawable

```
interface IGizmosDrawable
{
    void DrawGizmos();
}
```

### 10.6 CommandInfo

```
struct CommandInfo
{
    string Name;
    string Description;
    Type[] ParameterTypes;   // 参数类型列表
    string Usage;             // 用法字符串
}
```

---

## 11. 使用示例（伪代码）

### 11.1 业务实现作弊命令

```
public static class GameCheats
{
    [ConsoleCommand("add_gold", "添加金币")]
    public static void AddGold(int amount)
    {
        GameGlobal.GameData.Inventory.Gold.Value += amount;
        Log.Info("Cheat", $"添加 {amount} 金币");
    }

    [ConsoleCommand("set_hp", "设置血量")]
    public static void SetHp(float hp)
    {
        GameGlobal.GameData.Player.SetHp(hp);
    }

    [ConsoleCommand("god_mode", "无敌开关")]
    public static void GodMode()
    {
        var player = GameGlobal.GameData.Player;
        player.SetInvincible(!player.IsInvincible);
    }
}
```

### 11.2 日志使用

```
Log.Debug("Audio", "BGM 切换到 Battle");
Log.Info("Combat", "玩家受到 30 点伤害");
Log.Warning("Pool", "池已满，扩容");
Log.Error("Save", "存档损坏: slot 0");
```

### 11.3 注册自定义面板

```
public class MyCustomPanel : IDebugPanel
{
    public string Title => "自定义指标";

    public void OnGUI(Rect rect)
    {
        GUILayout.BeginArea(rect);
        GUILayout.Label($"当前关卡: {GameGlobal.LevelManager.CurrentLevelKey}");
        GUILayout.Label($"敌人数量: {GameGlobal.EntityManager.Count}");
        GUILayout.EndArea();
    }
}

GameGlobal.DebugConsole.RegisterPanel(new MyCustomPanel());
```

### 11.4 注册 Gizmos

```
public class EnemyVisionGizmos : IGizmosDrawable
{
    public void DrawGizmos()
    {
        foreach (var enemy in GameGlobal.EntityManager.Query<EnemyComponent>())
        {
            var pos = enemy.Get<MovementComponent>().Position;
            var range = enemy.Get<EnemyComponent>().AgroRange;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(pos, range);
        }
    }
}

GameGlobal.DebugConsole.RegisterGizmos(new EnemyVisionGizmos());
```

### 11.5 控制台交互

```
// 代码执行命令
GameGlobal.DebugConsole.Execute("add_gold 500");

// 打开控制台
GameGlobal.DebugConsole.Show();

// 查看所有命令
foreach (var cmd in GameGlobal.DebugConsole.GetCommands())
    Debug.Log($"{cmd.Name}: {cmd.Description} ({cmd.Usage})");
```

---

## 12. 实现检查清单

- [ ] `DebugConsole : IModule`，纳入 GameGlobal（`#if DEBUG`）
- [ ] `[ConsoleCommand]` 特性定义
- [ ] 反射扫描注册命令（启动时扫描所有程序集）
- [ ] 参数自动解析（int/float/string/bool）
- [ ] 命令执行 + 错误处理（参数不足/类型不匹配）
- [ ] 命令历史 + 自动补全
- [ ] `Log` 类（4 级 + Tag + 过滤），始终保留（非 `#if DEBUG`）
- [ ] Release 模式 `FilterLevel = Warning`
- [ ] `IDebugPanel` 接口 + 注册/注销
- [ ] 预置 `StatsPanel`（FPS/内存/实体数/DrawCall）
- [ ] `IGizmosDrawable` 接口 + 注册/注销
- [ ] `GizmosDriver` MonoBehaviour（OnDrawGizmos 统一调用）
- [ ] Console UI（uGUI，命令输入/历史/日志）
- [ ] 开关：快捷键 `` ` `` + 三指手势
- [ ] `#if DEBUG` 条件编译包裹主体
- [ ] `Dispose` 清理面板/Gizmos/订阅
- [ ] 所有公开 API 有中文 XML 注释
- [ ] 无硬编码（快捷键/手势走配置）

---

## 13. 依赖关系

| 依赖项 | 方向 | 说明 |
|--------|------|------|
| UIManager | 引用 | Console UI 面板 |
| EntityManager | 引用 | 实体数指标 |
| R3 | 引用 | 日志流（可选） |
| GameGlobal | 被引用 | 暴露 DebugConsole |

> **初始化顺序**：... → DebugConsole（最后，依赖其他模块的指标）
> DebugConsole 反射扫描命令需要所有业务程序集已加载。

---

## 14. 后续模块依赖本模块的接口

| 后续模块 | 使用的 DebugConsole 接口 |
|----------|------------------------|
| Editor Tools (4-2) | 编辑器菜单触发 DebugConsole 命令 |
| 业务层 | `[ConsoleCommand]` 作弊命令、`Log` 日志、`IDebugPanel` 自定义面板、`IGizmosDrawable` |

---

## 15. 关键使用规范（业务层 Coding AI 必读）

### 15.1 日志用 Log 类，不用 Debug.Log

- ❌ 禁止：`Debug.Log("msg")`
- ✅ 正确：`Log.Info("Tag", "msg")`

### 15.2 日志必须带 Tag

- ❌ 禁止：`Log.Info(null, "msg")`
- ✅ 正确：`Log.Info("Audio", "msg")`（Tag 便于过滤）

### 15.3 作弊命令用 [ConsoleCommand] 标记

- ❌ 禁止：`if (Input.GetKeyDown(KeyCode.F1)) CheatAddGold()`
- ✅ 正确：`[ConsoleCommand("add_gold")] public static void AddGold(int amount)`

### 15.4 调试代码用 #if DEBUG 包裹

- Gizmos 绘制、调试面板等用 `#if DEBUG` 包裹。
- Release 包不应包含调试代码。

### 15.5 性能面板可扩展

- 业务需要自定义指标时实现 `IDebugPanel` 注册。
- 不要修改框架预置 StatsPanel。

---

**文档结束。实现阶段请严格遵循本契约。**
