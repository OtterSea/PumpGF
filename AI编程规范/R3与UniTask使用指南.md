# R3 与 UniTask 使用指南

## 它们是什么

| 库 | 一句话 | 解决什么问题 |
|----|--------|-------------|
| **UniTask** | Unity 专用的零分配 async/await | 替代协程，让异步代码可 await、可取消、可组合 |
| **R3** | 现代版 Reactive Extensions for C# | 事件流、响应式属性、UI 绑定、帧驱动运算 |

两者都是 Cysharp 开发，设计上互补，**不需要封装，直接使用**。

## 什么时候用哪个

```
你要做的事情
├── "等一个异步操作完成" → UniTask
│   ├── 加载资源、网络请求、延迟、动画完成
│   └── 需要并发（WhenAll）、超时、取消
│
├── "监听一个持续的事件流" → R3
│   ├── 按钮点击、属性变化、每帧检测
│   └── 需要 debounce、filter、组合多个事件源
│
└── "两者混合" → R3 的 SubscribeAwait / SelectAwait
    └── 事件触发后执行异步操作（如点击按钮 → 发网络请求）
```

## 基础引用

```csharp
using R3;                              // R3 核心
using Cysharp.Threading.Tasks;         // UniTask 核心
using Cysharp.Threading.Tasks.Linq;    // UniTask 异步 LINQ（如需要）
```

---

## UniTask 篇

### 1. 替代协程：延迟与等待

**场景**：等 2 秒后执行操作。

```csharp
// ❌ 旧协程方式
IEnumerator DelayCoroutine()
{
    yield return new WaitForSeconds(2f);
    DoSomething();
}
StartCoroutine(DelayCoroutine());

// ✅ UniTask 方式
async UniTaskVoid DelayAsync()
{
    await UniTask.Delay(TimeSpan.FromSeconds(2f));
    DoSomething();
}
DelayAsync().Forget();
```

### 2. 等待条件成立

**场景**：等待某个 flag 变为 true。

```csharp
await UniTask.WaitUntil(() => isReady);
// 或者等待值变化
await UniTask.WaitUntilValueChanged(this, x => x.IsReady);
```

### 3. 等待下一帧 / 固定更新

```csharp
await UniTask.Yield();               // 下一帧（等价于 yield return null）
await UniTask.NextFrame();           // 保证下一帧
await UniTask.WaitForFixedUpdate();  // 等待 FixedUpdate
await UniTask.WaitForEndOfFrame();   // 等待帧末（Unity 2023.1+ 无需 MonoBehaviour）
```

### 4. 异步加载资源

**场景**：Resources.LoadAsync 并等待完成。

```csharp
async UniTask<Sprite> LoadSpriteAsync(string path, CancellationToken ct)
{
    var req = await Resources.LoadAsync<Sprite>(path).WithCancellation(ct);
    return req as Sprite;
}
```

### 5. 取消与生命周期管理

**场景**：GameObject 销毁时自动取消异步操作。

```csharp
// Unity 2022.2+ 直接用 destroyCancellationToken
async UniTaskVoid DoWorkAsync()
{
    await UniTask.Delay(TimeSpan.FromSeconds(5f), cancellationToken: destroyCancellationToken);
    Debug.Log("Done");
}

// 旧版本或需要更细粒度控制
async UniTaskVoid DoWorkAsync()
{
    var cts = new CancellationTokenSource();
    // 手动取消
    cancelButton.onClick.AddListener(() => cts.Cancel());
    await SomeAsyncOperation(cts.Token);
}
```

### 6. 超时

```csharp
async UniTask<string> FetchWithTimeoutAsync()
{
    var cts = new CancellationTokenSource();
    cts.CancelAfterSlim(TimeSpan.FromSeconds(5f)); // 用 PlayerLoop，不占线程
    try
    {
        return await DoNetworkRequestAsync(cts.Token);
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested)
    {
        return null; // 超时
    }
}
```

### 7. 并发执行

**场景**：同时加载三个资源，全部完成后再继续。

```csharp
async UniTaskVoid LoadAllAsync()
{
    var (a, b, c) = await UniTask.WhenAll(
        LoadSpriteAsync("icon1"),
        LoadSpriteAsync("icon2"),
        LoadSpriteAsync("icon3")
    );
    // 三个都加载完成
}
```

### 8. MonoBehaviour 中启动异步

```csharp
public class MyBehaviour : MonoBehaviour
{
    void Start()
    {
        // fire and forget，不需要 await
        InitializeAsync().Forget();
    }

    async UniTaskVoid InitializeAsync()
    {
        await UniTask.DelayFrame(1);
        // ...
    }
}
```

### 9. async void 替代品

```csharp
// ❌ 不要用 async void
async void OnClick() { ... }

// ✅ 用 UniTaskVoid + Forget
async UniTaskVoid OnClickAsync()
{
    await UniTask.Delay(TimeSpan.FromSeconds(1f));
}
void OnClick() => OnClickAsync().Forget();

// ✅ 或者注册事件时
button.onClick.AddListener(async () =>
{
    await UniTask.Delay(TimeSpan.FromSeconds(1f));
});
// 更好的写法：用 UniTask.UnityAction
button.onClick.AddListener(UniTask.UnityAction(async () =>
{
    await UniTask.Delay(TimeSpan.FromSeconds(1f));
}));
```

---

## R3 篇

### 1. 事件：Subject

**场景**：自己定义一个事件，多处监听。

```csharp
// 声明
public Subject<int> OnScoreChanged = new Subject<int>();

// 发送事件
OnScoreChanged.OnNext(100);

// 监听（记得 Dispose 或用 AddTo）
OnScoreChanged.Subscribe(score => Debug.Log($"Score: {score}"))
    .AddTo(this); // 绑定到 MonoBehaviour 生命周期
```

### 2. 响应式属性：ReactiveProperty

**场景**：HP 变化时自动更新 UI。

```csharp
public class Player
{
    public ReactiveProperty<int> Hp { get; } = new(100);
    public ReadOnlyReactiveProperty<bool> IsDead { get; }

    public Player()
    {
        IsDead = Hp.Select(x => x <= 0).ToReadOnlyReactiveProperty();
    }
}

// 使用
player.Hp.Value = 50;              // 设置值
player.Hp.Subscribe(hp => UpdateHpUI(hp));  // 监听变化
player.IsDead.Subscribe(dead => { if (dead) OnPlayerDeath(); });
```

### 3. Unity 事件转 Observable

**场景**：监听按钮点击。

```csharp
// uGUI 事件
button.OnClickAsObservable()
    .Subscribe(_ => Debug.Log("Clicked"))
    .AddTo(this);

slider.OnValueChangedAsObservable()
    .Subscribe(value => Debug.Log($"Slider: {value}"))
    .AddTo(this);

// MonoBehaviour 事件
this.OnCollisionEnterAsObservable()
    .Subscribe(collision => Debug.Log("Collided!"))
    .AddTo(this);

// UnityEvent 转 Observable（可绑定 CancellationToken）
var observable = onSomeUnityEvent.AsObservable(destroyCancellationToken);
```

### 4. 每帧检测：EveryUpdate + EveryValueChanged

**场景**：每帧检查玩家是否在地面。

```csharp
// 每帧执行
Observable.EveryUpdate()
    .Subscribe(_ => { /* 每帧执行 */ })
    .AddTo(this);

// 值变化时才触发（自动优化，不需要每帧手动比较）
Observable.EveryValueChanged(this, x => x.IsGrounded)
    .Where(grounded => grounded)
    .Subscribe(_ => Debug.Log("Landed!"))
    .AddTo(this);
```

### 5. 节流与防抖

**场景**：搜索框输入，停止输入 500ms 后才搜索。

```csharp
inputField.OnValueChangedAsObservable()
    .Debounce(TimeSpan.FromSeconds(0.5f))  // 停止 500ms 后才发出
    .Subscribe(text => Search(text))
    .AddTo(this);

// 或帧级防抖
Observable.EveryUpdate()
    .ThrottleLastFrame(30)  // 每 30 帧采样一次
    .Subscribe(_ => { })
    .AddTo(this);
```

### 6. 组合多个事件源

**场景**：玩家同时按下攻击键且不在冷却中时攻击。

```csharp
// CombineLatest：任一源变化时，用最新值组合
var canAttack = attackCooldown.Select(cd => cd <= 0);
var attackInput = attackButton.OnClickAsObservable();

canAttack
    .Where(can => can)
    .CombineLatest(attackInput, (can, click) => (can, click))
    .Where(x => x.can && x.click)
    .Subscribe(_ => Attack())
    .AddTo(this);
```

### 7. SerializableReactiveProperty（可在 Inspector 中序列化）

```csharp
public class Enemy : MonoBehaviour
{
    public SerializableReactiveProperty<int> hp = new(100);
}

// Inspector 中可直接编辑初始值，代码中 .Value 修改会触发监听
```

### 8. 订阅生命周期管理

```csharp
// 方式1：AddTo 绑定到 MonoBehaviour（推荐，简洁）
Observable.EveryUpdate()
    .Subscribe(_ => { })
    .AddTo(this); // this = MonoBehaviour，销毁时自动取消

// 方式2：DisposableBag（多订阅时高效）
DisposableBag bag = default;
Observable.EveryUpdate().Subscribe().AddTo(ref bag);
Observable.IntervalFrame(60).Subscribe().AddTo(ref bag);
bag.Dispose(); // 一次性取消所有

// 方式3：destroyCancellationToken（与 UniTask 统一）
Observable.EveryUpdate()
    .Subscribe(_ => { })
    .RegisterTo(destroyCancellationToken);
```

---

## R3 + UniTask 协作篇

### 1. 事件触发异步操作：SubscribeAwait

**场景**：点击按钮 → 发网络请求（异步），期间忽略新点击。

```csharp
button.OnClickAsObservable()
    .SelectAwait(async (_, ct) =>
    {
        var result = await FetchDataAsync(ct);
        return result;
    }, AwaitOperation.Drop) // 正在执行时丢弃新点击
    .Subscribe(result => ShowResult(result))
    .AddTo(this);
```

### 2. Observable 转 async/await

```csharp
// 等待下一个按钮点击
await button.OnClickAsObservable().FirstAsync();

// 等待事件流完成
await someSubject.OnCompletedAsync();
```

### 3. UniTask 异步循环 + R3 事件

**场景**：循环等待玩家点击，每次点击执行异步操作。

```csharp
// 等价于 Repeat，但更灵活
while (!destroyCancellationToken.IsCancellationRequested)
{
    await button.OnClickAsObservable()
        .Take(1)
        .ForEachAsync(_ => { });

    await DoSomethingAsync(destroyCancellationToken);
}
```

---

## 速查表

| 需求 | 用什么 | 关键 API |
|------|--------|----------|
| 等 N 秒 / N 帧 | UniTask | `UniTask.Delay` / `UniTask.DelayFrame` |
| 等待条件 | UniTask | `UniTask.WaitUntil` / `WaitUntilValueChanged` |
| 并发等待多个 | UniTask | `UniTask.WhenAll` |
| 超时 | UniTask | `CancellationTokenSource.CancelAfterSlim` |
| 监听按钮 / UI 事件 | R3 | `OnClickAsObservable()` |
| 属性变化通知 | R3 | `ReactiveProperty<T>` |
| 每帧检测值变化 | R3 | `EveryValueChanged` |
| 防抖 / 节流 | R3 | `Debounce` / `ThrottleLastFrame` |
| 事件流组合 | R3 | `CombineLatest` / `Merge` |
| 事件 → 异步操作 | R3+UniTask | `SelectAwait` / `SubscribeAwait` |
| 取消异步 | UniTask | `CancellationToken` / `destroyCancellationToken` |
| 订阅自动取消 | R3 | `.AddTo(this)` / `RegisterTo(ct)` |

## NuGet 包依赖说明

PumpGF 已内置 R3 核心 DLL（`R3.dll`、`Microsoft.Bcl.TimeProvider.dll`、`Microsoft.Bcl.AsyncInterfaces.dll`），位于 `Runtime/Plugins/`。

R3 的 Unity 适配层（`com.cysharp.r3`）和 UniTask（`com.cysharp.unitask`）通过 Git URL 在 `manifest.json` 中引用。新项目使用 PumpGF 时，需在 `manifest.json` 中同时添加以下条目：

```json
"com.pumpgf.framework": "https://github.com/<user>/PumpGF.git",
"com.cysharp.r3": "https://github.com/Cysharp/R3.git?path=src/R3.Unity/Assets/R3.Unity",
"com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask"
```
