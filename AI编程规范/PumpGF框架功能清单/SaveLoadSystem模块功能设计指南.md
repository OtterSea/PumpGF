# Save/Load System 模块功能设计指南

> **文档定位**：本文件是 Save/Load System 模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、API 契约与数据结构，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的公开 API 语义**。
>
> **模块编号**：建议 1-5（原 1-5 Scheduler/Timer 顺延为 1-6）。
> **前置依赖**：GameDataStore（1-4）必须先实现，Save/Load 对接其 `BuildSaveData`/`RestoreFromSaveData`。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 |
|--------|------|
| 1. 序列化格式 | JSON 默认（调试友好）+ Binary 可切换，通过 `ISaveProvider` 切换 |
| 2. 存档文件组织 | 单一 `RootSaveData` 序列化为一个文件 + 单独 `meta.json` 元信息 |
| 3. Vector3 序列化 | 存档片段用 `float[]`（3 元素）+ 辅助转换方法，不引入额外依赖 |
| 4. 版本迁移 | 加载时自动检测版本，链式迁移到当前版本，迁移后重新保存 |
| 5. 数据聚合 | ~~ISaveable 遍历~~ → GameDataStore 作为唯一聚合者（`BuildSaveData`/`RestoreFromSaveData`） |
| 6. 原子写入 | `.tmp` + rename + `.bak` 备份，崩溃可回滚上一版 |
| 7. 加密 | 预留 `IEncryptor` 接口，默认 NoopEncryptor，未来可加 AES |
| 8. 触发时机 | Save/Load 只提供 `SaveAsync`/`LoadAsync` API，触发时机由业务决定 |
| 9. 槽位 | 固定多槽位（数量配置化），每槽位一个文件夹 |
| 风格 | `SaveMgr : IModule`，纳入 `GameGlobal`，纯 C# 类 |
| 底层 | 基于 UniTask 异步读写 |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**Save/Load System 是纯持久化层**——
只负责序列化、文件读写、版本迁移、槽位管理，
**不关心数据在内存里怎么被修改**（那是 GameDataStore 的职责）。

### 1.2 职责清单

| 职责 | 说明 |
|------|------|
| 序列化/反序列化 | 通过 `ISaveProvider` 切换 JSON/Binary |
| 文件读写 | 异步读写存档文件（UniTask） |
| 版本迁移 | 加载时检测 SchemaVersion，链式迁移到当前版本 |
| 多槽位管理 | 固定槽位数量（配置化），每槽位独立文件夹 |
| 元信息管理 | 槽位元信息（角色名、时间、进度）单独存储，加载列表时不读完整存档 |
| 原子写入 | `.tmp` + rename + `.bak` 备份，防写入崩溃损坏 |
| 加密预留 | `IEncryptor` 接口，默认不加密 |

### 1.3 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ 运行时数据状态管理 | GameDataStore 负责 |
| ❌ 数据修改入口/校验 | GameDataStore 负责 |
| ❌ 自动存档调度 | 业务层决定触发时机（Scheduler/场景钩子/事件） |
| ❌ 云存档 | 未来需求，本阶段不做 |
| ❌ 存档压缩 | 暂不做（Binary 已够紧凑），未来可加 |

---

## 2. 整体架构

```
┌─────────────────────────────────────────┐
│            GameDataStore (数据中枢)       │
│  BuildSaveData() / RestoreFromSaveData() │
└──────────────────┬──────────────────────┘
                   │ RootSaveData
                   ▼
┌─────────────────────────────────────────┐
│           SaveMgr (纯持久化)              │
│  ┌──────────────────────────────────┐   │
│  │ ISaveProvider (序列化抽象)        │   │
│  │  ├─ JsonSaveProvider (默认)       │   │
│  │  └─ BinarySaveProvider (可选)     │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ Migration (版本迁移)              │   │
│  │  v1→v2→v3 链式                   │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ SlotManager (多槽位)              │   │
│  │  Slot0/ Slot1/ Slot2/            │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ AtomicWriter (.tmp + rename + bak)│   │
│  └──────────────────────────────────┘   │
└──────────────────┬──────────────────────┘
                   │ 文件 IO
                   ▼
┌─────────────────────────────────────────┐
│         Application.persistentDataPath    │
└─────────────────────────────────────────┘
```

---

## 3. ISaveProvider 抽象

### 3.1 接口定义

```
public interface ISaveProvider
{
    byte[] Serialize<T>(T data);
    T Deserialize<T>(byte[] bytes);
}
```

- `JsonSaveProvider`（默认）：JSON 序列化，可读、调试友好。
- `BinarySaveProvider`（可选）：二进制序列化，紧凑、快。
- 切换：`SaveMgr.SetProvider(new BinarySaveProvider())`。

### 3.2 JSON 实现说明

- 用 `System.Text.Json`（Unity 2021.2+ 内置）或 `Newtonsoft.Json`。
- **不使用 Unity 原生 `JsonUtility`**（不支持 Dictionary、功能弱）。
- 需处理 `float[]` 与 `Vector3` 的转换（见 §5.3）。

### 3.3 Binary 实现说明

- 用 `System.Runtime.Serialization.Formatters.Binary` 或 MessagePack-CSharp。
- 本阶段可只实现 JSON，Binary 预留接口。

---

## 4. RootSaveData 与 SaveDataBuilder

### 4.1 RootSaveData 结构

```
[Serializable]
public class RootSaveData
{
    public int SchemaVersion;           // 存档版本号
    public PlayerSaveData Player;
    public InventorySaveData Inventory;
    public QuestSaveData Quest;
    public SettingsSaveData Settings;
    // ... 各模块存档片段，与 GameDataStore 数据域一一对应
}
```

- 各片段是纯数据 DTO，`[Serializable]`，字段为可序列化类型。
- `SchemaVersion` 标记存档版本，用于版本迁移。

### 4.2 存档片段示例

```
[Serializable]
public class PlayerSaveData
{
    public float Hp;
    public int Level;
    public float[] Position;  // Vector3 → float[]
}

[Serializable]
public class InventorySaveData
{
    public int Gold;
    public List<ItemEntry> Items;  // itemId + count 列表
}
```

### 4.3 SaveDataBuilder

Save/Load 不直接使用 Builder（GameDataStore 已负责聚合），但提供 Builder 供手动构造场景：

```
var saveData = SaveDataBuilder.Create()
    .WithPlayer(p => { p.Hp = 100; p.Level = 1; })
    .WithInventory(inv => inv.Gold = 500)
    .Build();
```

- 链式 API，强类型，防 AI 写错结构。
- `Create()` 自动设置 `SchemaVersion = SaveSchema.CurrentVersion`。
- 日常存档走 `GameDataStore.BuildSaveData()`，Builder 用于特殊场景（如默认新存档）。

### 4.4 Vector3 序列化

存档片段中 Vector3/Quaternion 用 `float[]` 存储：

```
// 存
saveData.Player.Position = new float[] { pos.x, pos.y, pos.z };

// 读
var pos = new Vector3(saveData.Player.Position[0], saveData.Player.Position[1], saveData.Player.Position[2]);
```

辅助扩展方法：
```
public static class SaveDataExtensions
{
    public static float[] ToFloatArray(this Vector3 v) => new[] { v.x, v.y, v.z };
    public static Vector3 ToVector3(this float[] arr) => new(arr[0], arr[1], arr[2]);
    // Quaternion 同理
}
```

---

## 5. 版本迁移

### 5.1 Schema 版本号

```
public static class SaveSchema
{
    public const int CurrentVersion = 1;  // 当前存档版本，每次结构变更 +1
}
```

- 存档结构变化时（加字段、删字段、改类型），`CurrentVersion` +1。
- 旧存档 `SchemaVersion < CurrentVersion` 时触发迁移。

### 5.2 迁移注册

```
SaveMgr.RegisterMigration(1, 2, data =>
{
    // v1 → v2：给 Player 加 Mp 字段
    data.Player.Mp = 100;
});

SaveMgr.RegisterMigration(2, 3, data =>
{
    // v2 → v3：重命名 Level → PlayerLevel
    data.Player.PlayerLevel = data.Player.Level;
});
```

- 迁移函数签名：`Action<RootSaveData>`，原地修改。
- 链式执行：v1→v2→v3→...直到当前版本。

### 5.3 加载时自动迁移

```
LoadAsync(slotId, ct):
  1. 读文件 → 反序列化为 RootSaveData
  2. 检测 data.SchemaVersion
  3. while (data.SchemaVersion < SaveSchema.CurrentVersion):
       找到 (from=data.SchemaVersion) 的迁移函数
       执行迁移
       data.SchemaVersion = to
  4. 迁移完成，重新保存（避免下次重复迁移）
  5. 返回迁移后的 data
```

### 5.4 迁移失败处理

- 迁移函数抛异常 → 保留 `.bak` 备份 → 抛异常通知业务。
- 业务可提示"存档损坏，是否恢复上一版"。

---

## 6. 多槽位管理

### 6.1 槽位结构

```
<persistentDataPath>/Saves/
  ├─ Slot0/
  │   ├─ save.dat          (序列化数据)
  │   ├─ meta.json         (元信息：角色名、时间、进度)
  │   └─ thumbnail.png     (缩略图，可选)
  ├─ Slot1/
  │   └─ ...
  └─ Slot2/
      └─ ...
```

- 槽位数量配置化（默认 3）。
- 每槽位独立文件夹，隔离清晰。

### 6.2 元信息（SaveSlotMeta）

```
[Serializable]
public class SaveSlotMeta
{
    public string CharacterName;    // 角色名
    public DateTime SaveTime;      // 保存时间
    public int Progress;            // 进度（如关卡数）
    public float PlayTime;          // 游玩时长
}
```

- 单独存为 `meta.json`（小文件，快速读取）。
- 加载槽位列表时只读 meta，不读完整 `save.dat`。

### 6.3 槽位 API

```
IReadOnlyList<SaveSlotMeta> GetSlotList();      // 获取所有槽位元信息
bool HasSave(int slotId);                       // 槽位是否有存档
void DeleteSave(int slotId);                     // 删除槽位存档
```

---

## 7. 原子写入与备份

### 7.1 写入流程

```
SaveAsync(slotId, data, ct):
  1. serialize data → bytes
  2. 写入 save.dat.tmp
  3. 校验写入完整性（长度 + 校验和）
  4. 若 save.dat 存在 → rename save.dat → save.dat.bak
  5. rename save.dat.tmp → save.dat
  6. 更新 meta.json
```

- 崩溃在步骤 2/3：`.tmp` 残留，`save.dat` 仍是上次完整版本。
- 崩溃在步骤 4/5：`save.dat.bak` 可恢复。
- 启动时清理 `.tmp` 残留。

### 7.2 备份恢复

```
bool TryRestoreBackup(int slotId);
```
- `save.dat` 损坏时，从 `save.dat.bak` 恢复。
- 业务可提示玩家"存档损坏，已恢复上一版"。

---

## 8. 加密预留

### 8.1 IEncryptor 接口

```
public interface IEncryptor
{
    byte[] Encrypt(byte[] data);
    byte[] Decrypt(byte[] data);
}
```

- `NoopEncryptor`（默认）：直接返回原数据，不加密。
- 未来可实现 `AesEncryptor` 等。
- Provider 持有 Encryptor，序列化后加密、反序列化前解密。

### 8.2 本阶段不实现

- 个人独立游戏防篡改需求低。
- 预留接口无成本，未来按需实现。

---

## 9. 与 GameDataStore 的对接

### 9.1 唯一数据源

Save/Load 只对接 GameDataStore，不遍历 ISaveable：

```
// 保存
var saveData = GameGlobal.GameData.BuildSaveData();
await GameGlobal.SaveMgr.SaveAsync(slotId, saveData, ct);

// 加载
var saveData = await GameGlobal.SaveMgr.LoadAsync(slotId, ct);
GameGlobal.GameData.RestoreFromSaveData(saveData);
```

### 9.2 职责切分

| GameDataStore | Save/Load System |
|---------------|-------------------|
| 持有运行时数据状态 | 持久化到文件 |
| BuildSaveData 聚合数据 | 序列化/反序列化 |
| RestoreFromSaveData 恢复数据 | 版本迁移 |
| 数据校验 | 原子写入/备份 |
| 变更通知 | 槽位管理 |

---

## 10. API 契约（公开接口）

### 10.1 SaveMgr

```
class SaveMgr : IModule
{
    // 存档读写
    UniTask SaveAsync(int slotId, RootSaveData data, CancellationToken ct = default);
    UniTask<RootSaveData> LoadAsync(int slotId, CancellationToken ct = default);

    // 槽位管理
    IReadOnlyList<SaveSlotMeta> GetSlotList();
    bool HasSave(int slotId);
    void DeleteSave(int slotId);
    int SlotCount { get; }  // 配置化的槽位数

    // 版本迁移
    void RegisterMigration(int fromVersion, int toVersion, Action<RootSaveData> migration);

    // 备份恢复
    bool TryRestoreBackup(int slotId);

    // Provider 切换
    void SetProvider(ISaveProvider provider);

    // 加密器
    void SetEncryptor(IEncryptor encryptor);

    // IModule
    void Init();
    void Dispose();
}
```

### 10.2 ISaveProvider

```
interface ISaveProvider
{
    byte[] Serialize<T>(T data);
    T Deserialize<T>(byte[] bytes);
}
```

### 10.3 IEncryptor

```
interface IEncryptor
{
    byte[] Encrypt(byte[] data);
    byte[] Decrypt(byte[] data);
}
```

### 10.4 SaveSlotMeta

```
[Serializable]
class SaveSlotMeta
{
    string CharacterName;
    DateTime SaveTime;
    int Progress;
    float PlayTime;
}
```

### 10.5 SaveSchema

```
static class SaveSchema
{
    const int CurrentVersion = 1;
}
```

---

## 11. 使用示例（伪代码）

### 11.1 基础存读档

```
// 保存
var saveData = GameGlobal.GameData.BuildSaveData();
await GameGlobal.SaveMgr.SaveAsync(0, saveData, ct);

// 加载
var saveData = await GameGlobal.SaveMgr.LoadAsync(0, ct);
GameGlobal.GameData.RestoreFromSaveData(saveData);
```

### 11.2 槽位列表（存档界面）

```
var slots = GameGlobal.SaveMgr.GetSlotList();
for (int i = 0; i < slots.Count; i++)
{
    if (GameGlobal.SaveMgr.HasSave(i))
    {
        var meta = slots[i];
        Debug.Log($"槽位 {i}: {meta.CharacterName} | {meta.SaveTime} | 进度 {meta.Progress}");
    }
    else
    {
        Debug.Log($"槽位 {i}: 空槽");
    }
}
```

### 11.3 版本迁移注册

```
void RegisterMigrations()
{
    // v1 → v2：加 Mp 字段
    GameGlobal.SaveMgr.RegisterMigration(1, 2, data =>
    {
        data.Player.Mp = 100;
    });

    // v2 → v3：Inventory 结构变更
    GameGlobal.SaveMgr.RegisterMigration(2, 3, data =>
    {
        // 旧 Items → 新 Items 格式
        data.Inventory.Items = data.Inventory.LegacyItems
            .Select(i => new ItemEntry { Id = i.ItemId, Count = i.Amount })
            .ToList();
    });
}
```

### 11.4 切换序列化格式

```
// 默认 JSON
GameGlobal.SaveMgr.SetProvider(new JsonSaveProvider());

// 切换为 Binary（紧凑）
GameGlobal.SaveMgr.SetProvider(new BinarySaveProvider());
```

### 11.5 备份恢复

```
if (!await GameGlobal.SaveMgr.TryLoadAsync(0))
{
    // 存档损坏，尝试恢复备份
    if (GameGlobal.SaveMgr.TryRestoreBackup(0))
    {
        Debug.LogWarning("存档损坏，已恢复上一版备份");
        var saveData = await GameGlobal.SaveMgr.LoadAsync(0);
        GameGlobal.GameData.RestoreFromSaveData(saveData);
    }
    else
    {
        Debug.LogError("存档损坏且无备份");
    }
}
```

### 11.6 用 SaveDataBuilder 创建默认新存档

```
var defaultSave = SaveDataBuilder.Create()
    .WithPlayer(p => { p.Hp = 100; p.Level = 1; p.Position = Vector3.zero.ToFloatArray(); })
    .WithInventory(inv => inv.Gold = 100)
    .Build();

await GameGlobal.SaveMgr.SaveAsync(slotId, defaultSave, ct);
```

---

## 12. 实现检查清单

- [ ] `SaveMgr : IModule`，纳入 GameGlobal
- [ ] `ISaveProvider` 接口，`JsonSaveProvider` 默认实现
- [ ] `BinarySaveProvider` 预留（可只留接口，实现可选）
- [ ] `RootSaveData` 结构定义，含 `SchemaVersion` + 各模块片段
- [ ] `SaveDataBuilder` 链式 API
- [ ] Vector3 ↔ `float[]` 辅助扩展方法
- [ ] `SaveSchema.CurrentVersion` 常量
- [ ] `RegisterMigration` + 链式迁移执行
- [ ] 加载时自动迁移 + 迁移后重保存
- [ ] 多槽位文件夹结构
- [ ] `SaveSlotMeta` 单独 `meta.json` 存储
- [ ] `GetSlotList` 只读 meta 不读完整存档
- [ ] 原子写入：`.tmp` + rename + `.bak` 备份
- [ ] 启动时清理 `.tmp` 残留
- [ ] `TryRestoreBackup` 从 `.bak` 恢复
- [ ] `IEncryptor` 接口 + `NoopEncryptor` 默认
- [ ] 与 GameDataStore 对接：`SaveAsync(slot, gameData.BuildSaveData())`
- [ ] `SaveAsync(null data)` / `LoadAsync(不存在槽位)` 有明确异常/返回
- [ ] 迁移函数不存在（缺中间版本）→ 抛异常
- [ ] `Dispose` 清理 Provider/Encryptor
- [ ] 所有公开 API 有中文 XML 注释
- [ ] 无硬编码（槽位数、路径走配置）

---

## 13. 依赖关系

| 依赖项 | 方向 | 说明 |
|--------|------|------|
| GameDataStore | 引用 | `BuildSaveData` / `RestoreFromSaveData` |
| UniTask | 引用 | 异步读写 |
| GameGlobal | 被引用 | 暴露 SaveMgr |

> **初始化顺序**：... → GameDataStore → **SaveMgr** → ...
> SaveMgr 依赖 GameDataStore（对接数据源），但 Save/Load 操作由业务主动调用，不依赖其他模块初始化。

> **注意**：SaveMgr 不依赖 EventBus/Lifecycle。
> 存档触发时机由业务决定（可订阅 Lifecycle 场景钩子 / EventBus 事件 / Scheduler 定时）。

---

## 14. 后续模块依赖本模块的接口

| 后续模块 | 使用的 SaveMgr 接口 |
|----------|---------------------|
| 业务层（存档界面） | `GetSlotList` / `HasSave` / `SaveAsync` / `LoadAsync` / `DeleteSave` |
| 业务层（自动存档） | `SaveAsync`（由 Scheduler/场景钩子触发） |
| Debug Console (4-1) | `TryRestoreBackup` / 迁移状态查询 |

---

## 15. 关键使用规范（业务层 Coding AI 必读）

### 15.1 存档数据必须走 GameDataStore

- ❌ 禁止：业务自己拼 RootSaveData 直接 `SaveAsync`
- ✅ 正确：`GameGlobal.GameData.BuildSaveData()` 聚合

### 15.2 版本变更必须更新 SchemaVersion 并注册迁移

- 存档结构变化时，`SaveSchema.CurrentVersion` +1。
- 必须注册从旧版本到新版本的迁移函数。
- 不注册迁移会导致旧存档加载失败。

### 15.3 Vector3 必须用 float[] 存储辅助方法

- ❌ 禁止：存档片段直接用 `Vector3` 字段（JSON 无法序列化）
- ✅ 正确：`float[] Position` + `ToFloatArray()`/`ToVector3()` 辅助

### 15.4 元信息必须与存档同步更新

- `SaveAsync` 时同步更新 `meta.json`。
- 不要单独写 meta 而不写存档（不一致）。

### 15.5 加载流程必须处理迁移失败

- `LoadAsync` 可能抛迁移异常。
- 业务应 try-catch 并提示玩家 + 尝试 `TryRestoreBackup`。

---

**文档结束。实现阶段请严格遵循本契约。**
