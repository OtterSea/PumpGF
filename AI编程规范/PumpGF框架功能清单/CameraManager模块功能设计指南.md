# Camera Manager 模块功能设计指南

> **文档定位**：本文件是 Camera Manager 模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、API 契约与数据结构，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的公开 API 语义**。
>
> **模块编号**：2-5（Phase 2 通用系统层）。
> **前置依赖**：Cinemachine 2.x（UPM）、Lifecycle（LateUpdate）、EventBus、GameDataStore、InputMgr。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 |
|--------|------|
| 1. Cinemachine 版本 | 2.x（2.10.3，成熟稳定，基于 MonoBehaviour） |
| 2. 封装深度 | 集成层：管理注册/切换/震动/聚焦/状态联动，不重复 Cinemachine 的跟随/混合/碰撞 |
| 3. 相机注册 | 注册式，key → VirtualCamera |
| 4. 震动 | 统一 `Shake(intensity, duration)` 接口，内部用 Cinemachine Impulse |
| 5. 状态联动 | CameraState 枚举 + 与 InputContext 联动（可开关） |
| 6. 灵敏度持久化 | 通过 GameDataStore.SettingsData 持久化，CameraMgr 自动应用 |
| 7. 模块编号 | 2-5（Phase 2 通用系统层） |
| 风格 | `CameraMgr : IModule`，纳入 `GameGlobal`，纯 C# 类 |
| 底层 | Cinemachine 2.x + R3 + DOTween |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**CameraMgr 是 Cinemachine 与 PumpGF 框架的集成层**——
提供相机注册/切换/震动/聚焦/状态联动的统一 API，与 Lifecycle/EventBus/GameDataStore/InputMgr 集成。
但**不重复 Cinemachine 的跟随/混合/碰撞**等底层能力。

### 1.2 职责清单

| 职责 | 说明 |
|------|------|
| 相机注册 | key → VirtualCamera 注册管理 |
| 相机切换 | SetActiveCamera(key)，Priority 管理 |
| 相机震动 | Shake(intensity, duration)，统一接口 |
| 相机聚焦 | SetFollow(target)/SetLookAt(target) |
| 状态联动 | CameraState 枚举 + InputContext 联动 |
| 灵敏度持久化 | 订阅 GameDataStore 自动应用 |
| EventBus 集成 | 相机切换发事件 |
| Lifecycle 集成 | LateUpdate 通道（相机更新在 LateUpdate） |

### 1.3 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ 重新实现 Cinemachine 跟随/混合 | Cinemachine 已有 |
| ❌ 相机碰撞检测 | Cinemachine Collider 已有 |
| ❌ 相机轨道/路径 | Cinemachine Dolly Cart 已有 |
| ❌ 相机后处理 | Unity PostProcessing/URP 负责 |
| ❌ 渲染/画面特效 | 不在相机管理范围 |

---

## 2. 整体架构

```
┌─────────────────────────────────────────┐
│            业务逻辑层                    │
│  CameraMgr.SetActiveCamera("Battle")   │
│  CameraMgr.Shake(0.5f, 0.3f)           │
└──────────────────┬──────────────────────┘
                   │ API
                   ▼
┌─────────────────────────────────────────┐
│           CameraMgr (IModule)            │
│  ┌──────────────────────────────────┐   │
│  │ 相机注册表                        │   │
│  │  key → CinemachineVirtualCamera  │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ CameraState 映射                  │   │
│  │  CameraState → cameraKey         │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ ImpulseSource（震动）             │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ InputContext 监听（状态联动）     │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ 灵敏度订阅（GameDataStore）       │   │
│  └──────────────────────────────────┘   │
└──────┬──────────┬──────────┬────────────┘
       │ 优先级    │ Impulse   │ 输入轴
       ▼          ▼          ▼
┌─────────────────────────────────────────┐
│           Cinemachine 2.x                │
│  VirtualCamera / Brain / ImpulseSource │
└─────────────────────────────────────────┘
```

---

## 3. 相机注册与切换

### 3.1 注册

```
CameraMgr.Register("Battle", battleVirtualCamera);
CameraMgr.Register("Menu", menuVirtualCamera);
CameraMgr.Register("Cutscene", cutsceneVirtualCamera);
```

- key → CinemachineVirtualCamera 映射。
- 重复注册 Warning + 覆盖。
- 场景加载时注册，卸载时注销。

### 3.2 切换

```
CameraMgr.SetActiveCamera("Battle");
```

- 设置目标 VirtualCamera 的 `Priority` 为最高（如 100），其他降为 0。
- Cinemachine Brain 自动混合到高优先级相机。
- 混合效果由 Brain 的 `m_DefaultBlend` 配置（业务在 Inspector 设）。

### 3.3 查询

```
string ActiveCameraKey { get; }
CinemachineVirtualCamera ActiveCamera { get; }
bool IsCameraRegistered(string key);
```

---

## 4. 相机震动

### 4.1 统一接口

```
void Shake(float intensity, float duration);
void Shake(Vector3 sourcePosition, float intensity, float duration);  // 3D 方向震动
```

- `intensity`：震动强度（0~1）。
- `duration`：持续时间（秒）。
- `sourcePosition`：震动源位置（3D 方向性震动）。

### 4.2 实现

- CameraMgr 初始化时在主相机上创建/获取 `CinemachineImpulseSource`。
- `Shake` 时调用 `impulseSource.GenerateImpulse(intensity)`。
- `duration` 通过 DOTween 控制震动衰减，或用 ImpulseDefinition 的衰减曲线。

### 4.3 无 ImpulseSource 兜底

- 若场景无 ImpulseSource，CameraMgr 自动在主相机创建一个。
- 业务无需手动配置 ImpulseSource。

---

## 5. 相机聚焦（Follow/LookAt）

### 5.1 设置目标

```
void SetFollow(Transform target);
void SetLookAt(Transform target);
void SetFollowAndLookAt(Transform followTarget, Transform lookAtTarget);
```

- 设置当前活跃 VirtualCamera 的 `Follow` / `LookAt`。
- 切换角色/敌人聚焦时调用。

### 5.2 使用场景

```
// 切换聚焦到敌人（如锁定目标）
CameraMgr.SetFollowAndLookAt(enemyTransform, enemyTransform);

// 回到玩家
CameraMgr.SetFollowAndLookAt(playerTransform, playerTransform);
```

---

## 6. 相机状态与 InputContext 联动

### 6.1 CameraState 枚举

```
public enum CameraState
{
    None,
    Battle,      // 战斗相机
    Menu,        // 菜单相机
    Cutscene,    // 过场相机
    Dialog,      // 对话相机
    Debug,       // 调试相机
}
```

> 与 InputContext 枚举值一致，便于联动。

### 6.2 注册映射

```
CameraMgr.RegisterCameraState(CameraState.Battle, "Battle");
CameraMgr.RegisterCameraState(CameraState.Menu, "Menu");
CameraMgr.RegisterCameraState(CameraState.Cutscene, "Cutscene");
```

- CameraState → cameraKey 映射。

### 6.3 InputContext 联动

```
CameraMgr.AutoSwitchOnContextChange = true;  // 默认 true
```

- 开启时，CameraMgr 监听 InputMgr 的上下文变化。
- `Push(InputContext.Battle)` → 自动切换到 `CameraState.Battle` 对应的相机。
- `Pop()` → 恢复上层 CameraState。
- 关闭时，业务手动 `SetActiveCamera(key)`。

### 6.4 手动切换

```
CameraMgr.SetActiveCameraState(CameraState.Cutscene);
```

- 不依赖 InputContext 联动时手动切换。

---

## 7. 灵敏度/设置持久化

### 7.1 GameDataStore 扩展

```
public class SettingsData
{
    // 已有：BgmVolume / SfxVolume / ...
    public ReactiveProperty<float> CameraSensitivity { get; } = new(1.0f);  // 灵敏度倍率
    public ReactiveProperty<bool> InvertY { get; } = new(false);            // Y 轴反转
}
```

### 7.2 CameraMgr 自动应用

- CameraMgr 订阅 `GameDataStore.SettingsData.CameraSensitivity`。
- 变化时应用到当前 VirtualCamera 的输入轴：
  - `m_XAxis.m_MaxSpeed *= sensitivity`
  - `m_YAxis.m_MaxSpeed *= sensitivity`
  - `m_YAxis.m_InvertInput = invertY`

### 7.3 业务使用

```
// 玩家在设置界面调整灵敏度
GameGlobal.GameData.Settings.CameraSensitivity.Value = 1.5f;
// CameraMgr 自动应用，无需手动调 CameraMgr
```

---

## 8. 与框架模块集成

### 8.1 Lifecycle

- 相机更新在 LateUpdate（Cinemachine Brain 在 LateUpdate 处理）。
- CameraMgr 不额外订阅 Lifecycle，Cinemachine 自身处理。
- 暂停感知：暂停时 Cinemachine Brain 可配置是否继续更新（业务选择）。

### 8.2 EventBus

- 相机切换时发布事件：

```
public readonly struct CameraSwitchedEvent
{
    public readonly string From;
    public readonly string To;
}
```

- 业务可订阅，如 UI 显示当前相机名、音频切换等。

### 8.3 InputMgr

- AutoSwitchOnContextChange 监听 InputContext 变化。

### 8.4 GameDataStore

- 订阅灵敏度/反转设置，自动应用。

### 8.5 Level/Scene Manager

- 场景切换时清理旧场景的相机注册。
- LevelManager.OnEnterAsync 内注册新场景相机。

---

## 9. API 契约（公开接口）

### 9.1 CameraMgr

```
class CameraMgr : IModule
{
    // ── 注册 ──
    void Register(string key, CinemachineVirtualCamera camera);
    void Unregister(string key);
    bool IsCameraRegistered(string key);

    // ── 切换 ──
    void SetActiveCamera(string key);
    void SetActiveCameraState(CameraState state);
    string ActiveCameraKey { get; }
    CinemachineVirtualCamera ActiveCamera { get; }

    // ── 震动 ──
    void Shake(float intensity, float duration);
    void Shake(Vector3 sourcePosition, float intensity, float duration);

    // ── 聚焦 ──
    void SetFollow(Transform target);
    void SetLookAt(Transform target);
    void SetFollowAndLookAt(Transform followTarget, Transform lookAtTarget);

    // ── 状态联动 ──
    void RegisterCameraState(CameraState state, string cameraKey);
    void UnregisterCameraState(CameraState state);
    bool AutoSwitchOnContextChange { get; set; }

    // ── IModule ──
    void Init();
    void Dispose();
}
```

### 9.2 CameraState 枚举

```
public enum CameraState
{
    None,
    Battle,
    Menu,
    Cutscene,
    Dialog,
    Debug,
}
```

### 9.3 CameraSwitchedEvent

```
public readonly struct CameraSwitchedEvent
{
    public readonly string From;
    public readonly string To;
}
```

---

## 10. 使用示例（伪代码）

### 10.1 注册与切换

```
// 场景加载时注册相机
CameraMgr.Register("Battle", battleVirtualCamera);
CameraMgr.Register("Menu", menuVirtualCamera);

// 切换到战斗相机
CameraMgr.SetActiveCamera("Battle");
```

### 10.2 震动

```
// 受击震动
CameraMgr.Shake(intensity: 0.3f, duration: 0.2f);

// 爆炸震动（3D 方向）
CameraMgr.Shake(explosionPosition, intensity: 0.8f, duration: 0.5f);
```

### 10.3 聚焦目标

```
// 锁定敌人时聚焦
CameraMgr.SetFollowAndLookAt(lockedEnemy, lockedEnemy);

// 取消锁定回到玩家
CameraMgr.SetFollowAndLookAt(player, player);
```

### 10.4 InputContext 联动

```
// 注册状态映射
CameraMgr.RegisterCameraState(CameraState.Battle, "Battle");
CameraMgr.RegisterCameraState(CameraState.Menu, "Menu");

// AutoSwitchOnContextChange = true（默认）
// 打开菜单 → InputContext 变为 Menu → 自动切换到 Menu 相机
await UIManager.Push("UI/PauseMenu",
    new PageConfig { InputContext = InputContext.Menu });
// 相机自动切换到 Menu
```

### 10.5 灵敏度调整

```
// 设置界面
sensitivitySlider.OnValueChangedAsObservable()
    .Subscribe(v => GameGlobal.GameData.Settings.CameraSensitivity.Value = v)
    .AddTo(this);

// CameraMgr 自动应用，无需手动调
```

### 10.6 监听相机切换

```
EventBus.OnEvent<CameraSwitchedEvent>()
    .Subscribe(e => Debug.Log($"相机: {e.From} → {e.To}"))
    .AddTo(this);
```

---

## 11. 实现检查清单

- [ ] `CameraMgr : IModule`，纳入 GameGlobal
- [ ] 相机注册表（key → CinemachineVirtualCamera）
- [ ] `SetActiveCamera` 通过 Priority 管理切换
- [ ] `Shake` 内部用 CinemachineImpulseSource
- [ ] ImpulseSource 自动创建（无配置时兜底）
- [ ] `SetFollow`/`SetLookAt`/`SetFollowAndLookAt`
- [ ] `CameraState` 枚举 + `RegisterCameraState` 映射
- [ ] `AutoSwitchOnContextChange` 监听 InputMgr 上下文变化
- [ ] 订阅 GameDataStore 的 CameraSensitivity / InvertY
- [ ] 灵敏度自动应用到 VirtualCamera 输入轴
- [ ] `CameraSwitchedEvent` 发布
- [ ] `Dispose` 清理注册与订阅
- [ ] 所有公开 API 有中文 XML 注释
- [ ] 无硬编码（Priority 值等走配置）

---

## 12. 依赖关系

| 依赖项 | 方向 | 说明 |
|--------|------|------|
| Cinemachine 2.x | 引用 | VirtualCamera/Brain/ImpulseSource |
| InputMgr | 引用 | InputContext 联动 |
| GameDataStore | 引用 | 灵敏度持久化 |
| EventBus | 引用 | CameraSwitchedEvent |
| DOTween | 引用 | 震动衰减（可选） |
| GameGlobal | 被引用 | 暴露 CameraMgr |

> **初始化顺序**：... → CameraMgr（在 InputMgr/GameDataStore 之后）

---

## 13. 后续模块依赖本模块的接口

| 后续模块 | 使用的 CameraMgr 接口 |
|----------|----------------------|
| Level/Scene Manager (3-3) | 过场相机切换 |
| 业务层（战斗） | 震动、聚焦、状态切换 |
| Debug Console (4-1) | 相机调试面板 |

---

## 14. 关键使用规范（业务层 Coding AI 必读）

### 14.1 相机切换走 CameraMgr，不直接改 Priority

- ❌ 禁止：`virtualCamera.Priority = 100`
- ✅ 正确：`CameraMgr.SetActiveCamera("Battle")`

### 14.2 震动走 CameraMgr.Shake

- ❌ 禁止：直接操作 ImpulseSource
- ✅ 正确：`CameraMgr.Shake(intensity, duration)`

### 14.3 相机注册在场景加载时

- LevelManager/ILevel.OnEnterAsync 内注册相机。
- 场景卸载时注销。

### 14.4 灵敏度走 GameDataStore

- ❌ 禁止：`CameraMgr.SetSensitivity(value)`
- ✅ 正确：`GameDataStore.Settings.CameraSensitivity.Value = value`（CameraMgr 自动应用）

### 14.5 状态联动优先用 AutoSwitch

- ❌ 禁止：手动 SetActiveCamera + InputMgr.Push
- ✅ 正确：AutoSwitchOnContextChange + RegisterCameraState（自动联动）

---

**文档结束。实现阶段请严格遵循本契约。**
