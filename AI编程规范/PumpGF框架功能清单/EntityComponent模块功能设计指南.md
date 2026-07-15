# Entity Component (ECS-Lite) 模块功能设计指南

> **文档定位**：本文件是 Entity Component (ECS-Lite) 模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、API 契约与数据结构，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的公开 API 语义**。
>
> **模块编号**：3-2。
> **前置依赖**：R3（可选，ReactiveProperty）、PoolMgr（可选，Entity 池化参考）。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 |
|--------|------|
| 1. Entity 与 GameObject | Entity 纯 C#，持有可选 GameObject 引用，EntityId 关联 View |
| 2. Component 形态 | 纯 C# 类，实现 `IComponent` 标记接口，不依赖 MonoBehaviour |
| 3. EntityBuilder | 链式 Builder API（`With<T>` 配置组件） |
| 4. 属性变更流 | Component 属性**可选**用 `ReactiveProperty`，框架不强制 |
| 5. Query | 类型安全查询（`Query<T1, T2>`）+ `Where` 链式过滤 |
| 6. EntityManager | `IModule`，全局管理 Entity 创建/销毁/查询 |
| 7. FSM 集成 | Entity 持有 StateMachine 引用（普通字段，非组件） |
| 8. Entity 池化 | 可选池化，高频实体（子弹/特效）池化复用 |
| 风格 | 纯 C#，非 DOTS，不追求极致性能，追求灵活组合与可查询 |
| 底层 | R3（可选 ReactiveProperty）+ C# 泛型 |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**Entity Component 是轻量级组合模式框架**——
Entity 持有多个 Component 实现能力组合，EntityManager 全局管理并提供类型安全查询。
非 DOTS，不追求极致性能，追求**灵活组合、可查询、避免继承地狱**。

### 1.2 职责清单

| 职责 | 说明 |
|------|------|
| Entity 管理 | 纯 C# Entity，持有组件 + 可选 GameObject + 可选 StateMachine |
| Component 组合 | IComponent 标记接口，Entity 动态添加/移除组件 |
| EntityBuilder | 链式 API 创建 Entity |
| Query 查询 | 类型安全查询 + Where 过滤 |
| EntityManager | 全局管理 Entity 创建/销毁/查询 |
| 属性变更流 | Component 可选用 ReactiveProperty 暴露属性变更 |
| Entity 池化 | 高频实体池化复用 |
| 组件索引 | 按组件类型索引，Query 高效 |

### 1.3 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ DOTS ECS 集成 | 非 DOTS，纯 C# |
| ❌ Archetype/Chunk 优化 | 不追求极致性能 |
| ❌ Component System（自动遍历所有 Component） | 业务自行遍历 Query 结果 |
| ❌ Entity 序列化/存档 | 由 GameDataStore 管理持久化数据 |
| ❌ 可视化编辑器 | Phase 4 Editor Tools |
| ❌ 多线程/Job 系统 | 单线程，简单可靠 |

---

## 2. 整体架构

```
┌─────────────────────────────────────────┐
│            业务逻辑层                    │
│  (创建敌人/道具/NPC，组合不同能力)       │
└──────────────────┬──────────────────────┘
                   │ EntityBuilder / Query
                   ▼
┌─────────────────────────────────────────┐
│         EntityManager (IModule)          │
│  ┌──────────────────────────────────┐   │
│  │ Entity 表                        │   │
│  │  int Id → Entity                 │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ Component 索引                    │   │
│  │  Type → List<Entity>             │   │
│  └──────────────────────────────────┘   │
│  ┌──────────────────────────────────┐   │
│  │ EntityPool (可选池化)            │   │
│  │  key → Stack<Entity>             │   │
│  └──────────────────────────────────┘   │
└──────────────────┬──────────────────────┘
                   │ 持有
                   ▼
┌─────────────────────────────────────────┐
│              Entity                      │
│  ┌──────────┐ ┌──────────┐ ┌─────────┐ │
│  │Component1│ │Component2│ │  ...    │ │
│  │HealthCmp │ │MoveCmp   │ │         │ │
│  └──────────┘ └──────────┘ └─────────┘ │
│  ├─ GameObject? (可选 View)            │
│  ├─ StateMachine? (可选状态机)         │
│  └─ Id (唯一标识)                       │
└─────────────────────────────────────────┘
```

---

## 3. Entity 设计

### 3.1 Entity 类

```
public class Entity
{
    public int Id { get; }
    public GameObject GameObject { get; set; }    // 可选 View 引用
    public StateMachine StateMachine { get; set; } // 可选状态机引用

    private readonly Dictionary<Type, IComponent> _components;

    // 组件操作
    T Get<T>() where T : IComponent;
    bool Has<T>() where T : IComponent;
    (T1, T2) Get<T1, T2>() where T1 : IComponent where T2 : IComponent;
    void Add<T>(T component) where T : IComponent;
    void Add<T>(Action<T> configure = null) where T : IComponent, new();
    bool Remove<T>() where T : IComponent;
    void RemoveAll();  // 清除所有组件

    // 生命周期
    void Dispose();  // 销毁，通知 EntityManager
}
```

### 3.2 EntityId

- 全局唯一 `int`，由 EntityManager 分配。
- 用于关联 GameObject（View 层通过 EntityId 查找 Entity）。
- Entity 销毁后 Id 回收（可选，避免 Id 膨胀）。

### 3.3 Entity 与 View 分离

```
// 创建 Entity（纯逻辑，无 View）
var enemy = EntityManager.Create()
    .With<HealthComponent>(h => h.MaxHp = 100)
    .With<MovementComponent>(m => m.Speed = 3f)
    .With<AIComponent>()
    .Build();

// 可选关联 GameObject（View）
var go = await ResMgr.InstantiateAsync("Enemy/Prefab");
enemy.GameObject = go;
go.GetComponent<EntityView>().EntityId = enemy.Id;  // View 持有 EntityId
```

- Entity 是数据和逻辑的组合，不依赖 Unity。
- GameObject 是表现层，可选。
- 通过 EntityId 双向关联。

---

## 4. IComponent 接口

### 4.1 标记接口

```
public interface IComponent { }
```

- 纯标记接口，无强制方法。
- Component 是纯 C# 类，实现 IComponent。
- 不依赖 MonoBehaviour，可单元测试。

### 4.2 Component 示例

```
public class HealthComponent : IComponent
{
    public ReactiveProperty<float> Hp { get; } = new(100);  // 关心变更 → ReactiveProperty
    public float MaxHp;                                       // 配置值 → 普通字段
    public ReactiveProperty<bool> IsDead { get; private set; }

    public HealthComponent()
    {
        IsDead = Hp.Select(hp => hp <= 0).ToReadOnlyReactiveProperty();
    }

    public void TakeDamage(float damage)
    {
        Hp.Value = Mathf.Max(0, Hp.Value - damage);
    }

    public void Heal(float amount)
    {
        Hp.Value = Mathf.Min(MaxHp, Hp.Value + amount);
    }
}

public class MovementComponent : IComponent
{
    public float Speed;           // 配置值
    public Vector3 Position;      // 高频更新，普通字段（性能优先）
    public Vector3 Velocity;      // 普通字段
}
```

### 4.3 属性变更流约定

- 关心变更的属性（如 HP、状态）→ 用 `ReactiveProperty`。
- 高频更新的属性（如位置、速度）→ 用普通字段（性能优先）。
- 配置值（如 MaxHp、Speed）→ 用普通字段。
- 框架不强制，业务按需选择。

---

## 5. EntityBuilder 链式 API

### 5.1 Builder 设计

```
public class EntityBuilder
{
    public static EntityBuilder Create();
    public EntityBuilder With<T>(Action<T> configure = null) where T : IComponent, new();
    public EntityBuilder With<T>(T component) where T : IComponent;
    public EntityBuilder WithGameObject(GameObject go);
    public EntityBuilder WithStateMachine(StateMachine sm);
    public Entity Build();
}
```

### 5.2 使用示例

```
var enemy = EntityBuilder.Create()
    .With<HealthComponent>(h =>
    {
        h.MaxHp = 100;
        h.Hp.Value = 100;
    })
    .With<MovementComponent>(m => m.Speed = 3f)
    .With<AIComponent>()
    .WithStateMachine(combatStateMachine)
    .Build();

// 或直接传组件实例
var health = new HealthComponent { MaxHp = 200 };
var enemy = EntityBuilder.Create()
    .With(health)
    .With<MovementComponent>()
    .Build();
```

---

## 6. Query 过滤器

### 6.1 类型安全查询

```
public static class EntityManagerQueryExtensions
{
    // 单组件查询
    IEnumerable<Entity> Query<T1>() where T1 : IComponent;

    // 双组件查询
    IEnumerable<Entity> Query<T1, T2>()
        where T1 : IComponent where T2 : IComponent;

    // 三组件查询
    IEnumerable<Entity> Query<T1, T2, T3>()
        where T1 : IComponent where T2 : IComponent where T3 : IComponent;
}
```

### 6.2 Where 过滤

```
// 查询所有有 HealthComponent 的实体
var allAlive = EntityManager.Query<HealthComponent>();

// 带过滤
var lowHp = EntityManager.Query<HealthComponent>()
    .Where(e => e.Get<HealthComponent>().Hp.Value < 30);

// 双组件查询 + 过滤
var movableEnemies = EntityManager.Query<HealthComponent, MovementComponent>()
    .Where(e => e.Get<HealthComponent>().Hp.Value > 0);
```

### 6.3 实现原理

EntityManager 维护组件索引：
```
Dictionary<Type, HashSet<Entity>> _componentIndex;
```

- `Add<T>` 时将 Entity 加入 `typeof(T)` 的索引。
- `Remove<T>` 时从索引移除。
- `Query<T>()` 直接返回索引列表（O(1) 获取，O(n) 遍历）。

---

## 7. EntityManager 全局管理

### 7.1 API

```
class EntityManager : IModule
{
    // 创建
    EntityBuilder Create();  // 返回 EntityBuilder 链式创建
    Entity CreateEntity();   // 直接创建空 Entity

    // 销毁
    void Destroy(Entity entity);
    void DestroyAll();       // 销毁所有 Entity

    // 查询
    Entity GetById(int id);
    int Count { get; }

    // Query 扩展方法（见 §6）

    // 池化
    void RegisterPool(string key, Func<Entity> factory, int preheat = 0);
    Entity Get(string key);      // 从池取
    void Release(Entity entity);  // 回池

    // IModule
    void Init();
    void Dispose();
}
```

### 7.2 创建流程

```
// EntityManager.Create() 返回 EntityBuilder
// EntityBuilder.Build() 时：
//   1. 分配 EntityId
//   2. 注册到 EntityManager 的 Entity 表
//   3. 更新组件索引
//   4. 返回 Entity
```

### 7.3 销毁流程

```
// EntityManager.Destroy(entity):
//   1. 从组件索引移除
//   2. 从 Entity 表移除
//   3. entity.Dispose()（清理组件/状态机引用）
//   4. 可选销毁关联 GameObject
```

---

## 8. Entity 池化

### 8.1 适用场景

高频创建销毁的实体：
- 子弹/弹幕
- 特效（伤害数字、粒子）
- 临时 NPC

### 8.2 池化 API

```
// 注册池（预热）
EntityManager.RegisterPool("Bullet", () =>
    EntityBuilder.Create()
        .With<BulletComponent>()
        .With<MovementComponent>()
        .Build(),
    preheat: 20);

// 从池取
var bullet = EntityManager.Get("Bullet");
bullet.Get<MovementComponent>().Position = spawnPos;

// 回池
EntityManager.Release(bullet);
```

### 8.3 回池重置

- 回池时调用 `entity.RemoveAll()` 清除所有组件状态。
- 取出时通过 `With<T>` 重新配置。
- 或组件实现 `IResettable` 接口（可选），回池时调 `Reset()`。

```
public interface IResettable
{
    void Reset();
}
```

> 本阶段可选实现 IResettable，默认回池时 RemoveAll + 重新配置。

---

## 9. 与 FSM/HSM 集成

### 9.1 Entity 持有状态机引用

```
var player = EntityBuilder.Create()
    .With<HealthComponent>(h => h.MaxHp = 100)
    .With<MovementComponent>()
    .WithStateMachine(movementStateMachine)
    .Build();

// 访问状态机
player.StateMachine.Tick(dt);
player.StateMachine.ChangeState("Idle");
```

### 9.2 状态机与组件联动

```
public class CombatState : State
{
    private readonly Entity _entity;

    public CombatState(Entity entity) { _entity = entity; }

    public override void OnEnter()
    {
        var health = _entity.Get<HealthComponent>();
        health.Hp.Subscribe(hp => { if (hp <= 0) _entity.StateMachine.ChangeState("Dead"); })
            .AddTo(ref Bag);
    }
}
```

### 9.3 多状态机

Entity 持有单个 StateMachine 引用。若需多状态机（移动 + 攻击），可用 `Dictionary<string, StateMachine>` 或将状态机作为组件。

> 本阶段推荐单状态机引用，多状态机场景可扩展。

---

## 10. API 契约（公开接口）

### 10.1 Entity

```
class Entity
{
    int Id { get; }
    GameObject GameObject { get; set; }
    StateMachine StateMachine { get; set; }

    T Get<T>() where T : IComponent;
    bool Has<T>() where T : IComponent;
    (T1, T2) Get<T1, T2>() where T1 : IComponent where T2 : IComponent;
    void Add<T>(T component) where T : IComponent;
    void Add<T>(Action<T> configure = null) where T : IComponent, new();
    bool Remove<T>() where T : IComponent;
    void RemoveAll();
    void Dispose();
}
```

### 10.2 IComponent

```
interface IComponent { }
```

### 10.3 EntityBuilder

```
class EntityBuilder
{
    static EntityBuilder Create();
    EntityBuilder With<T>(Action<T> configure = null) where T : IComponent, new();
    EntityBuilder With<T>(T component) where T : IComponent;
    EntityBuilder WithGameObject(GameObject go);
    EntityBuilder WithStateMachine(StateMachine sm);
    Entity Build();
}
```

### 10.4 EntityManager

```
class EntityManager : IModule
{
    EntityBuilder Create();
    Entity CreateEntity();
    void Destroy(Entity entity);
    void DestroyAll();
    Entity GetById(int id);
    int Count { get; }

    void RegisterPool(string key, Func<Entity> factory, int preheat = 0);
    Entity Get(string key);
    void Release(Entity entity);

    void Init();
    void Dispose();
}
```

### 10.5 Query 扩展

```
static class EntityManagerQueryExtensions
{
    static IEnumerable<Entity> Query<T1>(this EntityManager mgr) where T1 : IComponent;
    static IEnumerable<Entity> Query<T1, T2>(this EntityManager mgr)
        where T1 : IComponent where T2 : IComponent;
    static IEnumerable<Entity> Query<T1, T2, T3>(this EntityManager mgr)
        where T1 : IComponent where T2 : IComponent where T3 : IComponent;
}
```

### 10.6 IResettable（可选）

```
interface IResettable
{
    void Reset();
}
```

---

## 11. 使用示例（伪代码）

### 11.1 创建敌人

```
var enemy = GameGlobal.EntityManager.Create()
    .With<HealthComponent>(h => { h.MaxHp = 100; h.Hp.Value = 100; })
    .With<MovementComponent>(m => m.Speed = 3f)
    .With<AIComponent>(ai => ai.AgroRange = 10f)
    .With<CombatComponent>()
    .WithStateMachine(aiStateMachine)
    .Build();

// 关联 View
var go = await GameGlobal.ResMgr.InstantiateAsync("Enemy/Slime");
enemy.GameObject = go;
go.GetComponent<EntityView>().EntityId = enemy.Id;
```

### 11.2 查询

```
// 所有有 HealthComponent 的实体
foreach (var entity in GameGlobal.EntityManager.Query<HealthComponent>())
{
    var health = entity.Get<HealthComponent>();
    Debug.Log($"Entity {entity.Id}: HP = {health.Hp.Value}");
}

// HP 低于 30 的敌人
var lowHp = GameGlobal.EntityManager.Query<HealthComponent>()
    .Where(e => e.Get<HealthComponent>().Hp.Value < 30);

// 有 Health + Movement 的实体
var movers = GameGlobal.EntityManager.Query<HealthComponent, MovementComponent>();
```

### 11.3 订阅属性变更

```
var enemy = GameGlobal.EntityManager.Create()
    .With<HealthComponent>(h => h.MaxHp = 100)
    .Build();

var health = enemy.Get<HealthComponent>();
health.Hp.Subscribe(hp => UpdateHpBar(hp)).AddTo(this);
health.IsDead.Subscribe(dead => { if (dead) OnEnemyDeath(enemy); }).AddTo(this);

// 造成伤害
health.TakeDamage(30);
```

### 11.4 池化子弹

```
// 注册池
GameGlobal.EntityManager.RegisterPool("Bullet", () =>
    EntityBuilder.Create()
        .With<BulletComponent>()
        .With<MovementComponent>(m => m.Speed = 20f)
        .Build(),
    preheat: 20);

// 发射
var bullet = GameGlobal.EntityManager.Get("Bullet");
bullet.Get<MovementComponent>().Position = muzzlePos;
bullet.Get<BulletComponent>().Damage = 10;

// 回收
GameGlobal.EntityManager.Release(bullet);
```

### 11.5 状态机联动

```
var player = GameGlobal.EntityManager.Create()
    .With<HealthComponent>(h => h.MaxHp = 100)
    .WithStateMachine(playerStateMachine)
    .Build();

// 状态机内访问组件
public class IdleState : State
{
    private readonly Entity _entity;
    public IdleState(Entity entity) { _entity = entity; }

    public override void OnEnter()
    {
        var movement = _entity.Get<MovementComponent>();
        movement.Velocity = Vector3.zero;
    }
}
```

---

## 12. 实现检查清单

- [ ] `Entity` 类（Id/GameObject/StateMachine/组件字典）
- [ ] `IComponent` 标记接口
- [ ] `EntityBuilder` 链式 API（With/WithGameObject/WithStateMachine/Build）
- [ ] `EntityManager : IModule`，纳入 GameGlobal
- [ ] EntityId 全局分配
- [ ] 组件索引（`Dictionary<Type, HashSet<Entity>>`）
- [ ] `Query<T1>`/`Query<T1,T2>`/`Query<T1,T2,T3>` 类型安全查询
- [ ] `Where` 过滤（LINQ 扩展）
- [ ] `Get<T>`/`Has<T>`/`Add<T>`/`Remove<T>`/`RemoveAll`
- [ ] `Destroy`/`DestroyAll` 清理
- [ ] Entity 池化（RegisterPool/Get/Release）
- [ ] 池化回池重置（RemoveAll 或 IResettable）
- [ ] `Dispose` 清理所有 Entity 与池
- [ ] 所有公开 API 有中文 XML 注释
- [ ] 无硬编码

---

## 13. 依赖关系

| 依赖项 | 方向 | 说明 |
|--------|------|------|
| R3 | 可选引用 | Component 内部 ReactiveProperty（可选） |
| GameGlobal | 被引用 | 暴露 EntityManager |

> **初始化**：EntityManager 是 IModule，在 GameGlobal 初始化。
> 无强依赖其他模块（FSM 是运行时关联，非初始化依赖）。

---

## 14. 后续模块依赖本模块的接口

| 后续模块 | 使用的 EntityManager 接口 |
|----------|--------------------------|
| Level/Scene Manager (3-3) | 关卡内实体的创建/销毁 |
| 业务层（敌人/道具/玩家） | Entity 创建、Query 查询、组件访问 |
| Debug Console (4-1) | 实体统计、Query 调试 |

---

## 15. 关键使用规范（业务层 Coding AI 必读）

### 15.1 用组合不用继承

- ❌ 禁止：`class FlyingEnemy : Enemy`
- ✅ 正确：`EntityBuilder.Create().With<FlyComponent>().With<EnemyComponent>()`

### 15.2 Component 是纯 C#，不依赖 MonoBehaviour

- ❌ 禁止：Component 继承 MonoBehaviour
- ✅ 正确：Component 是纯 C# 类，实现 IComponent

### 15.3 用 Query 查找实体，不用 GameObject.Find

- ❌ 禁止：`GameObject.Find("Enemy")` 遍历场景
- ✅ 正确：`EntityManager.Query<HealthComponent>()` 类型安全查询

### 15.4 高频实体用池化

- 子弹/特效等高频创建销毁的实体用 `RegisterPool` + `Get`/`Release`。

### 15.5 Entity 销毁必须走 EntityManager.Destroy

- ❌ 禁止：直接 `entity.Dispose()` 不通知 EntityManager
- ✅ 正确：`EntityManager.Destroy(entity)`

### 15.6 属性变更用 ReactiveProperty（按需）

- 关心变更的属性（HP/状态）用 ReactiveProperty。
- 高频属性（位置/速度）用普通字段。

---

**文档结束。实现阶段请严格遵循本契约。**
