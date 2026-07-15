# Config Manager 模块功能设计指南

> **文档定位**：本文件是 Config Manager 模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、API 契约与数据结构，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的公开 API 语义**。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 |
|--------|------|
| 1. 访问方式 | 两者都提供：编辑器生成 `GameConfigs` 强类型访问类（默认推荐）+ 保留泛型 `Get<T>(key)` API（动态场景） |
| 2. 加载时机 | 混合：`[Config(preload: true)]` 标记的启动预加载，其余懒加载（首次访问时加载） |
| 3. Provider 泛型约束 | `T : class`，兼容 SO（`ScriptableObject`）和未来 Luban（普通 C# 类） |
| 4. 访问类生成方式 | 编辑器菜单生成（"Tools/PumpGF/Generate GameConfigs"）+ 编译后 `[DidReloadScripts]` 自动触发 + 保存 SO 时 `AssetPostprocessor` 自动触发 |
| 5. 校验机制 | 特性驱动（`[Range]`/`[Required]`）+ `IConfigValidator` 接口（复杂逻辑）两者都支持 |
| 6. 热重载 | 仅编辑器期；运行时不支持热重载（正式版应禁用，避免状态不一致） |
| 7. Luban 预留 | 只定义 `IConfigProvider` 接口 + `SOConfigProvider` 默认实现；Luban Provider 未来实现，文档说明接入方式 |
| 8. 配置形态 | 支持两种：单例配置（`[Config]`）与配置表（`[ConfigTable]` + `ConfigTableSO<TRow,TKey>` 基类） |
| 风格 | `ConfigMgr : IModule`，纳入 `GameGlobal`，纯 C# 类 |
| 底层 | SO 走 Addressables 加载（复用 ResMgr）；缓存于 ConfigMgr 内部 |
| 变更通知 | 配置重载时通过 EventBus 发布 `ConfigReloadedEvent` |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**Config Manager 是游戏配置的统一加载、缓存、校验与访问入口**——
通过 `IConfigProvider` 抽象隔离数据源（SO / 未来 Luban），
对外提供强类型访问，但**不负责配置的业务语义解释**（那是业务层的事）。

### 1.2 职责清单

| 职责 | 说明 |
|------|------|
| 配置加载与缓存 | 通过 `IConfigProvider` 加载，内部缓存，避免重复加载 |
| 强类型访问 | `[Config]`/`[ConfigTable]` 特性 + 编辑器生成 `GameConfigs` 静态访问类 |
| 泛型访问兜底 | `Get<T>(key)` 供动态场景使用 |
| 配置校验 | 特性驱动 + `IConfigValidator` 接口，编辑器菜单"校验所有配置" |
| 编辑器热重载 | SO 修改后重新加载缓存 + 发布变更事件 |
| 配置变更通知 | 通过 EventBus 发布 `ConfigReloadedEvent` |
| 多数据源预留 | `IConfigProvider` 抽象，SO 为默认，Luban 未来接入 |

### 1.3 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ 配置的业务语义解释 | 业务层自行解读配置字段含义 |
| ❌ 运行时热重载 | 风险大（状态/存档可能基于旧配置），仅编辑器期支持 |
| ❌ Luban 完整实现 | 本阶段仅预留 `IConfigProvider` 接口，Luban Provider 留空 |
| ❌ 配置加密 | Addressables 自身能力，不在 ConfigMgr 职责内 |
| ❌ 配置编辑器 GUI | Inspector 自身可编辑 SO；高级编辑器工具归 Phase 4 Editor Tools |
| ❌ 配置远程下发 | 未来需求，本阶段不做 |

---

## 2. 整体架构

```
┌─────────────────────────────────────────────────────────┐
│                   ConfigMgr (IModule)                      │
│                                                          │
│  ┌──────────────────┐   ┌───────────────────────────┐   │
│  │ 配置缓存表       │   │ 加载状态表                 │   │
│  │ key → object     │   │ key → LoadState            │   │
│  └──────────────────┘   └───────────────────────────┘   │
│                                                          │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐   │
│  │ IConfigProvider│ │ Validator    │  │ HotReload   │   │
│  │ (数据源抽象)  │  │ (校验)       │  │ (编辑器)    │   │
│  └──────────────┘  └──────────────┘  └──────────────┘   │
└──────────────────────────┬──────────────────────────────┘
                           │ 委托加载
                           ▼
┌─────────────────────────────────────────────────────────┐
│              IConfigProvider (抽象)                       │
│   ┌─────────────────────┐                                │
│   │ SOConfigProvider    │ ← 默认实现，走 ResMgr/Addressables│
│   └─────────────────────┘                                │
│   ┌─────────────────────┐                                │
│   │ LubanConfigProvider │ ← 未来实现（预留，本阶段留空）  │
│   └─────────────────────┘                                │
└─────────────────────────────────────────────────────────┘

        │ GameConfigs.Player / GameConfigs.Items.GetByKey(id)
        ▼
┌─────────────────┐
│   业务层访问     │
│ (强类型/IDE补全) │
└─────────────────┘

        │ ConfigReloadedEvent
        ▼
┌─────────────────┐
│   EventBus      │
└─────────────────┘
```

---

## 3. 两种配置形态

### 3.1 单例配置（Singleton Config）

全局唯一的配置，一个 SO = 一份配置，直接访问字段。
适用：玩家基础属性、全局平衡参数、游戏设置等。

```
[Config("PlayerConfig")]
public class PlayerConfigSO : ScriptableObject
{
    public int MaxHp = 100;
    public float MoveSpeed = 5f;
}

// 访问
int hp = GameConfigs.Player.MaxHp;
```

### 3.2 配置表（Config Table）

多行数据的集合，一个 SO = 一张表，按 key 索引取某一行。
适用：道具表、技能表、怪物表、关卡表等。

```
// 表行定义（纯数据类）
[Serializable]
public class ItemRow
{
    public int Id;
    public string Name;
    public int MaxStack;
    public Sprite Icon;
}

// 配置表 SO（继承基类获得索引能力）
[ConfigTable("ItemTable")]
public class ItemTableSO : ConfigTableSO<ItemRow, int>  // TRow=ItemRow, TKey=int
{
    protected override int GetKey(ItemRow row) => row.Id;
}

// 访问
ItemRow item = GameConfigs.Items.GetByKey(1001);
Debug.Log(item.Name);
```

### 3.3 ConfigTableSO\<TRow, TKey\> 基类

配置表 SO 继承此基类，自动获得索引构建与查询能力：

| 成员 | 说明 |
|------|------|
| `List<TRow> Rows` | 序列化的行数据（Inspector 可编辑） |
| `IReadOnlyList<TRow> AllRows` | 只读访问所有行 |
| `TRow GetByKey(TKey key)` | 按 key 查询行（未找到返回 null + Warning） |
| `int Count` | 行数 |
| `bool TryGetByKey(TKey key, out TRow row)` | 安全查询（不 Warning） |
| `protected abstract TKey GetKey(TRow row)` | 子类定义"如何从行取 key"（int/string/自定义） |

**索引构建时机**：配置加载后（`IConfigProvider` 返回后）由 ConfigMgr 调用 `BuildIndex()` 构建 `Dictionary<TKey, TRow>`。

> **设计要点**：`TKey` 用泛型，每张表自定义 key 类型（`int`/`string`/枚举等）。
> 这匹配"每个配置表索引不一样"的需求——有的按 id（int），有的按 name（string）。

### 3.4 IConfigTable 标记接口

非泛型标记接口，用于编辑器识别"这是配置表"（区别于单例配置）：

```
public interface IConfigTable { }  // 无成员，仅标记
```

`ConfigTableSO<TRow, TKey>` 实现此接口。编辑器据此分类校验。

---

## 4. IConfigProvider 抽象

### 4.1 接口定义

```
public interface IConfigProvider
{
    // 加载单个配置
    UniTask<T> LoadConfigAsync<T>(string key, CancellationToken ct) where T : class;

    // 预加载所有配置（启动时调用）
    UniTask PreloadAsync(IProgress<float> progress, CancellationToken ct);

    // 查询
    bool IsConfigLoaded(string key);

    // 卸载（通常由 ConfigMgr 管理，Provider 可选实现）
    void Unload(string key);
}
```

> **关键**：泛型约束 `T : class`（非 `ScriptableObject`），兼容未来 Luban 配置类。

### 4.2 SOConfigProvider（默认实现）

- 走 `ResMgr.LoadAssetAsync<T>(key)` 加载 SO。
- `PreloadAsync` 扫描所有 `[Config]`/`[ConfigTable]` 标记（通过反射或预生成的配置清单），对 `preload: true` 的执行预加载。
- `Unload` 调用 `ResMgr.UnloadAsset` —— 但由于 ResMgr 现在是引用计数（AssetHandle），ConfigMgr 内部持有 handle，Unload 时 Dispose handle。

### 4.3 LubanConfigProvider（未来实现，预留）

- `LoadConfigAsync<T>` 从 Luban 导出的数据（JSON/二进制）反序列化为 C# 类。
- `PreloadAsync` 加载所有 Luban 表。
- **本阶段不实现**，仅保证 `IConfigProvider` 接口能被替换。

### 4.4 Provider 切换

```
// 默认
ConfigMgr.SetProvider(new SOConfigProvider());

// 未来接入 Luban
ConfigMgr.SetProvider(new LubanConfigProvider());
```

- Provider 在 `ConfigMgr.Init()` 时设置默认（SOConfigProvider）。
- 业务可在启动早期替换为其他 Provider。
- 切换后清空缓存，重新加载。

---

## 5. 特性定义

### 5.1 [Config] 特性（单例配置）

```
[AttributeUsage(AttributeTargets.Class)]
public class ConfigAttribute : Attribute
{
    public string Key { get; }          // Addressables key
    public bool Preload { get; set; }   // 是否启动预加载（默认 false）

    public ConfigAttribute(string key) { Key = key; }
}
```

用法：
```
[Config("PlayerConfig")]                    // 懒加载
public class PlayerConfigSO : ScriptableObject { ... }

[Config("GameBalanceConfig", Preload = true)]  // 启动预加载
public class GameBalanceConfigSO : ScriptableObject { ... }
```

### 5.2 [ConfigTable] 特性（配置表）

```
[AttributeUsage(AttributeTargets.Class)]
public class ConfigTableAttribute : Attribute
{
    public string Key { get; }
    public bool Preload { get; set; }

    public ConfigTableAttribute(string key) { Key = key; }
}
```

用法：
```
[ConfigTable("ItemTable", Preload = true)]  // 道具表启动预加载
public class ItemTableSO : ConfigTableSO<ItemRow, int> { ... }
```

### 5.3 校验特性（驱动编辑器校验）

复用 Unity 自带 + 框架扩展：

| 特性 | 说明 |
|------|------|
| `[Range(min, max)]` | 数值范围（Unity 原生） |
| `[Required]` | 字段不可为 null/空（框架扩展） |
| `[NonNegative]` | 数值不可为负（框架扩展） |
| `[UniqueId]` | 配置表行的 key 唯一（框架扩展，用于校验重复 id） |

> 校验特性通过反射在编辑器期校验，不侵入运行时。

---

## 6. 强类型访问类生成

### 6.1 生成目标

生成 `GameConfigs.cs` 静态类：

```
// 自动生成，禁止手动编辑
public static class GameConfigs
{
    // 单例配置
    public static PlayerConfigSO Player
        => ConfigMgr.Get<PlayerConfigSO>("PlayerConfig");

    public static GameBalanceConfigSO GameBalance
        => ConfigMgr.Get<GameBalanceConfigSO>("GameBalanceConfig");

    // 配置表
    public static ItemTableSO Items
        => ConfigMgr.Get<ItemTableSO>("ItemTable");

    public static SkillTableSO Skills
        => ConfigMgr.Get<SkillTableSO>("SkillTable");
}
```

- 属性名从类名去掉 `ConfigSO`/`TableSO` 后缀生成（如 `PlayerConfigSO` → `Player`，`ItemTableSO` → `Items`）。
- 也可在特性里指定属性名：`[Config("PlayerConfig", Name = "Player")]`。

### 6.2 生成触发时机

| 触发 | 方式 |
|------|------|
| 手动 | 菜单 "Tools/PumpGF/Generate GameConfigs" |
| 编译后 | `[DidReloadScripts]` 自动扫描 `[Config]`/`[ConfigTable]` 重新生成 |
| 保存 SO | `AssetPostprocessor.OnPostprocessAllAssets` 检测 SO 变更后重新生成 |

### 6.3 生成文件位置

```
Assets/Scripts/Generated/GameConfigs.cs   // 或项目约定的 Generated 目录
```

- 生成文件头部标注"自动生成，禁止手动编辑"。
- 纳入版本控制（便于 CI 与团队协作）。

### 6.4 业务访问

```
// 单例配置
int hp = GameConfigs.Player.MaxHp;
float speed = GameConfigs.Player.MoveSpeed;

// 配置表
ItemRow item = GameConfigs.Items.GetByKey(1001);
foreach (var skill in GameConfigs.Skills.AllRows) { ... }

// 动态场景用泛型 API
var config = ConfigMgr.Get<PlayerConfigSO>("PlayerConfig");
```

---

## 7. 配置加载时机（混合策略）

### 7.1 预加载

- `[Config(preload: true)]` / `[ConfigTable(preload: true)]` 标记的配置，在 `ConfigMgr.Init()` 后调用 `PreloadAsync` 批量加载。
- 适用：核心平衡配置、频繁访问的表（道具表、技能表）。
- 进度通过 `IProgress<float>` 报告（可对接加载界面）。

### 7.2 懒加载

- 未标记 `preload` 的配置，首次 `Get<T>(key)` 时加载。
- 加载完成后缓存，后续访问直接命中缓存。
- 适用：低频访问的配置（如特定关卡配置）。

### 7.3 并发保护

- 同一 key 并发 `Get<T>` 时，第二个调用 await 同一个加载任务（类似 ResMgr 的并发保护）。
- 避免重复加载。

### 7.4 加载状态

| 状态 | 说明 |
|------|------|
| `NotLoaded` | 未加载 |
| `Loading` | 加载中 |
| `Loaded` | 已加载，缓存命中 |
| `Unloaded` | 已卸载 |

`Get<T>` 在 `Loading` 状态时 await 同一任务，在 `NotLoaded` 时发起新加载。

---

## 8. 配置校验

### 8.1 特性驱动校验

编辑器反射配置类字段上的校验特性：
- `[Range(0, 9999)]` → 数值越界报错
- `[Required]` → 字段为 null/空报错
- `[NonNegative]` → 负数报错
- `[UniqueId]` → 配置表行 key 重复报错

### 8.2 IConfigValidator 接口（复杂逻辑）

```
public interface IConfigValidator
{
    IReadOnlyList<string> Validate();  // 返回错误列表（空列表表示通过）
}
```

配置 SO 实现 `IConfigValidator`，编写复杂校验逻辑：
```
public class GameBalanceConfigSO : ScriptableObject, IConfigValidator
{
    public int MaxLevel;
    public int ExpPerLevel;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (MaxLevel <= 0) errors.Add("MaxLevel 必须 > 0");
        if (ExpPerLevel < MaxLevel) errors.Add("ExpPerLevel 不应小于 MaxLevel");
        return errors;
    }
}
```

### 8.3 校验入口

- 菜单 "Tools/PumpGF/Validate All Configs" —— 校验所有配置并输出报告。
- 校验失败时 Debug.LogError 列出所有错误。
- 可扩展为编辑器窗口（Phase 4 Editor Tools）。

### 8.4 运行时基本校验

- 加载时检查 `null`（配置不存在）、类型不匹配。
- 运行时**不执行**特性/IConfigValidator 校验（性能考虑，校验在编辑器期完成）。

---

## 9. 编辑器热重载

### 9.1 触发

- 编辑器期监听 SO 修改（`AssetPostprocessor` 或 `EditorApplication` 事件）。
- 检测到已缓存的配置 SO 变更 → 重新加载 → 更新缓存。

### 9.2 变更通知

热重载后通过 EventBus 发布：

```
public readonly struct ConfigReloadedEvent
{
    public readonly string Key;        // 重载的配置 key
    public readonly Type ConfigType;   // 配置类型
}
```

业务订阅刷新：
```
EventBus.OnEvent<ConfigReloadedEvent>()
    .Where(e => e.ConfigType == typeof(PlayerConfigSO))
    .Subscribe(_ => RefreshPlayerUI())
    .AddTo(this);
```

### 9.3 配置表索引重建

热重载配置表后，自动调用 `BuildIndex()` 重建索引。

### 9.4 仅编辑器

- `#if UNITY_EDITOR` 包裹热重载逻辑。
- 正式版打包不包含热重载代码（编译剔除）。

---

## 9. API 契约（公开接口）

> 以下为**公开 API 契约**，实现时方法签名必须一致。

### 9.1 ConfigMgr

```
// 配置访问（泛型，动态场景）
T Get<T>(string key) where T : class;                          // 懒加载（同步，已加载则直接返回）
UniTask<T> GetAsync<T>(string key, CancellationToken ct = default) where T : class;  // 异步加载

// 预加载
UniTask PreloadAsync(IProgress<float> progress = null, CancellationToken ct = default);

// 查询
bool IsLoaded(string key);
int LoadedCount { get; }

// 热重载（编辑器）
void Reload(string key);
void ReloadAll();

// Provider 切换
void SetProvider(IConfigProvider provider);

// IModule
void Init();
void Dispose();
```

### 9.2 IConfigProvider

```
UniTask<T> LoadConfigAsync<T>(string key, CancellationToken ct) where T : class;
UniTask PreloadAsync(IProgress<float> progress, CancellationToken ct);
bool IsConfigLoaded(string key);
void Unload(string key);
```

### 9.3 ConfigTableSO\<TRow, TKey\>

```
abstract class ConfigTableSO<TRow, TKey> : ScriptableObject, IConfigTable
    where TRow : class
{
    List<TRow> Rows;  // [SerializeField]
    IReadOnlyList<TRow> AllRows { get; }
    int Count { get; }
    TRow GetByKey(TKey key);
    bool TryGetByKey(TKey key, out TRow row);
    protected abstract TKey GetKey(TRow row);
    internal void BuildIndex();  // 加载后由 ConfigMgr 调用
}
```

### 9.4 生成访问类

```
// 自动生成
static class GameConfigs
{
    static T Get<T>(string key) where T : class => ConfigMgr.Get<T>(key);
    // 各配置属性...
}
```

### 9.5 IConfigValidator

```
interface IConfigValidator
{
    IReadOnlyList<string> Validate();
}
```

---

## 10. 使用示例（伪代码，非最终实现）

### 10.1 定义单例配置

```
[Config("PlayerConfig", Preload = true)]
public class PlayerConfigSO : ScriptableObject, IConfigValidator
{
    [Range(1, 9999)] public int MaxHp = 100;
    [Range(0.1f, 50f)] public float MoveSpeed = 5f;
    [Required] public RuntimeAnimatorController Animator;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (MoveSpeed > 10f && MaxHp < 50)
            errors.Add("移速过高时 MaxHp 不应过低");
        return errors;
    }
}
```

### 10.2 定义配置表

```
[Serializable]
public class ItemRow
{
    [UniqueId] public int Id;
    [Required] public string Name;
    [NonNegative] public int MaxStack;
    public Sprite Icon;
}

[ConfigTable("ItemTable", Preload = true)]
public class ItemTableSO : ConfigTableSO<ItemRow, int>
{
    protected override int GetKey(ItemRow row) => row.Id;
}
```

### 10.3 业务访问

```
// 单例配置（强类型）
int hp = GameConfigs.Player.MaxHp;

// 配置表（按 id 索引）
ItemRow item = GameConfigs.Items.GetByKey(1001);
if (item != null) Debug.Log($"道具: {item.Name}");

// 配置表（遍历）
foreach (var row in GameConfigs.Items.AllRows)
{
    if (row.MaxStack > 99) Debug.Log($"{row.Name} 可堆叠");
}

// 配置表（安全查询）
if (GameConfigs.Items.TryGetByKey(9999, out var item2))
    UseItem(item2);

// 动态场景（泛型）
var config = await ConfigMgr.GetAsync<SkillTableSO>("SkillTable", ct);
```

### 10.4 监听配置重载

```
EventBus.OnEvent<ConfigReloadedEvent>()
    .Where(e => e.ConfigType == typeof(PlayerConfigSO))
    .Subscribe(_ => RefreshPlayerUI())
    .AddTo(this);
```

### 10.5 启动预加载（带进度）

```
await ConfigMgr.PreloadAsync(
    progress: Progress.Create(p => loadingBar.Value = p),
    ct: destroyCancellationToken);

// 预加载完成，GameConfigs 可直接访问
int hp = GameConfigs.Player.MaxHp;
```

### 10.6 校验所有配置（编辑器菜单）

```
// 菜单触发
[MenuItem("Tools/PumpGF/Validate All Configs")]
static void ValidateAll()
{
    var errors = ConfigMgrEditor.ValidateAllConfigs();
    if (errors.Count == 0) Debug.Log("✓ 所有配置校验通过");
    else foreach (var e in errors) Debug.LogError(e);
}
```

---

## 11. 实现检查清单

实现完成后，逐项自查：

- [ ] `ConfigMgr : IModule`，纳入 GameGlobal
- [ ] `IConfigProvider` 接口定义，`T : class` 约束
- [ ] `SOConfigProvider` 默认实现，走 `ResMgr.LoadAssetAsync`
- [ ] `LubanConfigProvider` 留空（注释说明未来实现）
- [ ] `[Config]` / `[ConfigTable]` 特性定义，含 `Key` / `Preload` / `Name`
- [ ] `ConfigTableSO<TRow, TKey>` 基类，含 `GetByKey`/`TryGetByKey`/`AllRows`/`BuildIndex`
- [ ] `IConfigTable` 标记接口
- [ ] `IConfigValidator` 接口
- [ ] 校验特性 `[Required]`/`[NonNegative]`/`[UniqueId]` 定义
- [ ] `GameConfigs.cs` 生成器（菜单 + `[DidReloadScripts]` + `AssetPostprocessor`）
- [ ] 生成文件标注"自动生成，禁止手动编辑"
- [ ] 混合加载：`Preload` 标记的启动预加载，其余懒加载
- [ ] 并发保护：同 key 并发 Get 共享同一加载任务
- [ ] 加载状态机：NotLoaded/Loading/Loaded/Unloaded
- [ ] 配置表 `BuildIndex` 在加载后自动调用
- [ ] 编辑器热重载（`#if UNITY_EDITOR`）
- [ ] 热重载后发布 `ConfigReloadedEvent`
- [ ] 热重载配置表后重建索引
- [ ] 校验菜单 "Tools/PumpGF/Validate All Configs"
- [ ] `Get<T>(null key)` / `Get<不存在的 key>` 有明确异常/Warning
- [ ] `Dispose` 清理缓存与 Provider
- [ ] 所有公开 API 有中文 XML 注释
- [ ] 无硬编码（配置 key 走特性，不写死字符串）

---

## 12. 依赖关系

| 依赖项 | 方向 | 说明 |
|--------|------|------|
| ResMgr | 引用 | `SOConfigProvider` 通过 `ResMgr.LoadAssetAsync` 加载 SO |
| EventBus | 引用 | 发布 `ConfigReloadedEvent` |
| R3 | 引用 | `ConfigReloadedEvent` 经 EventBus 走 R3 |
| UniTask | 引用 | 异步加载 |
| Addressables | 间接引用 | 经 ResMgr |
| GameGlobal | 被引用 | 暴露 ConfigMgr |

> **初始化顺序**：ConfigMgr 在 ResMgr 之后（依赖 ResMgr 加载 SO）。
> 建议顺序：Lifecycle → Pool → Res → Config → ...

---

## 13. 后续模块依赖本模块的接口

| 后续模块 | 使用的 Config 接口 |
|----------|-------------------|
| Save/Load System (1-4) | 读存档配置（槽位数、自动存档间隔） |
| Scheduler/Timer (1-5) | 读调度配置 |
| UI Framework (2) | 读 UI 配置（默认分辨率、锚点） |
| Audio Manager (2) | 读音频配置（音量上限、声道） |
| Input Manager (2) | 读输入配置（重映射默认值） |
| FSM/HSM (3-1) | 读状态机配置（SO 状态图） |
| Level/Scene Manager (3-3) | 读关卡配置表 |
| 业务层 | 道具表、技能表、怪物表、平衡配置等 |

---

## 14. Luban 接入说明（未来）

### 14.1 当前状态

本阶段**不实现** Luban，仅预留 `IConfigProvider` 接口。现阶段用 SO 完全满足 demo 需求。

### 14.2 未来接入步骤

1. 实现 `LubanConfigProvider : IConfigProvider`。
2. Luban 导出的配置类为普通 C# class（满足 `T : class`）。
3. `LoadConfigAsync<T>` 从 Luban 导出的 JSON/二进制反序列化。
4. `PreloadAsync` 加载所有 Luban 表。
5. 在 `ConfigMgr.Init` 中 `SetProvider(new LubanConfigProvider())` 切换。

### 14.3 注意事项

- Luban 配置类**不是 ScriptableObject**，无法在 Inspector 编辑（在 Excel 编辑）。
- 配置表若用 Luban，`ConfigTableSO<TRow, TKey>` 基类不适用（那是 SO 专属）。
  Luban 表应自行实现 `IConfigTable` 标记接口 + 索引逻辑，或框架提供 `ConfigTableBase<TRow, TKey>` 非基类版本（未来扩展）。
- `[Config]`/`[ConfigTable]` 特性仍可用于 Luban 配置类（标记 key + preload）。
- `GameConfigs` 生成器需扩展支持 Luban 配置类（扫描 `[Config]` 标记不区分 SO/Luban）。

---

## 15. 关键使用规范（业务层 Coding AI 必读）

> 本节是**强制规范**，业务层 Coding AI 必须严格遵守。

### 15.1 配置类必须标记特性

- 单例配置：`[Config("key")]`
- 配置表：`[ConfigTable("key")]`
- 未标记特性的配置类不会被 `GameConfigs` 生成器识别。

### 15.2 配置 key 必须与 Addressables key 一致

- `[Config("PlayerConfig")]` 的 `"PlayerConfig"` 必须是 Addressables 中该 SO 的 key。
- key 不一致会导致加载失败。

### 15.3 配置表必须继承 ConfigTableSO 并实现 GetKey

- ❌ 禁止：配置表 SO 不继承基类（无法获得索引能力）
- ✅ 正确：`public class ItemTableSO : ConfigTableSO<ItemRow, int>`

### 15.4 不要缓存 GetByKey 的结果

- `GetByKey` 每次查字典，已很快。
- 缓存行引用在热重载后会失效（指向旧行）。
- 若确需缓存，订阅 `ConfigReloadedEvent` 清理缓存。

### 15.5 禁止运行时修改配置实例

- 配置是只读数据，运行时修改会导致状态不一致。
- 需要可变状态用业务层自己的数据结构，从配置拷贝初值。

### 15.6 预加载的配置在启动后才能访问

- `Preload = true` 的配置在 `ConfigMgr.PreloadAsync` 完成后才可用。
- 在 `PreloadAsync` 完成前 `Get` 会触发懒加载（同步等待，可能卡顿）。
- 启动流程应 `await ConfigMgr.PreloadAsync()` 后再进入游戏。

---

**文档结束。实现阶段请严格遵循本契约。**
