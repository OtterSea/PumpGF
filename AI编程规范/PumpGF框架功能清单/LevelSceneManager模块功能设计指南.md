# Level / Scene Manager 模块功能设计指南

> **文档定位**：本文件是 Level/Scene Manager 模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、API 契约与数据结构，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的公开 API 语义**。
>
> **模块编号**：3-3。
> **前置依赖**：ResMgr（SceneHandle）、Lifecycle（Update 驱动 + 场景钩子）、UIManager（Loading UI）、DOTween（过渡动画）。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 |
|--------|------|
| 1. ILevel 接口 | 标准化关卡入口（OnEnterAsync/OnExitAsync/OnUpdate） |
| 2. SceneTransition | FadeTransition + LoadingScreenTransition 预置，可自定义 |
| 3. 数据注入 | ILevelData 接口，LoadLevelAsync 时传入 |
| 4. Additive Loading | 支持，ILevel 可管理多场景 |
| 5. 过渡 UI | SceneTransition 内部处理（封装 UIManager 调用） |
| 6. 关卡注册 | 注册式，key → ILevel 工厂 |
| 7. Update 绑定 | LevelManager 统一绑定到 Lifecycle 通道 |
| 8. 进度回调 | LoadLevelAsync 提供 IProgress<float> |
| 风格 | `LevelManager : IModule`，纳入 `GameGlobal`，纯 C# 类 |
| 底层 | ResMgr SceneHandle + DOTween + R3/UniTask |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**Level/Scene Manager 是关卡与场景加载的高级流程编排器**——
在 ResMgr 场景原语之上，提供 ILevel 标准化入口、SceneTransition 过渡、数据注入、Additive Loading。
负责"加载哪个关卡、怎么过渡、怎么注入数据"，但**不实现关卡内业务逻辑**。

### 1.2 职责清单

| 职责 | 说明 |
|------|------|
| 关卡标准化 | ILevel 接口（OnEnter/OnExit/OnUpdate 生命周期） |
| 关卡注册 | key → ILevel 工厂注册 |
| 加载流程 | 过渡→卸载旧→加载新→初始化→揭开过渡 |
| 过渡动画 | SceneTransition 抽象，预置 Fade/LoadingScreen |
| 数据注入 | ILevelData 接口，传入关卡配置 |
| Additive Loading | 多场景叠加管理 |
| Update 绑定 | 统一绑定到 Lifecycle 通道，暂停感知 |
| 进度回调 | 场景加载进度 IProgress |

### 1.3 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ 场景底层原语 | ResMgr 的 SceneHandle 负责 |
| ❌ 场景事件钩子 | Lifecycle 负责（OnSceneLoaded 等） |
| ❌ 关卡内业务逻辑 | ILevel 实现类负责 |
| ❌ 关卡存档 | GameDataStore + Save/Load 负责 |
| ❌ 场景烘焙/光照设置 | Unity 编辑器负责 |

---

## 2. 整体架构

```
┌─────────────────────────────────────────┐
│            业务逻辑层                    │
│  LevelManager.LoadLevelAsync("Battle")  │
└──────────────────┬──────────────────────┘
                   │ LoadLevelAsync
                   ▼
┌─────────────────────────────────────────┐
│          LevelManager (IModule)          │
│  ┌──────────────────────────────────┐   │
│  │ 关卡注册表                        │   │
│  │  key → Func<ILevel>              │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ 当前关卡                          │   │
│  │  ILevel + Update 订阅             │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ 场景句柄表（Additive）            │   │
│  │  List<SceneHandle>               │   │
│  └──────────────────────────────────┘   │
└──────┬──────────┬──────────┬────────────┘
       │ Scene   │ Update   │ UI
       ▼          ▼          ▼
┌──────────┐ ┌──────────┐ ┌──────────┐
│  ResMgr  │ │ Lifecycle│ │ UIManager│
│(SceneHandle)│ │(Update) │ │(Loading)│
└──────────┘ └──────────┘ └──────────┘

        │ SceneTransition
        ▼
┌─────────────────────────────────────┐
│  SceneTransition (过渡抽象)         │
│  ├─ FadeTransition (淡入淡出)        │
│  └─ LoadingScreenTransition (Loading)│
└─────────────────────────────────────┘
```

---

## 3. ILevel 接口

### 3.1 接口定义

```
public interface ILevel
{
    // 进入关卡：加载场景、初始化、注入数据
    UniTask OnEnterAsync(ILevelData data, CancellationToken ct);

    // 退出关卡：清理、卸载场景
    UniTask OnExitAsync(CancellationToken ct);

    // 关卡 Update（由 LevelManager 绑定 Lifecycle 驱动）
    void OnUpdate(float dt);
}
```

### 3.2 关卡示例

```
public class BattleLevel : ILevel
{
    private SceneHandle _mainScene;
    private SceneHandle _uiScene;

    public async UniTask OnEnterAsync(ILevelData data, CancellationToken ct)
    {
        var battleData = (BattleLevelData)data;

        // 加载主场景
        _mainScene = await LevelManager.LoadSceneAsync(
            "Scene/Battle", LoadSceneMode.Single, ct: ct);

        // Additive 加载 UI 场景
        _uiScene = await LevelManager.LoadSceneAsync(
            "Scene/BattleUI", LoadSceneMode.Additive, ct: ct);

        // 注入数据：初始化敌人、配置难度
        var enemySpawner = UnityEngine.Object.FindObjectOfType<EnemySpawner>();
        enemySpawner.Init(battleData.Difficulty, battleData.EnemyIds);
    }

    public async UniTask OnExitAsync(CancellationToken ct)
    {
        // 清理逻辑...
    }

    public void OnUpdate(float dt)
    {
        // 关卡逻辑（如检查胜利/失败条件）
    }
}
```

---

## 4. ILevelData 数据注入

### 4.1 接口定义

```
public interface ILevelData
{
    string LevelId { get; }
}
```

### 4.2 关卡数据示例

```
public class BattleLevelData : ILevelData
{
    public string LevelId { get; }
    public int Difficulty { get; }
    public List<int> EnemyIds { get; }
    public float TimeLimit { get; }

    public BattleLevelData(string id, int difficulty, List<int> enemies, float time)
    {
        LevelId = id;
        Difficulty = difficulty;
        EnemyIds = enemies;
        TimeLimit = time;
    }
}
```

### 4.3 使用

```
var data = new BattleLevelData("Stage1_3", difficulty: 3, enemies, timeLimit: 300f);
await LevelManager.LoadLevelAsync("Battle", data, transition: new LoadingScreenTransition());
```

- 同一关卡类，不同数据 = 不同难度/配置。
- 数据可从 ConfigMgr 的关卡配置表读取。

---

## 5. SceneTransition 过渡效果

### 5.1 抽象基类

```
public abstract class SceneTransition
{
    // 加载前：遮罩当前画面（如淡入黑屏/显示 Loading）
    public abstract UniTask PlayFadeOut(IProgress<float> progress, CancellationToken ct);

    // 加载后：揭开新画面（如淡出黑屏/隐藏 Loading）
    public abstract UniTask PlayFadeIn(CancellationToken ct);
}
```

### 5.2 FadeTransition（预置）

```
public class FadeTransition : SceneTransition
{
    public float Duration { get; set; } = 0.5f;
    public Color FadeColor { get; set; } = Color.black;

    // FadeOut：黑色 Image DOFade 0→1
    // FadeIn：黑色 Image DOFade 1→0
}
```

- 简单全屏淡入淡出。
- 用 DOTween 实现。
- 内部创建临时 Canvas + Image。

### 5.3 LoadingScreenTransition（预置）

```
public class LoadingScreenTransition : SceneTransition
{
    public string LoadingPageId { get; set; } = "UI/LoadingPage";

    // FadeOut：Push Loading 页面（UIManager），显示进度条
    // FadeIn：Pop Loading 页面
}
```

- 显示 Loading 界面 + 进度条。
- `PlayFadeOut` 时 Push Loading 页面，`progress` 更新进度条。
- `PlayFadeIn` 时 Pop Loading 页面。
- 内部调用 UIManager。

### 5.4 自定义过渡

业务可继承 SceneTransition 实现自定义效果：
```
public class CircleWipeTransition : SceneTransition { ... }
```

### 5.5 无过渡

```
await LevelManager.LoadLevelAsync("MainMenu", data, transition: null);
// 直接加载，无过渡动画
```

---

## 6. Additive Loading

### 6.1 多场景管理

LevelManager 维护场景句柄表：
```
List<SceneHandle> _additiveScenes;
```

- 主场景用 `LoadSceneMode.Single`（替换当前）。
- 子场景用 `LoadSceneMode.Additive`（叠加）。
- 退出关卡时统一卸载所有场景句柄。

### 6.2 场景加载 API

```
UniTask<SceneHandle> LoadSceneAsync(
    string sceneKey,
    LoadSceneMode mode = LoadSceneMode.Single,
    IProgress<float> progress = null,
    CancellationToken ct = default);

UniTask UnloadSceneAsync(SceneHandle handle, CancellationToken ct = default);
```

- 内部委托 ResMgr 的 `LoadSceneAsync`。
- 返回 SceneHandle 供 ILevel 持有，退出时卸载。

### 6.3 典型 Additive 用法

```
// ILevel.OnEnterAsync 内
_mainScene = await LevelManager.LoadSceneAsync("Scene/Battle", LoadSceneMode.Single, progress, ct);
_uiScene = await LevelManager.LoadSceneAsync("Scene/BattleUI", LoadSceneMode.Additive, ct: ct);
_lightingScene = await LevelManager.LoadSceneAsync("Scene/BattleLighting", LoadSceneMode.Additive, ct: ct);
```

---

## 7. 关卡注册与加载流程

### 7.1 注册

```
LevelManager.RegisterLevel("MainMenu", () => new MainMenuLevel());
LevelManager.RegisterLevel("Battle", () => new BattleLevel());
LevelManager.RegisterLevel("Cutscene", () => new CutsceneLevel());
```

- 启动时注册所有关卡（或按需注册）。
- key → ILevel 工厂。
- 重复注册 Warning + 覆盖。

### 7.2 加载流程

```
LoadLevelAsync(key, data, transition, progress, ct):
  1. 查找注册表，创建 ILevel 实例
  2. 若 transition != null:
       transition.PlayFadeOut(progress, ct)  // 遮罩当前画面
  3. 若有当前关卡:
       currentLevel.OnExitAsync(ct)  // 退出当前关卡
       绑定 Update 的 Disposable.Dispose()  // 解绑 Update
  4. level.OnEnterAsync(data, ct)  // 进入新关卡（内含场景加载）
  5. 绑定 level.OnUpdate 到 Lifecycle 通道
  6. 若 transition != null:
       transition.PlayFadeIn(ct)  // 揭开新画面
  7. 返回
```

### 7.3 卸载当前关卡

```
UnloadCurrentLevelAsync(transition, ct):
  1. transition.PlayFadeOut(ct)
  2. currentLevel.OnExitAsync(ct)
  3. 解绑 Update
  4. transition.PlayFadeIn(ct)
  5. CurrentLevel = null
```

---

## 8. 与 Lifecycle 集成（Update 绑定）

### 8.1 自动绑定

```
// LevelManager 加载关卡时
_currentLevelUpdateDisposable = Lifecycle.GetUpdateObservable(UpdateChannel.Logic)
    .Subscribe(dt => _currentLevel.OnUpdate(dt));
```

- 绑定 Logic 通道（固定 60Hz，暂停感知）。
- 关卡退出时 Dispose 解绑。
- 暂停时关卡不 Update（与 PauseProfile 联动）。

### 8.2 通道可选

```
await LevelManager.LoadLevelAsync("Battle", data, 
    updateChannel: UpdateChannel.Logic);  // 默认 Logic
```

- 某些关卡可能用 Default 通道（每帧）。

---

## 9. 加载进度回调

### 9.1 进度传递

```
await LevelManager.LoadLevelAsync("Battle", data,
    transition: new LoadingScreenTransition(),
    progress: Progress.Create(p => Debug.Log($"加载进度: {p:P0}")),
    ct: ct);
```

- `progress` 报告场景加载进度（0~1）。
- SceneTransition 的 `PlayFadeOut` 接收 progress，更新 Loading 进度条。
- 多场景加载时，进度按场景数加权。

### 9.2 进度来源

- ResMgr 的 `LoadSceneAsync` 的 `IProgress<float>`。
- 多场景时，按场景数分配进度区间。

---

## 10. API 契约（公开接口）

### 10.1 LevelManager

```
class LevelManager : IModule
{
    // ── 关卡注册 ──
    void RegisterLevel(string key, Func<ILevel> factory);
    void UnregisterLevel(string key);
    bool HasLevel(string key);

    // ── 关卡加载 ──
    UniTask LoadLevelAsync(
        string key,
        ILevelData data = null,
        SceneTransition transition = null,
        IProgress<float> progress = null,
        UpdateChannel updateChannel = UpdateChannel.Logic,
        CancellationToken ct = default);

    UniTask UnloadCurrentLevelAsync(
        SceneTransition transition = null,
        CancellationToken ct = default);

    // ── 场景加载（Additive）──
    UniTask<SceneHandle> LoadSceneAsync(
        string sceneKey,
        LoadSceneMode mode = LoadSceneMode.Single,
        IProgress<float> progress = null,
        CancellationToken ct = default);

    UniTask UnloadSceneAsync(SceneHandle handle, CancellationToken ct = default);

    // ── 查询 ──
    ILevel CurrentLevel { get; }
    string CurrentLevelKey { get; }
    bool IsLoading { get; }

    // ── IModule ──
    void Init();
    void Dispose();
}
```

### 10.2 ILevel

```
interface ILevel
{
    UniTask OnEnterAsync(ILevelData data, CancellationToken ct);
    UniTask OnExitAsync(CancellationToken ct);
    void OnUpdate(float dt);
}
```

### 10.3 ILevelData

```
interface ILevelData
{
    string LevelId { get; }
}
```

### 10.4 SceneTransition

```
abstract class SceneTransition
{
    abstract UniTask PlayFadeOut(IProgress<float> progress, CancellationToken ct);
    abstract UniTask PlayFadeIn(CancellationToken ct);
}

class FadeTransition : SceneTransition { ... }
class LoadingScreenTransition : SceneTransition { ... }
```

---

## 11. 使用示例（伪代码）

### 11.1 注册关卡

```
void RegisterLevels()
{
    GameGlobal.LevelManager.RegisterLevel("MainMenu", () => new MainMenuLevel());
    GameGlobal.LevelManager.RegisterLevel("Battle", () => new BattleLevel());
    GameGlobal.LevelManager.RegisterLevel("Cutscene", () => new CutsceneLevel());
}
```

### 11.2 加载关卡（带过渡 + 进度）

```
// 加载战斗关卡
var data = new BattleLevelData("Stage1", difficulty: 3, enemyIds, timeLimit: 300f);
await GameGlobal.LevelManager.LoadLevelAsync(
    "Battle",
    data,
    transition: new LoadingScreenTransition(),
    progress: Progress.Create(p => Debug.Log($"加载中: {p:P0}")),
    ct: destroyCancellationToken);
```

### 11.3 加载关卡（无过渡，快速）

```
await GameGlobal.LevelManager.LoadLevelAsync("MainMenu", transition: null);
```

### 11.4 Additive 场景

```
public class BattleLevel : ILevel
{
    public async UniTask OnEnterAsync(ILevelData data, CancellationToken ct)
    {
        // 主场景
        await LevelManager.LoadSceneAsync("Scene/Battle", LoadSceneMode.Single, ct: ct);

        // 叠加 UI 场景
        await LevelManager.LoadSceneAsync("Scene/BattleUI", LoadSceneMode.Additive, ct: ct);
    }
}
```

### 11.5 自定义过渡

```
public class CircleWipeTransition : SceneTransition
{
    public override UniTask PlayFadeOut(IProgress<float> progress, CancellationToken ct)
    {
        // 圆形扩散遮罩动画
        return PlayCircleAnim(toRadius: 0, ct: ct);
    }

    public override UniTask PlayFadeIn(CancellationToken ct)
    {
        return PlayCircleAnim(toRadius: 1, ct: ct);
    }
}

await LevelManager.LoadLevelAsync("Battle", data, new CircleWipeTransition());
```

### 11.6 关卡内 Update

```
public class BattleLevel : ILevel
{
    private float _timer;

    public void OnUpdate(float dt)
    {
        _timer += dt;
        if (_timer >= _timeLimit)
            OnBattleTimeout();
    }
}
```

---

## 12. 实现检查清单

- [ ] `LevelManager : IModule`，纳入 GameGlobal
- [ ] `ILevel` 接口（OnEnterAsync/OnExitAsync/OnUpdate）
- [ ] `ILevelData` 接口
- [ ] 关卡注册表（key → Func<ILevel>）
- [ ] `RegisterLevel`/`UnregisterLevel`/`HasLevel`
- [ ] `LoadLevelAsync` 完整流程（过渡→卸载→加载→初始化→揭开）
- [ ] `UnloadCurrentLevelAsync`
- [ ] `SceneTransition` 抽象基类
- [ ] `FadeTransition` 预置（DOTween）
- [ ] `LoadingScreenTransition` 预置（UIManager Push/Pop）
- [ ] `LoadSceneAsync`/`UnloadSceneAsync`（委托 ResMgr）
- [ ] Additive Loading 多场景句柄管理
- [ ] Update 绑定到 Lifecycle 通道
- [ ] 进度回调 `IProgress<float>` 传递
- [ ] 竞态处理（加载中再次加载 → 排队或拒绝）
- [ ] `Dispose` 清理当前关卡与所有场景句柄
- [ ] 所有公开 API 有中文 XML 注释
- [ ] 无硬编码（通道、过渡时长走配置）

---

## 13. 依赖关系

| 依赖项 | 方向 | 说明 |
|--------|------|------|
| ResMgr | 引用 | SceneHandle 场景原语 |
| Lifecycle | 引用 | Update 绑定 + 场景钩子自动转发 |
| UIManager | 引用 | LoadingScreenTransition 的 Loading UI |
| DOTween | 引用 | FadeTransition 动画 |
| UniTask | 引用 | 异步流程 |
| GameGlobal | 被引用 | 暴露 LevelManager |

> **初始化顺序**：... → LevelManager（在 ResMgr/Lifecycle/UIManager 之后）

---

## 14. 后续模块依赖本模块的接口

| 后续模块 | 使用的 LevelManager 接口 |
|----------|------------------------|
| Debug Console (4-1) | 关卡切换命令、当前关卡查询 |
| 业务层 | 关卡加载、场景管理 |

---

## 15. 关键使用规范（业务层 Coding AI 必读）

### 15.1 关卡必须实现 ILevel 接口

- ❌ 禁止：直接 `SceneManager.LoadScene` 加载关卡
- ✅ 正确：实现 ILevel + `LevelManager.LoadLevelAsync`

### 15.2 场景加载走 LevelManager，不直接调 ResMgr

- ❌ 禁止：`ResMgr.LoadSceneAsync`（绕过流程管理）
- ✅ 正确：`LevelManager.LoadSceneAsync`（Additive 场景在 ILevel 内调用）

### 15.3 关卡数据用 ILevelData 注入

- ❌ 禁止：关卡内硬编码难度/配置
- ✅ 正确：`LoadLevelAsync(key, data)` 传入 ILevelData

### 15.4 过渡用 SceneTransition，不手动 UIManager

- ❌ 禁止：加载前手动 Push Loading 页面
- ✅ 正确：`LoadLevelAsync(key, data, new LoadingScreenTransition())`

### 15.5 关卡 Update 用 OnUpdate，不自建 Update

- ❌ 禁止：关卡内自己订阅 Lifecycle Update
- ✅ 正确：实现 `ILevel.OnUpdate(dt)`，LevelManager 自动绑定

### 15.6 关卡退出必须清理场景

- ILevel.OnExitAsync 内卸载 Additive 场景。
- 或依赖 LevelManager 统一卸载（若 SceneHandle 由 LevelManager 持有）。

---

**文档结束。实现阶段请严格遵循本契约。**
