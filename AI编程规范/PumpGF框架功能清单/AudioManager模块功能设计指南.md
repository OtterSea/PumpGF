# Audio Manager 模块功能设计指南

> **文档定位**：本文件是 Audio Manager 模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、API 契约与数据结构，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的公开 API 语义**。
>
> **模块编号**：2-2。
> **前置依赖**：ResMgr（加载 AudioClip）、ConfigMgr（加载 AudioConfigSO）、Lifecycle、DOTween。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 |
|--------|------|
| 1. AudioSource 池 | AudioMgr 内部维护（不依赖 PoolMgr，因音频池有特殊需求） |
| 2. 淡入淡出 | DOTween Pro（框架依赖，已引入 Vendor） |
| 3. 音量组 | BGM / SFX / UI / Voice 四组，独立音量 + 静音 |
| 4. 3D 空间音效 | 支持（`Play3D(key, position)`） |
| 5. 资源加载 | BGM 按需加载（切换时释放旧的）；SFX 预加载（按标签） |
| 6. SO 配置 | `AudioConfigSO` 定义音频条目元数据 |
| 7. 暂停感知 | 提供 `SetPaused(bool)` 接口，业务层决定是否启用 |
| 8. API 形态 | `Play(key)` 返回 `IAudioHandle`（可忽略，Fire-and-Forget） |
| 9. 路由方式 | 音频类型由配置决定，`Play(key)` 统一入口自动路由到 BGM/SFX/UI/Voice 通道 |
| 风格 | `AudioMgr : IModule`，纳入 `GameGlobal`，纯 C# 类 |
| 底层 | uGUI AudioSource + R3 + DOTween + UniTask |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**Audio Manager 是游戏音频的统一播放与管理中枢**——
通过 SO 配置驱动，业务只需 `Play(key)` 即可播放，AudioMgr 根据配置自动路由到 BGM/SFX/UI/Voice 通道。
负责音量组、池化、淡入淡出、3D 空间音效，但**不实现音频内容的业务逻辑**。

### 1.2 职责清单

| 职责 | 说明 |
|------|------|
| 统一播放入口 | `Play(key)` 根据配置自动路由（BGM/SFX/UI/Voice） |
| 音量组管理 | 四组独立音量 + 静音，ReactiveProperty 供 UI 绑定 |
| BGM 管理 | 单曲播放、淡入淡出切换、循环 |
| SFX/UI/Voice 管理 | 池化 AudioSource、并发播放、Fire-and-Forget |
| 3D 空间音效 | 支持按世界位置播放 |
| AudioSource 池 | 内部维护池，播放完自动回收 |
| 资源加载 | BGM 按需、SFX 预加载（通过 ResMgr） |
| 暂停接口 | `SetPaused(bool)`，业务层决定启用时机 |

### 1.3 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ 音频内容制作 | 业务/美术负责 |
| ❌ 音频混音/效果器 | 本阶段不做（未来可接 AudioMixer） |
| ❌ 音频可视化/频谱分析 | 过重，未来需求 |
| ❌ 语音识别/TTS | 不在范围 |
| ❌ 自动暂停感知 | 只提供接口，由业务决定调用时机 |

---

## 2. 整体架构

```
┌─────────────────────────────────────────┐
│            业务逻辑层                    │
│  AudioMgr.Play("AttackHit")             │
│  AudioMgr.Play("BGM_Battle")            │
└──────────────────┬──────────────────────┘
                   │ Play(key)
                   ▼
┌─────────────────────────────────────────┐
│           AudioMgr (IModule)             │
│  ┌──────────────────────────────────┐   │
│  │ 配置查找表                         │   │
│  │ key → AudioEntry                  │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ 路由器（根据 entry.Type 分发）    │   │
│  │  BGM → BgmPlayer                  │   │
│  │  SFX → SfxPoolPlayer              │   │
│  │  UI  → SfxPoolPlayer(UI 组)       │   │
│  │  Voice → SfxPoolPlayer(Voice 组)  │   │
│  └──────────────────────────────────┘   │
│  ┌──────────┐ ┌──────────────────────┐  │
│  │BgmPlayer │ │SfxPoolPlayer         │  │
│  │(淡入淡出)│ │(AudioSource 池)     │  │
│  └──────────┘ └──────────────────────┘  │
│  ┌──────────────────────────────────┐   │
│  │ VolumeGroup (4组 ReactiveProperty)│   │
│  └──────────────────────────────────┘   │
└──────────────────┬──────────────────────┘
                   │ LoadAssetAsync<AudioClip>
                   ▼
┌─────────────────────────────────────────┐
│              ResMgr / Addressables       │
└─────────────────────────────────────────┘

        │ 配置加载
        ▼
┌─────────────────┐
│ AudioConfigSO   │  (SO，定义 key→元数据)
└─────────────────┘
```

---

## 3. 音频配置（AudioConfigSO）

### 3.1 设计原则

- **配置驱动**：业务只需 `Play(key)`，音频类型（BGM/SFX/UI/Voice）由配置决定。
- **AddressablesKey 存储**：AudioEntry 存 Addressables key（字符串），运行时按需加载 AudioClip，不直接持有 AudioClip 引用（避免 AudioConfigSO 加载时全量加载 clip）。

### 3.2 AudioConfigSO 结构

```
[CreateAssetMenu(menuName = "PumpGF/Audio/AudioConfig")]
public class AudioConfigSO : ScriptableObject
{
    public List<AudioEntry> Entries;
}
```

### 3.3 AudioEntry 结构

```
[Serializable]
public class AudioEntry
{
    public string Key;              // 业务用的 key，如 "AttackHit"、"BGM_Battle"
    public string AddressablesKey;  // Addressables 中 AudioClip 的 key
    public AudioType Type;          // BGM / SFX / UI / Voice（决定路由）
    [Range(0f, 1f)] public float Volume = 1f;   // 默认音量（0~1，与音量组相乘）
    [Range(-3f, 3f)] public float Pitch = 1f;   // 默认音调
    public bool Is3D;               // 是否 3D 空间音效（SFX/UI/Voice 有效）
    [Range(0f, 1f)] public float SpatialBlend = 1f;  // 3D 混合度（0=2D, 1=3D）
    public string PreloadLabel;      // 预加载标签（可选，用于分组预加载）
}

public enum AudioType { BGM, SFX, UI, Voice }
```

### 3.4 配置加载

- AudioMgr 启动时加载默认 `AudioConfigSO`（通过 ResMgr，key = `"AudioConfig"`）。
- 支持运行时加载额外配置：`LoadConfigAsync(configKey)`。
- 所有配置的 entry 合并到查找表（`Dictionary<string, AudioEntry>`）。
- key 重复时 Warning，后加载的覆盖。

### 3.5 AudioClip 加载策略

| 类型 | 加载时机 | 释放时机 |
|------|----------|----------|
| BGM | `Play` 时按需加载 | 切换/停止时释放（引用计数，AssetHandle.Dispose） |
| SFX | 预加载（`PreloadSfxAsync(label)`）或首次播放懒加载 | AudioMgr.Dispose 时统一释放 |
| UI/Voice | 同 SFX | 同 SFX |

> **BGM 按需**：一次只持有一个 BGM 的 AssetHandle，切换时 Dispose 旧的。
> **SFX 预加载**：常用 SFX 启动预加载，避免首次播放卡顿。

---

## 4. 音量组管理

### 4.1 四组音量

| 组 | 用途 | 默认音量 |
|----|------|----------|
| BGM | 背景音乐 | 1.0 |
| SFX | 音效 | 1.0 |
| UI | UI 交互音效 | 1.0 |
| Voice | 语音/配音 | 1.0 |

### 4.2 ReactiveProperty 暴露

每组用 `ReactiveProperty<float>` 持有音量，`ReactiveProperty<bool>` 持有静音状态，供 UI 绑定：

```
public class AudioMgr
{
    public ReactiveProperty<float> BgmVolume { get; } = new(1f);
    public ReactiveProperty<bool> BgmMuted { get; } = new(false);
    // SFX / UI / Voice 同理
}
```

UI 绑定示例：
```
AudioMgr.BgmVolume.Subscribe(v => bgmSlider.value = v).AddTo(this);
bgmSlider.OnValueChangedAsObservable().Subscribe(v => AudioMgr.BgmVolume.Value = v).AddTo(this);
```

### 4.3 有效音量计算

播放时，AudioSource 的实际音量 = `entry.Volume * groupVolume * (groupMuted ? 0 : 1)`。

### 4.4 音量持久化

音量/静音状态通过 GameDataStore 的 `SettingsData` 持久化：
```
public class SettingsData
{
    public ReactiveProperty<float> BgmVolume { get; } = new(1f);
    public ReactiveProperty<bool> BgmMuted { get; } = new(false);
    // ...
}
```
AudioMgr 订阅 GameDataStore 的音量变化，自动同步。

---

## 5. BGM 播放（淡入淡出）

### 5.1 BGM 特性

- 同一时间只播放一个 BGM。
- 支持 `Play(bgmKey)` 切换（淡出旧 → 淡入新）。
- 循环播放。
- DOTween 实现 `AudioSource.DOFade`。

### 5.2 切换流程

```
Play(bgmKey):
  1. 若当前有 BGM 在播:
     a. DOTween 淡出当前 BGM（DOFade(0, fadeOutDuration)）
     b. 淡出完成后 Stop + Dispose 旧 AssetHandle
  2. LoadAssetAsync<AudioClip>(entry.AddressablesKey) 加载新 BGM
  3. 分配 BGM 专用 AudioSource，设置 clip + 循环
  4. DOTween 淡入（volume 0 → target，fadeInDuration）
  5. Play
```

### 5.3 BGM 控制

```
void StopBgm(float fadeOutDuration = 1f);   // 淡出停止
void PauseBgm();                              // 暂停（不淡出）
void ResumeBgm();                             // 恢复
```

---

## 6. SFX/UI/Voice 播放（池化并发）

### 6.1 SFX 特性

- 可并发播放（同一 key 可同时播多个）。
- Fire-and-Forget：播放完自动回收 AudioSource。
- 池化 AudioSource，避免频繁创建销毁。

### 6.2 AudioSource 池

AudioMgr 内部维护 AudioSource 池：
- 池容量可配（默认 16）。
- 每个池化的 AudioSource 挂在一个隐藏 GameObject 上。
- 播放时从池取，设置 clip/volume/pitch/3D，播放。
- 播放完（`AudioSource.isPlaying == false`）自动回池。
- 池满时等待空闲或扩容（可配上限）。

### 6.3 播放流程

```
Play(sfxKey):
  1. 查找 entry（配置）
  2. 确保 AudioClip 已加载（懒加载或预加载命中）
  3. 从池取 AudioSource
  4. 设置 clip + volume（entry.Volume * groupVolume * !muted）+ pitch + 3D
  5. Play
  6. 注册"播放完回收"检查（每帧或协程检查 isPlaying）
  7. 返回 IAudioHandle（业务可忽略）
```

### 6.4 并发限制

- 同一 key 可配最大并发数（避免同音效叠加轰炸）。
- 超过并发数时丢弃最旧的或拒绝播放（可配策略）。

---

## 7. 3D 空间音效

### 7.1 3D 播放

```
IAudioHandle Play3D(string key, Vector3 position);
IAudioHandle Play3D(string key, Transform followTarget);  // 跟随物体移动
```

- `Is3D = true` 的 entry 用 3D AudioSource 播放。
- 设置 `spatialBlend = 1`（全 3D）。
- AudioSource 所在 GameObject 移到指定位置或跟随目标。

### 7.2 配置标记

- `AudioEntry.Is3D`：标记是否 3D。
- `AudioEntry.SpatialBlend`：3D 混合度（0=2D, 1=3D）。
- BGM 不支持 3D（始终 2D 全局）。

---

## 8. 暂停感知

### 8.1 接口设计

```
void SetPaused(bool paused);
```

- `paused = true`：所有 AudioSource 静音（或降低音量，可配策略）。
- `paused = false`：恢复原音量。
- **业务层决定调用时机**（可订阅 Lifecycle 的 PauseProfile 变化）。

### 8.2 典型用法

```
// 业务订阅 Lifecycle 暂停事件
GameGlobal.Lifecycle.ObserveChannelPaused(UpdateChannel.Logic)
    .Subscribe(paused => GameGlobal.AudioMgr.SetPaused(paused))
    .AddTo(this);
```

### 8.3 暂停策略

| 策略 | 行为 |
|------|------|
| Mute | 暂停时音量设为 0，恢复时还原 |
| LowerVolume | 暂停时降低到 0.3，恢复时还原 |

策略可配（`AudioConfig.PauseStrategy`）。

---

## 9. Fire-and-Forget + IAudioHandle

### 9.1 Play 返回 IAudioHandle

```
IAudioHandle Play(string key);  // 统一入口，配置路由
```

- 业务可忽略返回值（Fire-and-Forget）。
- 需要控制时使用 handle。

### 9.2 IAudioHandle

```
public interface IAudioHandle
{
    bool IsValid { get; }       // 是否仍在播放
    float Volume { get; set; }   // 实时音量（0~1，叠加在组音量上）
    void Stop(float fadeOut = 0f); // 停止（可选淡出）
    void Pause();
    void Resume();
}
```

- BGM 的 handle 支持 Stop（淡出停止）。
- SFX 的 handle 支持 Stop（立即停止 + 回收）。
- Handle 失效后（播放完自动回收）操作为 Noop + Warning。

---

## 10. API 契约（公开接口）

### 10.1 AudioMgr

```
class AudioMgr : IModule
{
    // ── 播放（统一入口，配置路由）──
    IAudioHandle Play(string key);                          // 2D 播放
    IAudioHandle Play3D(string key, Vector3 position);     // 3D 定点
    IAudioHandle Play3D(string key, Transform followTarget);// 3D 跟随

    // ── BGM 专用控制 ──
    void StopBgm(float fadeOutDuration = 1f);
    void PauseBgm();
    void ResumeBgm();
    bool IsBgmPlaying { get; }
    string CurrentBgmKey { get; }

    // ── 音量组 ──
    ReactiveProperty<float> BgmVolume { get; }
    ReactiveProperty<float> SfxVolume { get; }
    ReactiveProperty<float> UiVolume { get; }
    ReactiveProperty<float> VoiceVolume { get; }
    ReactiveProperty<bool> BgmMuted { get; }
    ReactiveProperty<bool> SfxMuted { get; }
    ReactiveProperty<bool> UiMuted { get; }
    ReactiveProperty<bool> VoiceMuted { get; }

    // ── 暂停 ──
    void SetPaused(bool paused);

    // ── 配置加载 ──
    UniTask LoadConfigAsync(string configKey, CancellationToken ct = default);
    void UnloadConfig(string configKey);

    // ── 预加载 ──
    UniTask PreloadSfxAsync(string label, IProgress<float> progress = null, CancellationToken ct = default);

    // ── 查询 ──
    bool HasAudioEntry(string key);
    bool IsClipLoaded(string key);

    // ── IModule ──
    void Init();
    void Dispose();
}
```

### 10.2 IAudioHandle

```
public interface IAudioHandle
{
    bool IsValid { get; }
    float Volume { get; set; }
    void Stop(float fadeOut = 0f);
    void Pause();
    void Resume();
}
```

### 10.3 AudioConfigSO / AudioEntry

```
class AudioConfigSO : ScriptableObject
{
    List<AudioEntry> Entries;
}

[Serializable]
class AudioEntry
{
    string Key;
    string AddressablesKey;
    AudioType Type;
    float Volume;
    float Pitch;
    bool Is3D;
    float SpatialBlend;
    string PreloadLabel;
}

enum AudioType { BGM, SFX, UI, Voice }
```

---

## 11. 使用示例（伪代码）

### 11.1 基础播放

```
// 播放音效（类型由配置决定）
GameGlobal.AudioMgr.Play("AttackHit");       // 若配置标记为 SFX，走 SFX 通道
GameGlobal.AudioMgr.Play("BGM_Battle");       // 若配置标记为 BGM，走 BGM 通道（淡入淡出切换）

// 3D 空间音效
GameGlobal.AudioMgr.Play3D("Explosion", enemyPosition);

// 跟随物体
GameGlobal.AudioMgr.Play3D("Footstep", playerTransform);
```

### 11.2 BGM 控制

```
// 切换 BGM（自动淡出旧的、淡入新的）
GameGlobal.AudioMgr.Play("BGM_Battle");

// 停止 BGM（淡出）
GameGlobal.AudioMgr.StopBgm(fadeOutDuration: 2f);

// 暂停/恢复 BGM
GameGlobal.AudioMgr.PauseBgm();
GameGlobal.AudioMgr.ResumeBgm();
```

### 11.3 音量控制（UI 绑定）

```
// UI 绑定音量滑块
GameGlobal.AudioMgr.BgmVolume
    .Subscribe(v => bgmSlider.value = v)
    .AddTo(this);

bgmSlider.OnValueChangedAsObservable()
    .Subscribe(v => GameGlobal.AudioMgr.BgmVolume.Value = v)
    .AddTo(this);

// 静音按钮
muteButton.OnClickAsObservable()
    .Subscribe(_ => GameGlobal.AudioMgr.BgmMuted.Value = !GameGlobal.AudioMgr.BgmMuted.Value)
    .AddTo(this);
```

### 11.4 暂停感知（业务决定）

```
// 订阅 Lifecycle 暂停，联动音频
GameGlobal.Lifecycle.ObserveChannelPaused(UpdateChannel.Logic)
    .Subscribe(paused => GameGlobal.AudioMgr.SetPaused(paused))
    .AddTo(this);
```

### 11.5 预加载 SFX

```
// 预加载战斗音效
await GameGlobal.AudioMgr.PreloadSfxAsync(
    "Audio/Battle",
    progress: Progress.Create(p => loadingBar.value = p));
```

### 11.6 IAudioHandle 控制

```
// 播放并控制
var handle = GameGlobal.AudioMgr.Play("ChargeUp");
// ... 充能中 ...
if (interrupted)
    handle.Stop(fadeOut: 0.2f);
else
    handle.Stop();  // 充能完成
```

### 11.7 AudioConfigSO 配置示例

```
// AudioConfigSO 资产中的条目示例：
// Key: "AttackHit"     AddressablesKey: "Audio/SFX/AttackHit"     Type: SFX   Is3D: false
// Key: "Explosion"     AddressablesKey: "Audio/SFX/Explosion"     Type: SFX   Is3D: true   SpatialBlend: 1
// Key: "BGM_Battle"    AddressablesKey: "Audio/BGM/BattleTheme"    Type: BGM   Is3D: false
// Key: "ButtonClick"   AddressablesKey: "Audio/UI/ButtonClick"     Type: UI    Is3D: false
// Key: "Voice_Greeting" AddressablesKey: "Audio/Voice/Greeting"   Type: Voice Is3D: false
```

---

## 12. 实现检查清单

- [ ] `AudioMgr : IModule`，纳入 GameGlobal
- [ ] `AudioConfigSO` + `AudioEntry` 结构定义
- [ ] 配置查找表（`Dictionary<string, AudioEntry>`），支持多配置合并
- [ ] `Play(key)` 统一入口，根据 `entry.Type` 路由
- [ ] BGM 播放器：淡入淡出切换（DOTween `DOFade`）
- [ ] BGM 按需加载（AssetHandle），切换时释放旧的
- [ ] SFX/UI/Voice 播放器：AudioSource 池
- [ ] AudioSource 池：播放完自动回收
- [ ] 3D 空间音效：`Play3D(key, position)` / `Play3D(key, Transform)`
- [ ] 四组音量 `ReactiveProperty` + 静音 `ReactiveProperty`
- [ ] 有效音量 = `entry.Volume * groupVolume * !muted`
- [ ] `SetPaused(bool)` 接口
- [ ] `IAudioHandle` 接口（IsValid/Volume/Stop/Pause/Resume）
- [ ] `PreloadSfxAsync(label)` 按标签预加载
- [ ] `LoadConfigAsync` / `UnloadConfig` 多配置支持
- [ ] 并发限制（同 key 最大并发数，可配）
- [ ] 音量持久化（订阅 GameDataStore.SettingsData）
- [ ] `Dispose` 清理所有 AudioSource 池、AssetHandle
- [ ] 所有公开 API 有中文 XML 注释
- [ ] 无硬编码（池容量、淡入淡出时长等走配置）

---

## 13. 依赖关系

| 依赖项 | 方向 | 说明 |
|--------|------|------|
| ResMgr | 引用 | 加载 AudioClip（BGM 按需、SFX 预加载） |
| ConfigMgr | 引用 | 加载 AudioConfigSO |
| GameDataStore | 引用 | 订阅音量/静音设置持久化 |
| Lifecycle | 可选引用 | 业务订阅暂停事件联动（非直接依赖） |
| R3 | 引用 | ReactiveProperty（音量/静音） |
| DOTween | 引用 | BGM 淡入淡出 |
| UniTask | 引用 | 异步加载/预加载 |
| GameGlobal | 被引用 | 暴露 AudioMgr |

> **初始化顺序**：... → AudioMgr（在 ResMgr/ConfigMgr/GameDataStore 之后）

---

## 14. 后续模块依赖本模块的接口

| 后续模块 | 使用的 AudioMgr 接口 |
|----------|---------------------|
| UI Framework (2-1) | `Play("ButtonClick")` UI 音效 |
| Input Manager (2-3) | 输入反馈音效 |
| Level/Scene Manager (3-3) | BGM 切换、场景音效 |
| 业务层 | 所有音频播放 |

---

## 15. 关键使用规范（业务层 Coding AI 必读）

### 15.1 用 Play(key) 统一入口

- ❌ 禁止：`PlayBgm(key)` / `PlaySfx(key)` 显式调用（类型由配置决定）
- ✅ 正确：`AudioMgr.Play("AttackHit")`（配置标记为 SFX 自动路由）

### 15.2 音频必须配置才能播放

- 所有音频必须在 `AudioConfigSO` 中注册。
- 未注册的 key → Warning + 不播放。

### 15.3 BGM 切换走 Play（自动淡入淡出）

- ❌ 禁止：手动 StopBgm + Play 实现切换
- ✅ 正确：`Play("BGM_Battle")` 自动淡出旧 BGM + 淡入新 BGM

### 15.4 音量设置走 GameDataStore

- ❌ 禁止：业务直接改 `AudioMgr.BgmVolume.Value`
- ✅ 正确：改 `GameDataStore.Settings.BgmVolume.Value`，AudioMgr 自动同步

### 15.5 高频 SFX 考虑并发限制

- 同一 SFX 短时间内大量播放会叠加轰炸。
- 在 AudioEntry 里配置最大并发数。

### 15.6 3D 音效必须标记 Is3D

- 配置里 `Is3D = true` + `SpatialBlend = 1`。
- 用 `Play3D(key, position)` 播放，不能用 `Play`。

---

**文档结束。实现阶段请严格遵循本契约。**
