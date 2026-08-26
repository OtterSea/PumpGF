# KCC (Kinematic Character Controller) 最佳实践

> **文档定位**：给 Coding AI 提供 KCC 在**真实游戏场景下的标准正例**及高频误用反例。KCC 是第三方库，框架**不封装**，业务层直接用。与《KCC使用指南》配合使用。
> **写作依据**：2026-08 项目实战反哺，融合《踩坑记录》《代码质量审查报告》真实误用。
> **优先读**：AI 接角色控制器/移动相关需求时先读本文再写代码。

---

## 0. 一句话原则

**KCC 的角色行为全部写在 `ICharacterController` 接口回调里，通过改 `ref currentVelocity`/`ref currentRotation` 控制角色；禁止直接改 `Transform`、禁止在业务里硬编码物理、禁止忘 `Motor.CharacterController = this`。**

---

## 1. 标准正例集

### 1.1 角色配置三步（不可缺）

**场景**：创建玩家角色。

```csharp
// 必做：
// 1. 加 CapsuleCollider（KCC 要求）
// 2. 加 KinematicCharacterMotor + Rigidbody(Kinematic)
// 3. 业务脚本实现 ICharacterController，Awake 里注册
private void Awake()
{
    Motor.CharacterController = this;   // 关键，漏了接口永不回调
}
```

```csharp
// ❌ 反例：加了 Motor 却忘注册 CharacterController
// → Motor 在跑，但 UpdateVelocity/UpdateRotation 永不调用，角色不会动
```

> **为什么**：`Motor.CharacterController = this` 是 Motor 与业务控制逻辑的绑定点。漏掉它，Motor 照常解算碰撞但业务速度永远为 0，角色"原地站桩"，且无任何报错——最难排查。

### 1.2 移动/跳跃/重力 → 全在 UpdateVelocity 里改 ref 速度

**场景**：角色四方向移动 + 跳跃 + 重力。

```csharp
public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
{
    // 平面移动（用 InputMgr，不用旧 Input API）
    var move = GameGlobal.InputMgr.GetAxisValue("Move").Value;
    var moveInput = new Vector3(move.x, 0, move.y).normalized;
    var targetVelocity = moveInput * MoveSpeed;
    currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, 1f - Mathf.Exp(-10f * deltaTime));

    // 跳跃
    if (_jumpRequested && Motor.GroundingStatus.IsStableOnGround)
        currentVelocity += Motor.CharacterUp * JumpSpeed;
    _jumpRequested = false;

    // 重力
    if (!Motor.GroundingStatus.IsStableOnGround)
        currentVelocity += Physics.gravity * deltaTime;
}
```

```csharp
// ❌ 反例：直接改 Transform 移动
transform.Translate(moveInput * MoveSpeed * deltaTime);  // 绕过 Motor，无碰撞解算，会穿墙/穿地
```

> **为什么**：KCC 的碰撞解算发生在 `UpdateVelocity` 之后。你只负责给出**期望速度**（ref 参数），Motor 负责检测碰撞、调整位移、处理楼梯斜坡。直接改 `Transform` 就绕开了整套解算，角色会穿墙。

### 1.3 朝向 → UpdateRotation 里改 ref 旋转

**场景**:角色朝移动方向转身。

```csharp
public void UpdateRotation(ref Quaternion currentRotation, float deltaTime)
{
    if (_moveInput != Vector3.zero)
    {
        var targetRotation = Quaternion.LookRotation(_moveInput, Motor.CharacterUp);
        currentRotation = Quaternion.Slerp(currentRotation, targetRotation, 1f - Mathf.Exp(-10f * deltaTime));
    }
}
```

### 1.4 暂停感知 → 订阅 Lifecycle,手动禁用 Motor

**场景**：菜单打开时角色冻结。

```csharp
// KCC 走 FixedUpdate，不感知 Lifecycle 暂停，需手动禁 Motor
GameGlobal.Lifecycle.ObserveChannelPaused(UpdateChannel.Logic)
    .Subscribe(paused => { Motor.enabled = !paused; })
    .AddTo(this);
```

```csharp
// ❌ 反例：以为绑了 Lifecycle 逻辑通道角色就会自动暂停
// KCC 是 FixedUpdate 驱动，完全绕开 Lifecycle 通道 —— 必须手动禁 Motor，否则菜单开着角色还在动
```

### 1.5 楼梯/斜坡/移动平台 → 全靠 KCC 配置，不写代码

**场景**：角色爬楼梯、上斜坡、站上移动平台。

```csharp
// ✅ 正确：KCC 自动处理。只需在 Motor Inspector 配置：
// - StepHandling 启用（楼梯）
// - SlopeLimit 设最大坡度
// 移动平台：平台挂 PhysicsMover，角色站上去自动跟随
```

```csharp
// ❌ 反例：自己写射线/Rigidbody 检测楼梯、手工位移平台上的角色 —— 重复造轮子
```

### 1.6 冲刺/闪避 → UpdateVelocity 里叠加瞬时速度

**场景**：冲刺技能。

```csharp
public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
{
    // ... 常规移动 ...
    if (_isDashing)
    {
        currentVelocity += _dashDirection * _dashSpeed;
        _dashTimer -= deltaTime;
        if (_dashTimer <= 0) _isDashing = false;
    }
}
```

---

## 2. 高频误用反例汇总（AI 思维惯性）

| # | 误用模式 | 正确做法 | 踩坑来源 |
|---|---------|---------|---------|
| 1 | 忘 `Motor.CharacterController = this` | Awake 里注册 | 不注册接口永不回调 |
| 2 | 直接改 `Transform` 移动 | 改 `ref currentVelocity` | 绕过碰撞解算 |
| 3 | 用 `Input.GetAxisRaw` 读移动 | `GameGlobal.InputMgr.GetAxisValue("Move")` | 铁律-输入走 InputMgr |
| 4 | 以为绑 Logic 通道会自动暂停 | 手动订阅 `ObserveChannelPaused` 禁用 Motor | KCC 走 FixedUpdate |
| 5 | 自己写楼梯/斜坡/平台逻辑 | 用 KCC 内置 StepHandling/PhysicsMover | 重复造轮子 |
| 6 | `UpdateVelocity` 里直接改 `currentVelocity` 之外的 Transform | 只改 ref 参数 | 契约 §8.6 |
| 7 | 忘了 Rigidbody 设为 Kinematic | Kinematic 刚体 | KCC 不靠物理引擎驱动 |
| 8 | 把角色行为逻辑写 MonoBehaviour.Update | 写 `ICharacterController` 回调 | 走 Motor 驱动 |
| 9 | 从碰撞器读取自身时用自己碰撞器导致碰撞自身 | `IsColliderValidForCollisions` 过滤 | 角色自撞 |
| 10 | 状态机与 KCC 未结合 | FSM 状态驱动 `UpdateVelocity`（状态类实现 `ICharacterController`） | 契约 §6.3 |

---

## 3. 边界与不做什么

- KCC **不做**（业务自行处理）：角色移动手感（游戏特异）、动画同步（`AfterCharacterUpdate` 里做）、攻击/技能碰撞判定逻辑。
- KCC 不感知 Lifecycle 暂停（FixedUpdate 驱动），需业务手动禁 Motor。
- KCC 不支持旧 `Input.GetAxis`（应经 InputMgr）。
- **框架不封装 KCC**（角色手感太游戏特异），业务层直接调 KCC API。

---

## 4. 与其它模块集成

- **InputMgr**：移动/跳跃输入经 `GameGlobal.InputMgr.GetAxisValue/GetButtonValue`，不用旧 Input API。
- **Lifecycle**：暂停感知靠 `ObserveChannelPaused` 手动禁 Motor。
- **FSM**：状态机驱动角色行为——状态类实现 `ICharacterController`，`State.OnEnter` 注册为 Motor 控制器、`OnExit` 注销。

---

## 5. 与使用指南的关系

- API/组件配置以《KCC使用指南》为准；本文只加"用法正例"与真实踩坑。
- 若冲突，以 KCC 官方文档与《KCC使用指南》为准。

---

*本文档随 PumpGF 框架分发。由 2026-08 项目实战反哺。*
