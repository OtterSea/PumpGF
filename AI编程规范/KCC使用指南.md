# KCC (Kinematic Character Controller) 使用指南

> **用途**：本文件供 Coding AI 快速了解 KCC 用法，节省 token。
> KCC 是第三方库，框架**不封装**，业务层直接使用。

## 1. 概述

- **库**：Kinematic Character Controller v3.1.0
- **位置**：`Packages/com.pumpgf.framework/Vendor/KCC/`
- **命名空间**：`KinematicCharacterController`
- **用途**：3D 角色 kinematic 移动控制（碰撞/楼梯/斜坡/移动平台）
- **框架不封装**：角色移动手感太游戏特异，业务层直接用 KCC API

## 2. 核心组件

| 组件 | 说明 |
|------|------|
| `KinematicCharacterMotor` | 挂在角色上，处理碰撞检测与移动解算 |
| `ICharacterController` | 业务实现的接口，控制角色行为（速度/旋转/碰撞响应） |
| `PhysicsMover` | 挂在移动平台上，自动与角色碰撞交互 |
| `KinematicCharacterSystem` | 系统，自动在 FixedUpdate 驱动所有 Motor |

## 3. 基本用法

### 3.1 角色设置

1. 创建角色 GameObject
2. 添加 `CapsuleCollider`（KCC 要求）
3. 添加 `KinematicCharacterMotor` 组件
4. 添加 `Rigidbody`（设为 Kinematic，KCC 要求）
5. 添加业务脚本，实现 `ICharacterController`

### 3.2 实现 ICharacterController

```csharp
using UnityEngine;
using KinematicCharacterController;

public class MyCharacterController : MonoBehaviour, ICharacterController
{
    public KinematicCharacterMotor Motor;

    [Header("移动参数")]
    public float MoveSpeed = 5f;
    public float JumpSpeed = 10f;

    private Vector3 _moveInput;
    private bool _jumpRequested;

    private void Awake()
    {
        // 关键：将控制器注册到 Motor
        Motor.CharacterController = this;
    }

    private void Update()
    {
        // 业务输入（可用 InputMgr）
        _moveInput = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical")).normalized;
        if (Input.GetKeyDown(KeyCode.Space))
            _jumpRequested = true;
    }

    // ── ICharacterController 接口实现 ──

    public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
    {
 {
            // 平面移动
            var targetVelocity = _moveInput * MoveSpeed;
            currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, 1f - Mathf.Exp(-10f * deltaTime));
        }

        // 跳跃
        if (_jumpRequested && Motor.GroundingStatus.IsStableOnGround)
        {
            currentVelocity += Motor.CharacterUp * JumpSpeed;
        }
        _jumpRequested = false;

        // 重力
        if (!Motor.GroundingStatus.IsStableOnGround)
        {
            currentVelocity += Physics.gravity * deltaTime;
        }
    }

    public void UpdateRotation(ref Quaternion currentRotation, float deltaTime)
    {
        // 朝向移动方向
        if (_moveInput != Vector3.zero)
        {
            var targetRotation = Quaternion.LookRotation(_moveInput, Motor.CharacterUp);
            currentRotation = Quaternion.Slerp(currentRotation, targetRotation, 1f - Mathf.Exp(-10f * deltaTime));
        }
    }

    public void BeforeCharacterUpdate(float deltaTime) { }
    public void AfterCharacterUpdate(float deltaTime) { }
    public void PostGroundingUpdate(float deltaTime) { }

    public bool IsColliderValidForCollisions(Collider coll)
    {
        return true; // 默认所有碰撞器有效
    }

    public void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport) { }
    public void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport) { }
    public void ProcessHitStabilityReport(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, Vector3 atCharacterPosition, Quaternion atCharacterRotation, ref HitStabilityReport hitStabilityReport) { }
    public void OnDiscreteCollisionDetected(Collider hitCollider) { }
}
```

## 4. ICharacterController 接口说明

| 方法 | 调用时机 | 典型用途 |
|------|----------|----------|
| `UpdateRotation` | Motor 计算旋转时 | 设置角色朝向 |
| `UpdateVelocity` | Motor 计算速度时 | 设置移动速度/跳跃/重力 |
| `BeforeCharacterUpdate` | Motor 更新前 | 输入采样、状态准备 |
| `AfterCharacterUpdate` | Motor 更新后 | 动画同步、后处理 |
| `PostGroundingUpdate` | 地面探测后 | 根据地面状态调整 |
| `OnGroundHit` | 探测到地面碰撞时 | 落地反馈（音效/特效） |
| `OnMovementHit` | 移动中碰撞时 | 撞墙反馈 |
| `IsColliderValidForCollisions` | 碰撞过滤 | 忽略特定碰撞器（如自己） |

## 5. Motor 常用属性

| 属性 | 说明 |
|------|------|
| `Motor.GroundingStatus.IsStableOnGround` | 是否稳定站在地面 |
| `Motor.GroundingStatus.GroundCollider` | 脚下的碰撞器 |
| `Motor.GroundingStatus.GroundNormal` | 地面法线 |
| `Motor.CharacterUp` | 角色上方向（考虑坡度） |
| `Motor.TransientPosition` | 当前位置 |
| `Motor.TransientRotation` | 当前旋转 |
| `Motor.BaseVelocity` | 基础速度（含平台速度） |

## 6. 与 PumpGF 集成

### 6.1 暂停感知

KCC 走 FixedUpdate，不感知 Lifecycle 暂停。暂停时需手动禁用：

```csharp
// 订阅 Lifecycle 暂停
GameGlobal.Lifecycle.ObserveChannelPaused(UpdateChannel.Logic)
    .Subscribe(paused =>
    {
        Motor.enabled = !paused;  // 暂停时禁用 Motor
    })
    .AddTo(this);
```

### 6.2 与 InputMgr 集成

```csharp
// 用 InputMgr 替代 Input.GetAxisRaw
private void Update()
{
    var move = GameGlobal.InputMgr.GetAxisValue("Move").Value;
    _moveInput = new Vector3(move.x, 0, move.y).normalized;

    if (GameGlobal.InputMgr.GetButtonValue("Jump").Value && !_jumpHeld)
        _jumpRequested = true;
    _jumpHeld = GameGlobal.InputMgr.GetButtonValue("Jump").Value;
}
```

### 6.3 与 FSM 集成

```csharp
// 状态机驱动角色行为
public class MoveState : State, ICharacterController
{
    // 在 State.OnEnter 注册为 Motor 的控制器
    // 在 State.OnExit 注销
    // UpdateVelocity 里根据状态设置不同速度
}
```

### 6.4 移动平台（PhysicsMover）

```csharp
// 移动平台 GameObject 挂 PhysicsMover
// 在 FixedUpdate 里用 DOTween 或手动移动平台
// KCC 自动处理角色与平台的交互（站上去随平台移动）
```

## 7. 常见场景

### 7.1 爬楼梯/斜坡

KCC **自动处理**楼梯和斜坡，无需额外代码。Motor 的 Inspector 配置：
- `StepHandling`：楼梯处理（启用）
- `SlopeLimit`：最大斜坡角度

### 7.2 移动平台

挂 `PhysicsMover` 到平台，角色站上去自动跟随。

### 7.3 冲刺/闪避

```csharp
// 在 UpdateVelocity 里叠加冲刺速度
if (_isDashing)
{
    currentVelocity += _dashDirection * _dashSpeed;
    _dashTimer -= deltaTime;
    if (_dashTimer <= 0) _isDashing = false;
}
```

### 7.4 下坡不弹跳

KCC 默认下坡稳定。若需调整，在 `OnGroundHit` 里检查 `hitStabilityReport`。

## 8. 注意事项

1. **必须设置 `Motor.CharacterController = this`**，否则接口不被调用。
2. **Motor 自动注册到 KinematicCharacterSystem**，无需手动注册（3.x）。
3. **KCC 走 FixedUpdate**，不感知 Lifecycle 暂停，需手动禁用 Motor。
4. **Rigidbody 设为 Kinematic**，KCC 不用物理引擎驱动角色。
5. **Motor 的 Inspector 参数**（StepHandling/SlopeLimit/GroundSnapSpeed 等）按游戏调参。
6. **不要在 UpdateVelocity 里直接改 Transform**，只改 `ref currentVelocity`，Motor 自动解算碰撞。

---

**更多详情参阅 KCC 官方文档与示例（Asset Store 页面）。**
