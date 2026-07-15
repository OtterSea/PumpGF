# ResMgr 资源加载模块功能设计指南

> **文档定位**：本文件是 ResMgr 模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、API 契约与数据结构，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的公开 API 语义**。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 |
|--------|------|
| 1. 引用计数 | 引入 `AssetHandle<T>` 抽象，彻底的引用计数方案，每次加载返回独立 handle，Dispose 减引用，归零才真正释放 |
| 2. 场景接口边界 | ResMgr 补全所有场景底层原语（含激活控制、Additive、卸载、进度），Level Manager 在其上构建流程，不直接碰 Addressables |
| 3. 非池化实例托管 | 用 `destroyCancellationToken` 监听 GameObject 销毁，自动清理非池化实例字典与对应 AssetHandle 引用 |
| 4. Resources 接口 | 删除 Resources 接口，统一 Addressables；业务层若确有需求可自行 `Resources.Load`（见 §11） |
| 5. 能力增强 | 实现单资源进度回调、标签批量加载、资源使用统计；重试/内存上限/加载优先级预留提及，暂不实现 |
| 6. 资源组预加载 | 实现 `PreloadByLabelAsync(label)` 按 Addressables Label 批量预加载 |
| 风格 | 纯 C# 类 `ResMgr : IModule`，与 PoolMgr/LifecycleMgr 一致 |
| 异步 | 全部 `UniTask` + `CancellationToken`，遵循框架规范 |
| 池集成 | `InstantiateAsync` 优先走 PoolMgr，无池才直接实例化 |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**ResMgr 是资源加载的统一入口与生命周期托管者**——
所有 Addressables 资源的加载、引用计数、卸载、场景底层原语、预加载都经此调度，
但**不负责资源加载的业务流程编排**（如加载界面、过渡动画、场景切换逻辑）。

### 1.2 职责清单

| 职责 | 说明 |
|------|------|
| 资产加载与引用计数 | `AssetHandle<T>` 抽象，多次加载同一 key 共享底层 handle，各自持有独立引用计数 |
| GameObject 实例化 | `InstantiateAsync`，优先走池，非池化实例自动托管生命周期 |
| 场景底层原语 | 加载/卸载/Additive/激活控制/进度，不含流程编排 |
| 批量预加载 | 按 key 列表 / 按 Addressables Label 预加载 |
| 资源使用统计 | Debug 工具：查询已加载资产的引用计数与占用 |
| 引用安全释放 | 非池化实例销毁时自动清理引用，防止泄漏与野指针 |

### 1.3 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ 加载界面 / 进度条 UI | 由 Phase 2 UI Framework 负责，ResMgr 只暴露进度数据 |
| ❌ 场景切换流程编排（过渡动画、激活时机决策） | 由 Phase 3 Level/Scene Manager 负责 |
| ❌ 资源加密 / 解密 | Addressables 自身能力，不在 ResMgr 职责内 |
| ❌ Resources.Load 接口 | 框架禁用 Resources，业务确有需求自行调用（见 §11） |
| ❌ 内存上限自动卸载 | 暂不实现，预留接口（见 §10） |
| ❌ 加载失败自动重试 | 暂不实现，业务用 UniTask 自行重试（见 §10） |
| ❌ 加载优先级调度 | 暂不实现，预留（见 §10） |

---

## 2. 整体架构

```
┌─────────────────────────────────────────────────────────┐
│                     ResMgr (IModule)                      │
│                                                          │
│  ┌──────────────────┐   ┌───────────────────────────┐   │
│  │ AssetEntry 表    │   │ InstanceTracker 表         │   │
│  │ key → AssetEntry │   │ instance → (entry, mode)   │   │
│  │ (引用计数核心)    │   │ (实例生命周期托管)         │   │
│  └────────┬─────────┘   └─────────────┬─────────────┘   │
│           │                            │                  │
│  ┌────────▼─────────┐   ┌──────────────▼──────────┐    │
│  │ AssetHandle<T>   │   │ destroyCancellationToken │    │
│  │ (调用方持有)      │   │ 自动清理监听              │    │
│  └──────────────────┘   └───────────────────────────┘   │
│                                                          │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │ SceneLoader  │  │ Preloader    │  │ StatsReport │  │
│  │ (场景原语)   │  │ (预加载/标签) │  │ (使用统计)  │  │
│  └──────────────┘  └──────────────┘  └──────────────┘  │
└──────────────────────────┬──────────────────────────────┘
                           │ Addressables API
                           ▼
┌─────────────────────────────────────────────────────────┐
│              UnityEngine.AddressableAssets               │
└─────────────────────────────────────────────────────────┘

        │ InstantiateAsync 优先查询
        ▼
┌─────────────────┐
│     PoolMgr     │
│  (对象池)       │
└─────────────────┘
```

---

## 3. 核心设计：引用计数与 AssetHandle

### 3.1 为什么需要引用计数

现有"共享 handle + 直接 UnloadAsset(key)"的设计有致命缺陷：
- 模块 A 加载 "Enemy"，模块 B 也加载 "Enemy"（共享同一 handle）。
- 模块 A 调用 `UnloadAsset("Enemy")` → handle 被释放、从字典移除。
- 模块 B 持有的引用变成**已释放的 handle.Result** → 野指针、崩溃、数据错乱。

Addressables 官方设计就是引用计数的（每次 `LoadAssetAsync` 返回独立 handle，需各自 Release）。
当前 ResMgr 用"共享 handle"绕开了它，却丢失了引用计数语义。本设计彻底修正。

### 3.2 AssetEntry（内部引用计数条目）

每个 key 对应一个 `AssetEntry`，维护引用计数与底层 Addressables handle：

| 字段 | 类型 | 说明 |
|------|------|------|
| `Key` | string | Addressables key |
| `Handle` | `AsyncOperationHandle` | 底层 Addressables handle（共享） |
| `RefCount` | int | 引用计数，每次 LoadAssetAsync +1，每次 AssetHandle.Dispose -1 |
| `LoadType` | Type | 加载时指定的类型（校验用） |

**引用计数语义：**
- `RefCount == 0` → 调用 `Addressables.Release(Handle)`，从 `_entries` 移除。
- `RefCount > 0` → handle 保持活跃，新加载直接复用并 `RefCount++`。

### 3.3 AssetHandle\<T>（调用方持有的引用票据）

`AssetHandle<T>` 是**调用方持有的引用票据**，类型安全，`IDisposable`。

| 成员 | 说明 |
|------|------|
| `T Asset` | 获取加载的资源（若已 Dispose 返回 null 并 Warning） |
| `bool IsValid` | 引用是否仍有效（未 Dispose 且底层未释放） |
| `string Key` | 对应的 Addressables key |
| `void Dispose()` | 减引用计数，归零则真正释放底层 handle（幂等，多次调用安全） |

**关键约束：**
- `AssetHandle<T>` **不可被复制传递**。若模块 B 需要模块 A 的资源，B 必须自行 `LoadAssetAsync`（`RefCount++`），**禁止**拿 A 的 handle 传递。
- 原因：传递后双方都 Dispose 会导致重复减引用；只一方 Dispose 会导致另一方持有过期引用。
- 文档与示例中必须强调此约束，确保业务层 Coding AI 正确使用。

### 3.4 加载流程

```
LoadAssetAsync<T>(key, ct):
  1. await EnsureAddressablesInitialized(ct)
  2. 若 _entries 含 key:
       entry.AddRef()
       返回 new AssetHandle<T>(entry)
  3. 否则:
       handle = Addressables.LoadAssetAsync<T>(key)
       _entries[key] = new Entry(handle)  // 先注册防止并发重复加载
       await handle.ToUniTask(ct)
       失败 → 移除 entry, 抛异常
       成功 → entry.AddRef(), 返回 new AssetHandle<T>(entry)
```

### 3.5 卸载流程

```
AssetHandle<T>.Dispose():
  1. 若已 disposed → return（幂等）
  2. entry.Release()
       RefCount--
       若 RefCount == 0:
         Addressables.Release(entry.Handle)
         _entries.Remove(entry.Key)
  3. 标记 disposed
```

### 3.6 并发加载保护

并发 `LoadAssetAsync` 同一 key 时：
- 第一个调用注册 entry（`RefCount=0`，handle 已创建但未 await 完成）。
- 第二个调用发现 entry 已存在，`AddRef()` 并 await 同一 handle。
- 两个调用各自返回独立的 `AssetHandle<T>`，引用计数正确。

> 实现注意：entry 注册与 handle 创建需原子化，避免并发重复 `Addressables.LoadAssetAsync`。
> 推荐用 `Dictionary` + handle 本身的 `IsDone` 状态判断，或用 `UniTask` 缓存加载任务。

---

## 4. GameObject 实例化与非池化实例托管

### 4.1 InstantiateAsync 设计

`InstantiateAsync` 返回 `GameObject`，调用方用 `Release(instance)` 释放。

```
InstantiateAsync(key, position, rotation, ct):
  1. handle = await LoadAssetAsync<GameObject>(key, ct)  // 内部持有一个 AssetHandle
  2. 若 PoolMgr.HasPool(key):
       instance = PoolMgr.Get(key, position, rotation)
       记录 instance → 模式=Pool
  3. 否则:
       instance = Object.Instantiate(handle.Asset, position, rotation)
       记录 instance → (handle, 模式=Direct)
       绑定 instance.destroyCancellationToken → instance 销毁时自动清理
  4. 返回 instance
```

### 4.2 Release 语义

```
Release(instance):
  1. 查询 InstanceTracker:
     - 模式=Pool → PoolMgr.Release(instance)（回池，不动 AssetHandle，池自管）
     - 模式=Direct → Object.Destroy(instance) + AssetHandle.Dispose()
  2. 若 instance 不在 tracker → Warning + Object.Destroy（兜底）
```

### 4.3 destroyCancellationToken 自动清理（防泄漏）

非池化实例注册时绑定 `instance.destroyCancellationToken`：
- 若调用方**直接 `Object.Destroy(instance)` 而未调用 `Release`**：
  - `destroyCancellationToken` 触发 → 自动从 `InstanceTracker` 移除。
  - 自动 `AssetHandle.Dispose()` → 引用计数正确释放。
  - 调用方无需手动 Release（但推荐显式 Release 以表达意图）。

**这是防泄漏的关键设计**——即使业务层忘记 Release，资源引用也会随实例销毁自动释放。

### 4.4 InstanceTracker 数据结构

| 字段 | 类型 | 说明 |
|------|------|------|
| `Instance` | `GameObject` | 实例 |
| `Source` | enum { `Pool`, `Direct` } | 来源 |
| `AssetHandle` | `AssetHandle<GameObject>` | 仅 Direct 模式持有（Pool 模式为 null，池自管） |
| `UnregisterToken` | `CancellationTokenRegistration` | destroyCancellationToken 监听注销句柄 |

---

## 5. 场景底层原语

### 5.1 设计原则

ResMgr 提供**场景底层原语**，不编排流程。Level/Scene Manager（Phase 3）在其上构建：
- 加载界面、进度条、过渡动画。
- "加载完不激活，等过渡动画播完再激活"的流程。
- 多场景叠加管理。

### 5.2 SceneHandle 抽象

加载场景返回 `SceneHandle`，与 `AssetHandle` 类似但针对场景：

| 成员 | 说明 |
|------|------|
| `Scene Scene` | 加载的场景（激活后可用） |
| `float Progress` | 加载进度 0~1 |
| `bool IsActivated` | 是否已激活 |
| `void Activate()` | 激活场景（仅 `activateOnLoad=false` 时需要） |
| `UniTask UnloadAsync(CancellationToken)` | 卸载场景 |
| `void Dispose()` | 等同 Unload（若未激活则直接释放） |

### 5.3 场景加载 API

```
UniTask<SceneHandle> LoadSceneAsync(
    string key,
    LoadSceneMode mode = LoadSceneMode.Single,
    bool activateOnLoad = true,
    IProgress<float> progress = null,
    CancellationToken ct = default)
```

- `mode`：`Single`（替换）或 `Additive`（叠加）。
- `activateOnLoad`：`false` 时加载完不激活，由调用方 `SceneHandle.Activate()` 控制激活时机（用于过渡动画）。
- `progress`：加载进度回调。
- 返回 `SceneHandle`，调用方持有，用完后 `UnloadAsync` 或 `Dispose`。

### 5.4 场景卸载 API

- `SceneHandle.UnloadAsync(ct)`：卸载场景，释放底层 Addressables scene handle。
- `SceneHandle.Dispose()`：若未激活直接释放；若已激活则卸载。
- 场景 handle 同样进引用计数管理？**场景通常不共享**，一个场景一个 handle，`UnloadAsync` 直接释放。不引入场景引用计数（避免过度设计）。

### 5.5 场景事件与 Lifecycle 的关系

- ResMgr 的场景加载/卸载**不直接触发** Lifecycle 的场景钩子事件。
- Unity 的 `SceneManager.sceneLoaded`/`sceneUnloaded` 事件由 Lifecycle 监听并转发。
- ResMgr 调用 `Addressables.LoadSceneAsync` 后，Unity 内部触发 `SceneManager` 事件 → Lifecycle 自动通知订阅者。
- 二者解耦：ResMgr 负责"加载"，Lifecycle 负责"通知"，无需 ResMgr 调用 Lifecycle。

---

## 6. 批量预加载

### 6.1 按 key 列表预加载

```
UniTask PreloadAsync(
    IReadOnlyList<string> keys,
    IProgress<float> progress = null,
    CancellationToken ct = default)
```

- 并行加载所有 key，统一进度报告（已完成数 / 总数）。
- 加载后各 handle 的 `RefCount=1`，由 ResMgr 内部持有直到 `UnloadAsset(key)` 或显式释放。
- **注意**：预加载的 handle 没有对应的 `AssetHandle<T>` 票据返回。调用方后续 `LoadAssetAsync` 同 key 时会 `RefCount++` 得到票据。预加载相当于"提前热身"。

### 6.2 按 Label 预加载（资源组）

```
UniTask PreloadByLabelAsync(
    string label,
    IProgress<float> progress = null,
    CancellationToken ct = default)
```

- 通过 `Addressables.LoadResourceLocationsAsync(label)` 获取该 Label 下所有资源 key。
- 逐个加载（或并行，可配并发数）。
- 适用场景：`"UI/Common"` 一次性预加载所有通用 UI；`"Audio/BGM"` 预加载所有背景音乐。

### 6.3 预加载的并发控制

- 并行加载过多资源会打爆磁盘 IO 与内存。提供 `LifecycleConfig` 配置 `PreloadConcurrency`（默认 8）。
- 用 `UniTask` 的 `WhenEach` 或 SemaphoreSlim 控制并发。

---

## 7. 单资源加载进度回调

```
UniTask<AssetHandle<T>> LoadAssetAsync<T>(
    string key,
    IProgress<float> progress = null,
    CancellationToken ct = default)
```

- `progress` 可选，加载过程中报告 0~1 进度。
- 内部监听 `handle.PercentComplete`。

---

## 8. 资源使用统计（Debug 工具）

### 8.1 统计 API

```
// 获取所有已加载资产信息（用于 Debug 面板/编辑器工具）
IReadOnlyList<AssetInfo> GetLoadedAssetsInfo();

// 已加载资产数量
int LoadedAssetCount { get; }

// 实例化对象数量（按模式分）
int PooledInstanceCount { get; }
int DirectInstanceCount { get; }
```

### 8.2 AssetInfo 结构

| 字段 | 类型 | 说明 |
|------|------|------|
| `Key` | string | Addressables key |
| `Type` | Type | 加载类型 |
| `RefCount` | int | 当前引用计数 |
| `Size` | long | 资源占用字节数（尽力而为，Addressables 不一定提供） |

### 8.3 用途

- 开发期 Debug 面板展示"哪些资源引用计数异常高"（泄漏排查）。
- "哪些资源从未被释放"（持续增长警告）。
- 不用于运行时性能逻辑（纯诊断）。

---

## 9. Addressables 初始化修复

### 9.1 现有缺陷

现有代码用 `bool _addressablesReady` 标志位，存在并发竞态：
- 第一个调用设 `true` 但未 await 完成，第二个调用直接 return → 使用未初始化的 Addressables。

### 9.2 修复方案：缓存 UniTask

```
UniTask _initTask;
UniTask EnsureAddressablesInitialized(CancellationToken ct)
{
    // 缓存初始化任务，并发调用共享同一个任务
    return _initTask ??= InitCoreAsync();
}
```

- 首次调用创建任务并缓存。
- 后续并发调用 await 同一任务，无竞态。
- 失败时清空缓存允许重试：`catch { _initTask = default; throw; }`。

---

## 10. 预留能力（暂不实现，文档提及）

以下能力本阶段**不实现**，但在文档中明确预留，便于未来扩展：

| 能力 | 预留方式 | 未来实现思路 |
|------|----------|--------------|
| 加载失败重试 | API 不含重试参数 | 业务用 UniTask 自行 `Retry` 操作符包装；未来可在 ResMgr 加 `RetryPolicy` 参数 |
| 内存上限自动卸载 | 无自动卸载逻辑 | 未来可加 `MemoryBudget` 配置，超限时按 LRU 卸载 `RefCount==1` 的预加载资产 |
| 加载优先级 | 无优先级参数 | 未来可暴露 `Addressables.DownloadDependencies` 的优先级设置 |

> **当前约束**：业务层若需重试，用 UniTask 的 `Retry`/`RetryUntil` 操作符自行包装 `LoadAssetAsync` 调用。

---

## 11. Resources 接口说明（已删除）

### 11.1 框架决定

**框架删除 `LoadResourceAsync` 接口，统一走 Addressables。**

理由：
- 框架原则是"禁用 Resources"，保留接口会诱导误用。
- Addressables 已能覆盖所有资源加载场景（含运行时下载、依赖管理、加密）。

### 11.2 业务层自行使用 Resources（仅限特殊需求）

若业务层确有需求（如 Addressables 尚未初始化阶段的极早期资源、第三方插件强制要求 Resources），
可**自行**调用 `UnityEngine.Resources.Load`，但：

- 该资源**不纳入 ResMgr 管理**，无法通过 `IsAssetLoaded` 查询、无法通过 `UnloadAsset` 释放。
- 业务自行调用 `Resources.UnloadAsset` / `Resources.UnloadUnusedAssets` 管理。
- 框架**不推荐**此用法，仅在无替代方案时使用。

---

## 12. API 契约（公开接口）

> 以下为**公开 API 契约**，实现时方法签名必须一致（参数名可微调，但语义不可变）。

### 12.1 资产加载与引用计数

```
// 加载资产，返回引用票据
UniTask<AssetHandle<T>> LoadAssetAsync<T>(
    string key,
    IProgress<float> progress = null,
    CancellationToken ct = default) where T : Object;

// 查询资产是否已加载（RefCount > 0）
bool IsAssetLoaded(string key);

// 已加载资产数量
int LoadedAssetCount { get; }
```

### 12.2 AssetHandle\<T>

```
sealed class AssetHandle<T> : IDisposable where T : Object
{
    T Asset { get; }        // 获取资源（已 Dispose 返回 null + Warning）
    bool IsValid { get; }  // 是否仍有效
    string Key { get; }    // 对应 key
    void Dispose();         // 减引用计数（幂等）
}
```

### 12.3 GameObject 实例化

```
UniTask<GameObject> InstantiateAsync(
    string key, Vector3 position, Quaternion rotation,
    CancellationToken ct = default);

UniTask<GameObject> InstantiateAsync(string key, CancellationToken ct = default);

// 释放实例化对象（池化回池，非池化销毁 + 释放 AssetHandle）
void Release(GameObject instance);
```

### 12.4 场景底层原语

```
UniTask<SceneHandle> LoadSceneAsync(
    string key,
    LoadSceneMode mode = LoadSceneMode.Single,
    bool activateOnLoad = true,
    IProgress<float> progress = null,
    CancellationToken ct = default);

sealed class SceneHandle : IDisposable
{
    Scene Scene { get; }
    float Progress { get; }
    bool IsActivated { get; }
    void Activate();
    UniTask UnloadAsync(CancellationToken ct = default);
    void Dispose();  // 等同 UnloadAsync
}
```

### 12.5 批量预加载

```
UniTask PreloadAsync(
    IReadOnlyList<string> keys,
    IProgress<float> progress = null,
    CancellationToken ct = default);

UniTask PreloadByLabelAsync(
    string label,
    IProgress<float> progress = null,
    CancellationToken ct = default);
```

### 12.6 资源使用统计

```
IReadOnlyList<AssetInfo> GetLoadedAssetsInfo();
int LoadedAssetCount { get; }
int PooledInstanceCount { get; }
int DirectInstanceCount { get; }

struct AssetInfo
{
    string Key;
    Type Type;
    int RefCount;
    long Size;
}
```

### 12.7 IModule 实现

```
void Init();
void Dispose();
```

---

## 13. 使用示例（伪代码，非最终实现）

### 13.1 加载资产（直接使用）

```
// 模块 A 加载配置
AssetHandle<GameConfig> handle = await GameGlobal.ResMgr.LoadAssetAsync<GameConfig>("Config/Main");

// 使用
var config = handle.Asset;
Debug.Log(config.PlayerHp);

// 用完释放（重要！）
handle.Dispose();
```

### 13.2 多模块共享资源（引用计数正确）

```
// 模块 A
var handleA = await ResMgr.LoadAssetAsync<GameObject>("Enemy/Prefab");
// RefCount = 1

// 模块 B 也加载同一 key（各自持有独立 handle）
var handleB = await ResMgr.LoadAssetAsync<GameObject>("Enemy/Prefab");
// RefCount = 2，底层共享同一 handle

// 模块 A 释放
handleA.Dispose();
// RefCount = 1，底层 handle 仍活跃，模块 B 安全使用

// 模块 B 释放
handleB.Dispose();
// RefCount = 0，底层 handle 真正释放
```

> **禁止**：`handleB = handleA`（传递 handle）——会导致重复 Dispose 或一方持有过期引用。

### 13.3 实例化 GameObject

```
// 实例化（自动托管 prefab 的 AssetHandle）
GameObject enemy = await ResMgr.InstantiateAsync("Enemy/Prefab", position, rotation);

// 使用...

// 释放（池化回池，非池化销毁 + 释放 prefab 引用）
ResMgr.Release(enemy);

// 或者直接 Destroy（destroyCancellationToken 自动清理引用）
Object.Destroy(enemy);  // 也安全，引用自动释放
```

### 13.4 场景加载（含激活控制）

```
// 加载但不激活（用于过渡动画）
var sceneHandle = await ResMgr.LoadSceneAsync(
    "Scene/Battle",
    mode: LoadSceneMode.Single,
    activateOnLoad: false,
    progress: Progress.Create(p => uiProgressBar.Value = p));

// 过渡动画播完...
await fadeOutTask;

// 激活场景
sceneHandle.Activate();

// ... 战斗中 ...

// 卸载场景
await sceneHandle.UnloadAsync();
```

### 13.5 按 Label 预加载

```
// 预加载所有通用 UI
await ResMgr.PreloadByLabelAsync(
    "UI/Common",
    progress: Progress.Create(p => Debug.Log($"预加载进度: {p:P0}")));

// 预加载后，后续 LoadAssetAsync 同 key 直接命中缓存
var handle = await ResMgr.LoadAssetAsync<Sprite>("UI/Common/Icon_Hp");
```

### 13.6 资源使用统计（Debug）

```
foreach (var info in ResMgr.GetLoadedAssetsInfo())
{
    Debug.Log($"{info.Key} | type={info.Type.Name} | refCount={info.RefCount} | size={info.Size}");
}
```

---

## 14. 实现检查清单

实现完成后，逐项自查：

- [ ] `AssetEntry` 引用计数正确，`RefCount==0` 时真正 `Addressables.Release`
- [ ] `AssetHandle<T>` 是 `sealed class` + `IDisposable`，`Dispose` 幂等
- [ ] `AssetHandle<T>` 不可复制传递（文档与示例强调，类型设计上 `Asset` 属性只读）
- [ ] `LoadAssetAsync` 并发同 key 时共享 entry，引用计数正确
- [ ] `InstantiateAsync` 内部 `LoadAssetAsync<GameObject>` 持有 AssetHandle
- [ ] 非池化实例绑定 `destroyCancellationToken`，销毁时自动从 `InstanceTracker` 移除 + Dispose AssetHandle
- [ ] `Release(instance)` 按 `Source` 模式分流（Pool 回池，Direct 销毁 + 释放引用）
- [ ] `SceneHandle` 支持 `activateOnLoad=false` + `Activate()`
- [ ] `SceneHandle.UnloadAsync` 正确释放 Addressables scene handle
- [ ] `LoadSceneAsync` 支持 `Single` / `Additive` 模式
- [ ] `LoadSceneAsync` 进度回调正确
- [ ] `PreloadAsync` 并行加载 + 统一进度
- [ ] `PreloadByLabelAsync` 通过 Label 获取资源列表后加载
- [ ] 预加载并发数受 `LifecycleConfig.PreloadConcurrency` 控制（防 IO 打爆）
- [ ] `EnsureAddressablesInitialized` 用 `UniTask` 缓存，无并发竞态
- [ ] `GetLoadedAssetsInfo` 返回正确的 key/type/refCount
- [ ] `Resources` 接口已删除，无残留
- [ ] `Release(null)` 与 `Release(未托管对象)` 有 Warning 兜底，不崩
- [ ] `Dispose()` 清理所有 entry、instance tracker、scene handle
- [ ] 所有公开 API 有中文 XML 注释
- [ ] 无硬编码（预加载并发数等走配置）
- [ ] 错误处理：加载失败、key 不存在、类型不匹配等有明确异常 + Warning

---

## 15. 依赖关系

| 依赖项 | 方向 | 说明 |
|--------|------|------|
| UniTask | 引用 | 异步加载、进度、并发控制 |
| Addressables | 引用 | 核心资源系统 |
| PoolMgr | 引用（InstantiateAsync 时） | 通过 `GameGlobal.PoolMgr` 查池/取池/回池 |
| GameGlobal | 被引用 | 作为服务定位器暴露 ResMgr |

> **与 Lifecycle 的关系**：ResMgr 可使用 LifecycleMgr 的 CTS 工厂（如加载超时取消），
> 但 ResMgr 初始化在 LifecycleMgr 之后，不阻塞。ResMgr 的场景加载**不调用** Lifecycle，
> 场景事件由 Unity `SceneManager` → Lifecycle 自动转发（见 §5.5）。

---

## 16. 后续模块依赖本模块的接口

| 后续模块 | 使用的 ResMgr 接口 |
|----------|-------------------|
| UI Framework (2) | `LoadAssetAsync<GameObject>`、`InstantiateAsync`、`PreloadByLabelAsync("UI/*")` |
| Audio Manager (2) | `LoadAssetAsync<AudioClip>`、`PreloadByLabelAsync("Audio/*")` |
| Save/Load System (1) | `LoadAssetAsync<SaveConfig>` |
| Level/Scene Manager (3) | `LoadSceneAsync`、`SceneHandle.Activate/Unload`、`PreloadByLabelAsync("Scene/*")` |
| PoolMgr | 预制体加载（池预热时 `LoadAssetAsync` 持有 AssetHandle） |
| 业务层（敌人/道具/技能） | `InstantiateAsync`、`Release`、`LoadAssetAsync` |

---

## 17. 关键使用规范（业务层 Coding AI 必读）

> 本节是**强制规范**，业务层 Coding AI 必须严格遵守。

### 17.1 AssetHandle 不可传递

- ❌ 禁止：`handleB = handleA`
- ❌ 禁止：把 handle 作为方法返回值传递给多个调用方
- ✅ 正确：每个需要资源的模块自行 `LoadAssetAsync`（引用计数自动 +1）

### 17.2 AssetHandle 必须 Dispose

- 加载后**必须**在不再使用时 `Dispose()`。
- 忘记 Dispose 会导致资源永不释放（内存泄漏）。
- 推荐：在模块的 `Dispose` / `OnDestroy` 中统一 Dispose 持有的所有 handle。

### 17.3 实例化对象必须 Release 或 Destroy

- `InstantiateAsync` 返回的 GameObject 必须通过 `Release(instance)` 释放。
- 直接 `Object.Destroy(instance)` 也安全（destroyCancellationToken 自动清理引用）。
- **禁止**：忘记释放导致实例与 prefab 引用双重泄漏。

### 17.4 场景必须 Unload

- `LoadSceneAsync` 返回的 `SceneHandle` 必须在场景结束时 `UnloadAsync` 或 `Dispose`。
- 忘记 Unload 会导致 Addressables scene handle 泄漏、内存累积。

### 17.5 预加载后仍需 LoadAssetAsync 获取票据

- `PreloadAsync` / `PreloadByLabelAsync` 只是"热身"，handle 由 ResMgr 内部持有。
- 使用时仍需 `LoadAssetAsync` 获取 `AssetHandle<T>` 票据（RefCount++，复用已加载的底层 handle）。

---

**文档结束。实现阶段请严格遵循本契约。**
