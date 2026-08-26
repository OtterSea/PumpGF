# R3 与 UniTask 最佳实践

> **文档定位**：给 Coding AI 提供 R3/UniTask 在**真实业务场景下的标准正例**及高频误用反例。R3/UniTask 是 Cysharp 开发的库，框架不封装，直接使用。与《R3与UniTask使用指南》配合。
> **写作依据**：2026-08 项目实战反哺——这是项目**最头疼的错误类型**（异步/生命周期/时序竞态）的正面教材。
> **优先读**：AI 写任何 async/await、订阅、异步加载、延迟、事件流代码前，先读本文。**这是最容易运行时翻车的地方。**

---

## 0. 一句话原则

**所有异步/订阅必须带取消（`destroyCancellationToken` 或 `AddTo(this)` 或 CTS 工厂）；禁用 `async void`、禁用协程、禁用裸 `new CancellationTokenSource()` 不 Dispose；延迟用 `UniTask.Delay`，禁用 `WaitForSeconds`。**

---

## 1. 标准正例集

### 1.1 异步延迟 → async UniTaskVoid + destroyCancellationToken

**场景**：等 2 秒后执行。

```csharp
// ✅ 正确：Fire-and-forget + 生命周期取消
async UniTaskVoid DelayedWork()
{
    await UniTask.Delay(TimeSpan.FromSeconds(2f), cancellationToken: destroyCancellationToken);
    DoSomething();
}
DelayedWork().Forget();
```

```csharp
// ❌ 反例 1：协程
IEnumerator DelayCoroutine()
{
    yield return new WaitForSeconds(2f);
    DoSomething();
}
StartCoroutine(DelayCoroutine());   // 违反铁律-禁协程；且停止协程要手动管

// ❌ 反例 2：async UniTaskVoid 无取消 —— 对象销毁后 await 继续，访问已销毁对象报错
async UniTaskVoid DelayedWork()
{
    await UniTask.Delay(TimeSpan.FromSeconds(2f));  // 无取消 token，销毁后仍继续
    DoSomething();   // 若 GameObject 已销毁 → MissingReferenceException
}
```

> **为什么**：`destroyCancellationToken` 在 GameObject 销毁时自动取消 → `UniTask.Delay` 抛 OCE → 异步链安全终止，不会访问已销毁对象。漏掉它，是最典型的"运行时才炸"的坑。

### 1.2 异步加载资源 → 带取消

**场景**：加载资源并等待完成。

```csharp
// ✅ 正确
async UniTask<Sprite> LoadSpriteAsync(string path, CancellationToken ct)
{
    var req = await Resources.LoadAsync<Sprite>(path).WithCancellation(ct);
    return req as Sprite;
}
// 调用时传 destroyCancellationToken 或 CTS 工厂生成的 token
```

```csharp
// ❌ 反例：加载不带取消，对象销毁后加载仍完成 → 拿到资源无法用
async UniTask<Sprite> LoadSpriteAsync(string path)
{
    var req = await Resources.LoadAsync<Sprite>(path);
    return req as Sprite;   // 没传 ct
}
```

### 1.3 取消源生命周期 → 用 destroyCancellationToken 或 CTS 工厂，不裸 new

**场景**：手动管理一个异步操作的取消。

```csharp
// ✅ 正例 1（首选）：Unity 内置 destroyCancellationToken，无需自己管 CTS
await UniTask.Delay(TimeSpan.FromSeconds(5f), cancellationToken: destroyCancellationToken);

// ✅ 正例 2（框架 CTS 工厂）：绑定 GameObject,销毁自动 Cancel + Dispose
var ct = GameGlobal.Lifecycle.CreateLinkedToken(gameObject);
await SomeAsync(ct);
```

```csharp
// ❌ 反例：裸 new CTS 忘记 Dispose → 高频内存泄漏
var cts = new CancellationTokenSource();
cts.CancelAfterSlim(TimeSpan.FromSeconds(5f));
try { await SomeAsync(cts.Token); }
finally { cts.Dispose(); }   // 一漏就泄漏；且手动管易错
```

> **为什么**：`destroyCancellationToken`/`CreateLinkedToken(gameObject)` 把取消源的创建和释放交给生命周期托管，从机制上消灭"手写 CTS 忘 Dispose"这个高频泄漏。

### 1.4 async void → 一律换 UniTaskVoid + Forget

**场景**：事件回调里做异步操作。

```csharp
// ✅ 正确：用 UniTaskVoid + Forget
void OnClick() => OnClickAsync().Forget();
async UniTaskVoid OnClickAsync()
{
    await UniTask.Delay(TimeSpan.FromSeconds(1f));
    DoSomething();
}

// ✅ 或注册 Unity 事件时用 UniTask.UnityAction
button.onClick.AddListener(UniTask.UnityAction(async () =>
{
    await UniTask.Delay(TimeSpan.FromSeconds(1f));
}));
```

```csharp
// ❌ 反例：async void —— 异常无法被捕获，直接抛到主线程 crash 整个游戏
async void OnClick()
{
    await UniTask.Delay(TimeSpan.FromSeconds(1f));
    throw new Exception("...");  // 无法捕获，Unity 主线程 crash
}
```

> **为什么**：`async void` 的异常无法被 `try/catch` 捕获，会直接层层抛到主线程，导致整个游戏崩溃且难定位。`UniTaskVoid` 的 `Forget` 可挂接 `UniTaskScheduler` 的统一异常处理。

### 1.5 监听事件流 → Subscribe 必须绑定生命周期

**场景**：监听按钮点击、属性变化、每帧检测。

```csharp
// ✅ 正确：AddTo(this) 绑定 MonoBehaviour，销毁自动取消
button.OnClickAsObservable()
    .Subscribe(_ => Debug.Log("Clicked"))
    .AddTo(this);

// ✅ 或用 DisposableBag（多订阅时高效）
DisposableBag bag = default;
Observable.EveryUpdate().Subscribe(_ => { }).AddTo(ref bag);
bag.Dispose();  // 一次性取消所有
```

```csharp
// ❌ 反例：Subscribe 不绑定 —— 对象销毁后订阅仍在，回调访问已销毁对象 → 泄漏 + NullRef
button.OnClickAsObservable()
    .Subscribe(_ => { /* 不 AddTo，销毁后泄漏 */ });
```

> **为什么**：R3 的 Subscribe 返回 `IDisposable`。不绑定时，订阅会一直挂在事件源上（如全局 Subject 或按钮），而持有回调的 MonoBehaviour 可能已销毁 → 内存泄漏 + 访问已销毁对象。`.AddTo(this)` 让订阅随对象销毁自动释放。

### 1.6 响应式属性 → ReactiveProperty 驱动 UI

**场景**：HP 变化自动更新血条。

```csharp
// ✅ 正确
public class Player
{
    public ReactiveProperty<int> Hp { get; } = new(100);
    public ReadOnlyReactiveProperty<bool> IsDead { get; }
    public Player()
    {
        IsDead = Hp.Select(x => x <= 0).ToReadOnlyReactiveProperty();
    }
}
// 订阅（绑定生命周期）
player.Hp.Subscribe(hp => UpdateHpUI(hp)).AddTo(this);
```

```csharp
// ❌ 反例：手动在 Update 里轮询比较 HP 再更新 UI —— 重复造轮子 + 无谓每帧开销
void Update()
{
    if (_lastHp != player.Hp) { UpdateHpUI(player.Hp); _lastHp = player.Hp; }
}
```

### 1.7 每帧值检测 → EveryValueChanged（自动优化，不用每帧比较）

**场景**：检测角色落地。

```csharp
// ✅ 正确
Observable.EveryValueChanged(this, x => x.IsGrounded)
    .Where(grounded => grounded)
    .Subscribe(_ => Debug.Log("Landed!"))
    .AddTo(this);
```

```csharp
// ❌ 反例：EveryUpdate 里手动比较
Observable.EveryUpdate()
    .Subscribe(_ => { if (IsGrounded) OnLanded(); })  // 每帧触发，可能重复触发落地
    .AddTo(this);
```

### 1.8 事件触发异步操作 → SubscribeAwait + AwaitOperation

**场景**：点击按钮发网络请求，期间忽略新点击。

```csharp
// ✅ 正确
button.OnClickAsObservable()
    .SelectAwait(async (_, ct) => await FetchDataAsync(ct), AwaitOperation.Drop)
    .Subscribe(result => ShowResult(result))
    .AddTo(this);
```

```csharp
// ❌ 反例：Subscribe 里直接 async void —— 并发触发多个请求，结果错乱
button.OnClickAsObservable()
    .Subscribe(_ => FetchDataAsync());  // 每次点击并发发请求，无法控制
```

### 1.9 等待条件成立 → WaitUntil / WaitUntilValueChanged

**场景**：等待某 flag 为 true。

```csharp
// ✅ 正确
await UniTask.WaitUntil(() => isReady, cancellationToken: destroyCancellationToken);
```

### 1.10 并发等待 → UniTask.WhenAll

**场景**：同时加载 3 个资源。

```csharp
// ✅ 正确
var (a, b, c) = await UniTask.WhenAll(
    LoadSpriteAsync("icon1"), LoadSpriteAsync("icon2"), LoadSpriteAsync("icon3")
);
```

### 1.11 超时 → CancelAfterSlim（PlayerLoop，不占线程）

**场景**：网络请求 5 秒超时。

```csharp
// ✅ 正确
var cts = new CancellationTokenSource();
cts.CancelAfterSlim(TimeSpan.FromSeconds(5f));
try { return await DoNetworkRequestAsync(cts.Token); }
catch (OperationCanceledException) when (cts.IsCancellationRequested) { return null; }
```

---

## 2. 高频误用反例汇总（AI 思维惯性）—— 最关键的清单

> 这一节是本次项目**运行时翻车**的集中来源，AI 写异步代码前逐条对照。

| # | 误用模式 | 正确做法 | 后果 |
|---|---------|---------|------|
| 1 | `async void` | `UniTaskVoid` + `Forget` | 异常无法捕获，crash 整个游戏 |
| 2 | 用协程 `WaitForSeconds`/`StartCoroutine` | `UniTask.Delay` + 取消 | 禁协程铁律；协程难取消 |
| 3 | 异步不传 `destroyCancellationToken` | 传取消 token | 对象销毁后访问已销毁对象报错 |
| 4 | 裸 `new CancellationTokenSource()` 忘 Dispose | `destroyCancellationToken` / CTS 工厂 | 内存泄漏 |
| 5 | `Subscribe(...)` 不 `AddTo(this)` | `.AddTo(this)` / `RegisterTo(ct)` | 订阅泄漏 + 访问已销毁对象 |
| 6 | `MonoBehaviour.Update` 里手动轮询比较值 | `EveryValueChanged` / ReactiveProperty | 重复造轮子 + 无谓开销 |
| 7 | 事件里 `Subscribe` 内联 `async void` 发请求 | `SelectAwait(..., AwaitOperation.Drop)` | 并发请求错乱 |
| 8 | 用 `Time.deltaTime` 在异步里做延迟累计 | `UniTask.Delay` | 不精确 + 不暂停感知 |
| 9 | `Start()` 里 `InitializeAsync().Forget()` 不处理竞态 | 用 `DestroyCancellationToken` 或先检查 | 初始化协程竞态 |
| 10 | 在 `async` 方法里用 `Dispose` 后继续访问资源 | 确保 Dispose 后不再有异步访问 | 已释放对象访问 |
| 11 | 订阅全局 Subject 不取消 | `AddTo` 绑定生命周期 | 全局泄漏 + 重复回调 |
| 12 | `WaitForEndOfFrame`/`WaitUntil` 不传取消 | 传 `destroyCancellationToken` | 对象销毁后 await 永不结束 |

---

## 3. 边界与不做什么

- R3/UniTask **不封装**（Cysharp 库，直接使用）。
- Subscribe/Await **必须**绑定生命周期（`AddTo(this)` / `RegisterTo(ct)` / `destroyCancellationToken`）。
- 不要为了"省事"用循环+Delay 模拟事件流——用 R3 的流式操作（`Where`/`Select`/`CombineLatest`/`Merge`）。
- 多订阅场景优先用 `DisposableBag`；单订阅用 `.AddTo(this)`。
- 异步操作传 `ct` 时，统一传 `destroyCancellationToken` 或框架 `CreateLinkedToken` 生成的 token。

---

## 4. 与框架其它模块的配合

- **延迟/帧等待**：`UniTask.Delay`/`DelayFrame`（感知暂停需绑定 Lifecycle 通道，或用 `destroyCancellationToken`）。
- **CTS 工厂**：`GameGlobal.Lifecycle.CreateLinkedToken(gameObject)` 生成绑定生命周期的 token，首选。
- **暂停感知的异步**：Subscribe `Lifecycle.ObserveChannelPaused` 决定是否继续。

---

## 5. 与使用指南的关系

- API/用法以《R3与UniTask使用指南》为准；本文只补充"正例 + 真实踩坑"，尤其强调**取消/生命周期绑定**。
- 若冲突，以库官方文档与《使用指南》为准。

---

*本文档随 PumpGF 框架分发。由 2026-08 项目实战反哺。这是本次项目最需要沉淀的模块——异步/生命周期/时序竞态是运行时翻车的头号来源。*
