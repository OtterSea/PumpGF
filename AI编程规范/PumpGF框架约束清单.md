# PumpGF 框架约束清单（AI 铁律）

> **本文件是 AI 编码的绝对禁区清单。**
> 违反任何一条都会导致性能崩溃、逻辑错误或框架失效。
> AI 在生成任何代码前**必须逐条检查**是否触犯。

---

## 🚫 铁律 1：不准绕过 LifecycleMgr 自建 Update

**禁止** 在 MonoBehaviour 上写 `Update()` / `FixedUpdate()` / `LateUpdate()`。
**必须** 通过 `GameGlobal.LifecycleMgr.RegisterTick(channel, callback)` 注册。

```csharp
// 🚫 错误
void Update() { DoLogic(); }

// ✅ 正确
GameGlobal.LifecycleMgr.RegisterTick(UpdateChannel.Logic, OnLogicTick);
```

**原因**：框架通过 `LifecycleDriver`（唯一 MonoBehaviour）统一驱动所有 Channel。自建 Update 会脱离框架时序控制，导致执行顺序不可预测。

---

## 🚫 铁律 2：不准在 FSM `.When()` 条件中产生副作用

**禁止** 在 `StateMachineBuilder` 的 `.When(() => ...)` 条件回调里调用 `Consume()`、修改状态、发送事件、写入任何字段。

```csharp
// 🚫 错误 — When() 每帧评估，会反复消耗输入
.When(() => inputMgr.ConsumeBufferedInput("Attack"))

// ✅ 正确 — When() 只做纯读查询，OnEnter 里消耗
.When(() => _requestAttack)
// 在 State.OnEnter 中：
_fsm.ClearRequests();
inputSource.ConsumeAttack();
```

**原因**：`.When()` 条件在**每个 Tick 都会被全部评估一遍**（包括未满足的转换），副作用会被重复执行。

---

## 🚫 铁律 3：不准混淆 Input 通道和 Logic 通道

| 通道 | 频率 | 用途 | 典型内容 |
|------|------|------|----------|
| `UpdateChannel.Input` | 每帧（可变 dt） | 采集输入 | 读取 InputAction、写入 InputSource |
| `UpdateChannel.Logic` | 固定步长（60Hz） | 游戏逻辑 | FSM Tick、战斗、移动、物理 |

**禁止** 在 Logic 通道里读取原始 InputAction。
**禁止** 在 Input 通道里执行游戏逻辑。

```csharp
// 🚫 错误 — Logic Tick 里直接读 InputAction
void OnLogicTick(float dt)
{
    var move = _moveAction.ReadValue<Vector2>(); // 频率不匹配！
}

// ✅ 正确 — Input Tick 采集，存入 InputSource；Logic Tick 读 InputSource
void OnInputTick(float dt)
{
    _inputSource.Move = _moveAction.ReadValue<Vector2>();
}
void OnLogicTick(float dt)
{
    var move = _inputSource.Move; // 读上一次采集的快照
}
```

---

## 🚫 铁律 4：不准在热路径中使用闭包 / Lambda 捕获

**禁止** 在每帧执行的代码中使用捕获外部变量的 lambda。

```csharp
// 🚫 错误 — 每次调用都会分配一个闭包对象
fist.OnReturnComplete += () => Combat.OnFistReturned(fist, isLeft);

// ✅ 正确 — 用方法引用 + 预存状态
fist.SetOwner(this, isLeft);
fist.OnReturnComplete += fist.HandleReturn; // 无捕获
```

**原因**：每个闭包 = 一次 `new` = GC 压力。在 60Hz 的 Logic Tick 下，每帧产生闭包会导致 GC 频繁回收。

---

## 🚫 铁律 5：不准用 SetPosition 做运动学角色移动

**禁止** 对挂载了 `KinematicCharacterMotor` 的角色使用 `transform.position = ...`、`Rigidbody.MovePosition()` 或 `CharacterController.Move()`。

```csharp
// 🚫 错误 — 穿墙！
transform.position += direction * speed * dt;

// ✅ 正确 — 通过 KCC 接口
public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
{
    currentVelocity = _isDashing ? _dashVelocity : _moveVelocity;
}
```

**原因**：KCC 通过 `UpdateVelocity` / `UpdateRotation` 回调驱动角色，内部处理碰撞检测和地面贴合。绕过它会导致穿墙、卡地形、物理状态不一致。

---

## 🚫 铁律 6：不准用 `System.IObservable<T>` 或 `Task`

| 🚫 禁止 | ✅ 必须用 |
|---------|----------|
| `System.IObservable<T>` | `R3.Observable<T>` |
| `System.Threading.Tasks.Task` | `Cysharp.Threading.Tasks.UniTask` |
| `async void` | `async UniTaskVoid` |
| `System.Reactive` 任何类型 | `R3` 对应类型 |

```csharp
// 🚫 错误
public System.IObservable<int> OnHpChanged => _hpSubject;
public async Task LoadAsync() { ... }

// ✅ 正确
public R3.Observable<int> OnHpChanged => _hpSubject;
public async UniTask LoadAsync() { ... }
```

**原因**：R3 ≠ System.Reactive，类型不兼容。UniTask 是零分配异步，Task 会产生大量 GC。

---

## 🚫 铁律 7：不准绕过对象池直接 Instantiate / Destroy

**禁止** 对频繁创建销毁的对象（子弹、特效、UI 弹窗、伤害数字等）使用原生 `Instantiate()` / `Destroy()`。

```csharp
// 🚫 错误
var bullet = Instantiate(bulletPrefab);
Destroy(bullet, 3f);

// ✅ 正确
var bullet = GameGlobal.PoolMgr.Get<Bullet>(bulletPrefab);
GameGlobal.PoolMgr.Release(bullet);
```

**原因**：`Instantiate` 触发序列化 + 构造 + Awake 链，`Destroy` 触发 GC。对象池预创建复用，零分配。

---

## 🚫 铁律 8：不准在 OnGUI / 热路径中使用字符串插值

**禁止** 在每帧调用的方法中使用 `$"..."` 字符串插值、`string.Format()`、`+` 拼接。

```csharp
// 🚫 错误 — 每帧分配字符串
void OnGUI()
{
    GUI.Label(rect, $"HP: {_hp} / {_maxHp}");
}

// ✅ 正确 — 用 StringBuilder 缓存或仅在值变化时更新
private readonly StringBuilder _sb = new(64);
void OnHpChanged(int hp)
{
    _sb.Clear();
    _sb.Append("HP: ").Append(hp).Append(" / ").Append(_maxHp);
    _cachedLabel = _sb.ToString();
}
```

**原因**：每次字符串插值 = `string.Format()` = `new string()` = GC 分配。OnGUI 每帧调用 2+ 次，会产生大量垃圾。

---

## 🚫 铁律 9：不准自建输入系统，必须走 InputMgr

**禁止** 直接使用 `Input.GetKeyDown()` / `Keyboard.current[Key.X].wasPressedThisFrame` 做游戏逻辑输入。
**必须** 通过 `GameGlobal.InputMgr` 注册和消费输入。

```csharp
// 🚫 错误
if (Keyboard.current[Key.Space].wasPressedThisFrame) { Jump(); }

// ✅ 正确
// InputMgr 已绑定 InputActionAsset 中的 "Jump" Action
GameGlobal.InputMgr.OnActionPerformed("Jump")
    .Subscribe(_ => _requestJump = true)
    .AddTo(this);
```

**例外**：Debug 工具（如 `DebugConsoleDriver` 的 F1 切换）可直接读取键盘。

---

## 🚫 铁律 10：不准在 `ICharacterController` 回调外修改角色物理状态

**禁止** 在 `UpdateVelocity` / `UpdateRotation` / `AfterCharacterUpdate` 等 KCC 回调之外修改角色的速度、旋转、位置。

```csharp
// 🚫 错误 — 在普通方法里改速度
void StartDash()
{
    _motor.BaseVelocity = dashDir * dashSpeed; // 会被下一帧覆盖
}

// ✅ 正确 — 设置标志，在 UpdateVelocity 里读取
void StartDash()
{
    _isDashing = true;
    _dashVelocity = dashDir * dashSpeed;
}
public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
{
    if (_isDashing) currentVelocity = _dashVelocity;
}
```

---

## 速查表

| # | 一句话 |
|---|--------|
| 1 | 不写 Update，走 LifecycleMgr |
| 2 | `.When()` 只读，不改 |
| 3 | Input 采集，Logic 消费 |
| 4 | 热路径零闭包 |
| 5 | 运动走 KCC velocity |
| 6 | R3 + UniTask，不用 System |
| 7 | 频繁对象走池 |
| 8 | 热路径零字符串分配 |
| 9 | 输入走 InputMgr |
| 10 | 角色物理只在 KCC 回调里改 |

---

> 📅 创建时间：2026-07-19
> 📌 本清单源自 ProjectA 角色3C系统重构中发现的真实 bug，每条都对应一个曾经导致严重问题的案例。
