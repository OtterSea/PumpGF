# GameDataStore 模块功能设计指南

> **文档定位**：本文件是 GameDataStore 模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、API 契约与数据结构，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的公开 API 语义**。
>
> **模块编号**：建议 1-4（新增），原 Save/Load System 顺延为 1-5。
> GameDataStore 必须在 Save/Load 之前实现（Save/Load 依赖其 `BuildSaveData`）。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 |
|--------|------|
| 1. 引入 GameDataStore | 作为独立模块，Save/Load 退化为纯持久化层 |
| 2. 数据修改模式 | 模式 A（ReactiveProperty 轻量）+ 模式 B（Command 严格）并存 |
| 3. Command 历史 | 只内存记录，不持久化（Debug 面板可查，重启清空） |
| 4. 数据域组织 | 按功能域划分（PlayerData/InventoryData/QuestData/SettingsData） |
| 5. 数据校验 | DataStore 层做校验，关键数据抛异常，宽松数据 Warning + 钳制 |
| 风格 | `GameDataStore : IModule`，纳入 `GameGlobal`，纯 C# 类 |
| 底层 | 基于 R3 ReactiveProperty / ReadOnlyReactiveProperty |
| 变更通知 | R3 自动通知（数据层）+ Command handler 内 EventBus.Publish（事件层） |
| 审计 | Debug 模式下自动记录所有 RP 变更 + Command 历史 |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**GameDataStore 是游戏运行时数据状态的中枢**——
所有可持久化数据集中于此，修改经统一入口，变更自动通知且可审计。
Save/Load System 在其之下负责纯持久化，二者正交。

### 1.2 为什么需要这一层

**痛点（来自实际开发反馈）：**
- 数据修改散落各处，无统一入口。
- 修改粒度混乱（有时整个 struct，有时单字段）。
- 不知道何时改了数据，难追溯。
- 缺少"服务器协议"式的约束。

**GameDataStore 的价值：用架构约束替代服务器约束——让框架强制规范。**

### 1.3 职责清单

| 职责 | 说明 |
|------|------|
| 数据集中持有 | 所有运行时可持久化数据集中在数据域中 |
| 统一修改入口 | 模式 A（ReactiveProperty）+ 模式 B（Command） |
| 数据校验 | 修改时校验合法性（Hp 不为负、金币不溢出等） |
| 变更通知 | ReactiveProperty 自动通知 + Command 内可发 EventBus 事件 |
| 审计日志 | 数据变更自动记录日志，Command 历史内存留存 |
| 存档对接 | `BuildSaveData`/`RestoreFromSaveData`，作为 Save/Load 唯一数据源 |

### 1.4 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ 序列化/文件读写 | Save/Load System 负责 |
| ❌ 版本迁移 | Save/Load System 负责 |
| ❌ 槽位管理 | Save/Load System 负责 |
| ❌ Command 历史持久化 | 只内存，Debug 可查，不写存档 |
| ❌ 事件溯源/回滚 | 过重，本阶段不做 |
| ❌ 业务规则判定 | DataStore 只管数据状态，不管"能不能攻击"等业务逻辑 |

---

## 2. 整体架构

```
┌─────────────────────────────────────────┐
│            业务逻辑层                    │
│  背包/技能/AI 通过 DataStore 读写数据    │
│  ┌──────────────┐  ┌──────────────────┐│
│  │ 模式 A: 直接 │  │ 模式 B: Command  ││
│  │ RP.Value =   │  │ Execute(cmd)     ││
│  └──────────────┘  └──────────────────┘│
└──────────────────┬──────────────────────┘
                   │ 唯一修改入口
                   ▼
┌─────────────────────────────────────────┐
│         GameDataStore (数据中枢)          │
│  ┌─────────┐ ┌──────────┐ ┌──────────┐  │
│  │PlayerData│ │Inventory│ │QuestData │  │
│  │(R3 RP)  │ │(R3 RP)  │ │(R3 RP)   │  │
│  └─────────┘ └──────────┘ └──────────┘  │
│  ┌──────────────────────────────────┐   │
│  │ CommandBus (模式 B)              │   │
│  │ Type → Handler + History(内存)   │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ AuditLogger (Debug 自动审计)     │   │
│  │ R3 Subscribe 记录所有变更        │   │
│  └──────────────────────────────────┘   │
└──────────────────┬──────────────────────┘
                   │ BuildSaveData / RestoreFromSaveData
                   ▼
┌─────────────────────────────────────────┐
│         Save/Load System (纯持久化)       │
└─────────────────────────────────────────┘
```

---

## 3. 数据域组织

### 3.1 按功能域划分

每个功能域是独立类，持有自己的 ReactiveProperty 数据：

```
public class PlayerData
{
    public ReactiveProperty<float> Hp { get; } = new(100);  // 示例，实际见 3.2 封装级别
    public ReactiveProperty<int> Level { get; } = new(1);
}

public class InventoryData
{
    public ReactiveProperty<int> Gold { get; } = new(0);
    public ReactiveDictionary<int, int> Items { get; } = new(); // itemId → count
}
```

GameDataStore 持有所有数据域：
```
class GameDataStore
{
    public PlayerData Player { get; } = new();
    public InventoryData Inventory { get; } = new();
    public QuestData Quest { get; } = new();
    public SettingsData Settings { get; } = new();
}
```

### 3.2 数据域的字段封装级别

按数据重要性分两级：

**关键数据（需校验，如 Hp/金币）：**
- ReactiveProperty 私有，公开只读 `ReadOnlyReactiveProperty`
- 修改通过公开方法（方法内校验）

```
public class PlayerData
{
    private readonly ReactiveProperty<float> _hp = new(100);
    public ReadOnlyReactiveProperty<float> Hp => _hp;

    public void SetHp(float value)
    {
        if (value < 0) throw new ArgumentException("Hp 不能为负");
        _hp.Value = value;
    }

    public void ModifyHp(float delta)
    {
        SetHp(Mathf.Max(0, _hp.Value + delta));
    }
}
```

**普通数据（无需校验，如音量/设置）：**
- ReactiveProperty 公开，直接改 Value

```
public class SettingsData
{
    public ReactiveProperty<float> BgmVolume { get; } = new(1f);
    public ReactiveProperty<float> SfxVolume { get; } = new(1f);
}
// 使用：gameData.Settings.BgmVolume.Value = 0.5f;
```

### 3.3 数据域初值

- 数据域构造时设置默认初值（如 Hp=100）。
- `RestoreFromSaveData` 时覆盖为存档值。
- 新游戏用默认初值。

---

## 4. 数据修改模式 A：ReactiveProperty 轻量模式

### 4.1 适用场景

- 高频/简单修改（UI 状态、设置项）
- 无需审计的操作
- 直接字段赋值

### 4.2 修改方式

```
// 普通数据：直接改 Value
gameData.Settings.BgmVolume.Value = 0.5f;

// 关键数据：通过方法（校验）
gameData.Player.SetHp(80);
gameData.Player.ModifyHp(-20);  // 扣 20 血
```

### 4.3 自动通知

ReactiveProperty.Value 变化自动通知所有订阅者（UI 等），无需手动 EventBus。

### 4.4 自动审计日志（Debug）

GameDataStore 初始化时给所有数据域的 ReactiveProperty 挂 Subscribe，记录变更日志：
```
[DataStore] Player.Hp: 100 → 80
[DataStore] Settings.BgmVolume: 1.0 → 0.5
```
- 仅 Debug 模式开启（`#if DEBUG` 或配置开关）。
- Release 关闭以省性能。

---

## 5. 数据修改模式 B：Command 严格模式

### 5.1 适用场景

- 需审计的操作（伤害结算、经济交易、任务进度）
- 需要显式操作记录的操作
- 参数完整、语义明确的操作

### 5.2 Command 定义

Command 是 `readonly struct`，实现 `IDataCommand`：

```
public interface IDataCommand { }

public readonly struct ModifyPlayerHpCommand : IDataCommand
{
    public readonly float Delta;
    public readonly string Reason;  // 可选，审计用
    public ModifyPlayerHpCommand(float delta, string reason = null)
    {
        Delta = delta;
        Reason = reason;
    }
}

public readonly struct AddItemCommand : IDataCommand
{
    public readonly int ItemId;
    public readonly int Count;
    public AddItemCommand(int itemId, int count) { ... }
}
```

### 5.3 注册与执行

```
// 启动时注册 handler
gameData.RegisterHandler<ModifyPlayerHpCommand>(cmd =>
{
    gameData.Player.ModifyHp(cmd.Delta);
    EventBus.Publish(new PlayerHpChangedEvent(cmd.Delta, cmd.Reason));
});

// 业务执行
gameData.Execute(new ModifyPlayerHpCommand(-30, "被怪物攻击"));
```

### 5.4 Command 特点

- 值类型，零分配
- 参数完整显式（类似服务器协议）
- 可携带审计信息（Reason）
- handler 内可校验 + 通知

### 5.5 与模式 A 的关系

- 模式 B 的 handler 内部用模式 A（调 `Player.ModifyHp`）。
- 模式 A 是数据"底座"，模式 B 是"有审计需求时的封装"。
- 业务自行选择：简单数据用 A，关键数据用 B。

---

## 6. 数据校验

### 6.1 校验位置

- **关键数据**：在数据域的公开方法内校验（如 `SetHp` 校验非负）。
- **Command handler**：在执行前校验（如 `AddItemCommand` 校验 itemId 有效、Count > 0）。

### 6.2 校验失败处理

| 数据类型 | 处理方式 |
|----------|----------|
| 严格数据（Hp/金币/等级） | 抛 `ArgumentException`，阻止非法修改 |
| 宽松数据（音量/设置） | Warning 日志 + 钳制到合法范围（如音量钳制到 0~1） |

### 6.3 全量校验（Debug）

```
IReadOnlyList<string> ValidateAll();
```
- Debug 面板/菜单触发，校验所有数据域。
- 返回错误列表，空列表表示全部通过。

---

## 7. 变更通知与审计日志

### 7.1 变更通知两层

| 层级 | 机制 | 用途 |
|------|------|------|
| 数据层 | ReactiveProperty 自动通知 | UI 绑定、局部刷新 |
| 事件层 | Command handler 内 EventBus.Publish | 跨模块通知、业务逻辑触发 |

### 7.2 审计日志

- Debug 模式下，所有 ReactiveProperty 变更自动记录日志。
- Command 执行自动记录（类型 + 参数 + 时间）。
- Release 关闭。

---

## 8. Command 历史（内存）

### 8.1 历史记录

```
struct CommandHistoryEntry
{
    Type CommandType;
    object Command;        // boxed，但仅 Debug
    DateTime Timestamp;
}
```
- 每次 Execute 记录一条。
- 内存 List，默认保留最近 1000 条（可配）。
- 超限丢弃最旧。

### 8.2 查询

```
IReadOnlyList<CommandHistoryEntry> GetCommandHistory();
```
- Debug 面板展示"最近的操作"。
- 排查"谁改了这个数据"。

### 8.3 不持久化

- 历史只内存，不写存档。
- 重启清空。
- 如需回滚/重放，未来扩展（本阶段不做）。

---

## 9. 与 Save/Load System 的对接

### 9.1 GameDataStore 作为唯一聚合者

GameDataStore 实现 `BuildSaveData`/`RestoreFromSaveData`，作为 Save/Load 的唯一数据源：

```
class GameDataStore
{
    public RootSaveData BuildSaveData()
    {
        return new RootSaveData
        {
            SchemaVersion = SaveSchema.CurrentVersion,
            Player = new PlayerSaveData
            {
                Hp = Player.Hp.Value,
                Level = Player.Level.Value,
            },
            Inventory = Inventory.BuildSaveData(),
            // ...
        };
    }

    public void RestoreFromSaveData(RootSaveData data)
    {
        if (data.Player != null)  // 兼容旧存档缺字段
        {
            Player.SetHp(data.Player.Hp);
            Player.SetLevel(data.Player.Level);
        }
        Inventory.RestoreFromSaveData(data.Inventory);
    }
}
```

### 9.2 Save/Load 的简化

- Save/Load 不再遍历 ISaveable。
- Save/Load 只对接 GameDataStore 一个对象。
- Save: `await saveMgr.SaveAsync(slot, gameData.BuildSaveData())`
- Load: `gameData.RestoreFromSaveData(await saveMgr.LoadAsync(slot))`

### 9.3 存档片段定义

存档片段（PlayerSaveData 等）是纯数据 DTO，与数据域一一对应：
```
[Serializable]
public class PlayerSaveData
{
    public float Hp;
    public int Level;
}
```
- 存档片段是序列化用的 DTO，不是运行时状态。
- 运行时状态在数据域（ReactiveProperty），存档片段是快照。

---

## 10. API 契约（公开接口）

### 10.1 GameDataStore

```
class GameDataStore : IModule
{
    // 数据域访问
    PlayerData Player { get; }
    InventoryData Inventory { get; }
    // ... 各数据域

    // Command 模式
    void RegisterHandler<TCommand>(Action<TCommand> handler)
        where TCommand : struct, IDataCommand;
    void Execute<TCommand>(TCommand command)
        where TCommand : struct, IDataCommand;
    bool HasHandler<TCommand>() where TCommand : struct, IDataCommand;

    // 存档对接
    RootSaveData BuildSaveData();
    void RestoreFromSaveData(RootSaveData data);

    // 校验
    IReadOnlyList<string> ValidateAll();

    // Command 历史（Debug）
    IReadOnlyList<CommandHistoryEntry> GetCommandHistory();
    void ClearCommandHistory();

    // IModule
    void Init();
    void Dispose();
}
```

### 10.2 IDataCommand

```
interface IDataCommand { }
```

### 10.3 数据域约定

```
// 关键数据域示例
class PlayerData
{
    ReadOnlyReactiveProperty<float> Hp { get; }      // 只读暴露
    void SetHp(float value);                          // 校验 + 设值
    void ModifyHp(float delta);
}

// 普通数据域示例
class SettingsData
{
    ReactiveProperty<float> BgmVolume { get; }        // 直接可写
}
```

---

## 11. 使用示例（伪代码）

### 11.1 定义数据域

```
public class PlayerData
{
    private readonly ReactiveProperty<float> _hp = new(100);
    public ReadOnlyReactiveProperty<float> Hp => _hp;

    private readonly ReactiveProperty<int> _level = new(1);
    public ReadOnlyReactiveProperty<int> Level => _level;

    public void SetHp(float value)
    {
        if (value < 0) throw new ArgumentException("Hp 不能为负");
        _hp.Value = value;
    }

    public void ModifyHp(float delta)
        => SetHp(Mathf.Max(0, _hp.Value + delta));

    public void SetLevel(int value)
    {
        if (value < 1) throw new ArgumentException("Level 不能 < 1");
        _level.Value = value;
    }
}
```

### 11.2 定义 Command

```
public readonly struct ModifyPlayerHpCommand : IDataCommand
{
    public readonly float Delta;
    public readonly string Reason;
    public ModifyPlayerHpCommand(float delta, string reason = null)
    {
        Delta = delta;
        Reason = reason;
    }
}
```

### 11.3 注册 Handler

```
void RegisterCommands()
{
    GameGlobal.GameData.RegisterHandler<ModifyPlayerHpCommand>(cmd =>
    {
        GameGlobal.GameData.Player.ModifyHp(cmd.Delta);
        GameGlobal.EventBus.Publish(new PlayerHpChangedEvent(cmd.Delta, cmd.Reason));
    });
}
```

### 11.4 业务使用

```
// 模式 A（简单修改）
GameGlobal.GameData.Settings.BgmVolume.Value = 0.5f;

// 模式 B（关键操作，如扣血）
GameGlobal.GameData.Execute(new ModifyPlayerHpCommand(-30, "被史莱姆攻击"));

// 订阅数据变化（UI 绑定）
GameGlobal.GameData.Player.Hp
    .Subscribe(hp => hpBar.Value = hp / 100f)
    .AddTo(this);

// 查询 Command 历史（Debug）
foreach (var entry in GameGlobal.GameData.GetCommandHistory())
    Debug.Log($"[{entry.Timestamp:HH:mm:ss}] {entry.CommandType.Name}");
```

### 11.5 存档对接

```
// 保存
var saveData = GameGlobal.GameData.BuildSaveData();
await GameGlobal.SaveMgr.SaveAsync(slotId, saveData, ct);

// 加载
var saveData = await GameGlobal.SaveMgr.LoadAsync(slotId, ct);
GameGlobal.GameData.RestoreFromSaveData(saveData);
```

---

## 12. 实现检查清单

- [ ] `GameDataStore : IModule`，纳入 GameGlobal
- [ ] 数据域按功能划分（PlayerData/InventoryData 等）
- [ ] 关键数据：私有 RP + 公开方法（校验）
- [ ] 普通数据：公开 RP（直接改）
- [ ] `IDataCommand` 接口，Command 为 `readonly struct`
- [ ] `RegisterHandler<TCommand>` / `Execute<TCommand>` / `HasHandler<TCommand>`
- [ ] Execute 记录 Command 历史（内存 List，限 1000 条，可配）
- [ ] Debug 模式下所有 RP 挂 Subscribe 记录审计日志
- [ ] `BuildSaveData` 聚合所有数据域为 RootSaveData
- [ ] `RestoreFromSaveData` 从 RootSaveData 恢复，兼容 null 片段
- [ ] `ValidateAll` 全量校验
- [ ] `GetCommandHistory` / `ClearCommandHistory`
- [ ] 数据域初值在构造时设置
- [ ] 所有公开 API 有中文 XML 注释
- [ ] 无硬编码（历史上限等走配置）

---

## 13. 依赖关系

| 依赖项 | 方向 | 说明 |
|--------|------|------|
| R3 | 引用 | ReactiveProperty/ReadOnlyReactiveProperty/ReactiveDictionary |
| EventBus | 引用 | Command handler 内发布变更事件 |
| GameGlobal | 被引用 | 暴露 GameDataStore |

> **初始化顺序**：Lifecycle → Pool → Res → Config → Event → **GameDataStore** → Save/Load → ...

---

## 14. 后续模块依赖本模块的接口

| 后续模块 | 使用的 GameDataStore 接口 |
|----------|--------------------------|
| Save/Load System (1-5) | `BuildSaveData` / `RestoreFromSaveData` |
| UI Framework (2) | 订阅 ReactiveProperty 绑定 UI |
| 业务层 | 通过数据域读写数据 + Execute Command |

---

## 15. 关键使用规范（业务层 Coding AI 必读）

### 15.1 运行时数据必须放在 GameDataStore 的数据域

- ❌ 禁止：业务自己持有可持久化数据（散落）
- ✅ 正确：数据放在 GameDataStore.PlayerData 等

### 15.2 关键数据用方法修改（校验）

- ❌ 禁止：关键数据字段公开可写（绕过校验）
- ✅ 正确：私有 RP + 公开方法

### 15.3 需审计的操作用 Command

- 伤害/经济/任务进度等用 `Execute(Command)`
- 简单 UI 状态用直接 `RP.Value =`

### 15.4 存档片段与数据域一一对应

- PlayerData ↔ PlayerSaveData
- 不要把多个域的数据混在一个存档片段

### 15.5 RestoreFromSaveData 要兼容 null

- 旧存档可能缺新字段，RestoreFromSaveData 时检查 null

### 15.6 不要在 Command handler 外直接改关键数据

- ❌ 禁止：`gameData.Player.SetHp(0)` 直接调（绕过审计）
- ✅ 正确：`gameData.Execute(new ModifyPlayerHpCommand(...))`

---

**文档结束。实现阶段请严格遵循本契约。**
