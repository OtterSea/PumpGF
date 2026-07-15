# Testing Suite 模块功能设计指南

> **文档定位**：本文件是 Testing Suite 模块的**设计契约**，供后续 Coding AI 按此实现。
> 本文件只描述设计意图、职责边界、API 契约与数据结构，**不包含最终实现代码**。
> 实现阶段可在此契约框架内自由组织代码结构，但**不得偏离本文件约定的公开 API 语义**。
>
> **模块编号**：4-3。
> **前置依赖**：Unity Test Framework（Unity 内置）、UniTask（异步测试）。
> **特殊说明**：本模块**不是 IModule**，不纳入 GameGlobal，纯测试代码。放 `Tests/` 文件夹，独立 asmdef。

---

## 0. 决策记录（已锁定）

| 决策项 | 结论 |
|--------|------|
| 1. 测试框架 | Unity Test Framework（基于 NUnit），Unity 内置 |
| 2. 测试分层 | 不改框架。纯 C# 逻辑用 Edit Mode（直接 new 实例），依赖 Unity/GameGlobal 的用 Play Mode（集成测试） |
| 3. 测试组织 | 统一 `Tests/` 文件夹 + 独立 `PumpGF.Tests` asmdef，按模块分子文件夹 |
| 4. TestHelper | 提供 TestHelper + AssertHelper 工具类，减少样板 |
| 5. CI 支持 | 提供 CI 配置文档（Unity `-runTests` 命令行），不内置 CI 脚本 |
| 6. 测试模板 | 提供 EditMode/PlayMode 测试代码模板，AI 按模板写测试 |
| 风格 | 纯测试代码，非 IModule |
| 底层 | NUnit + Unity Test Framework + UniTask |

---

## 1. 模块定位与职责边界

### 1.1 一句话定位

**Testing Suite 是框架的测试基础设施**——
提供分层测试策略（Edit Mode 纯逻辑 + Play Mode 集成）、TestHelper 工具、测试模板，
保障框架代码质量，CI 友好。但**不实现业务测试**（那是业务层的事）。

### 1.2 职责清单

| 职责 | 说明 |
|------|------|
| 测试分层 | Edit Mode（纯 C# 逻辑）+ Play Mode（集成测试） |
| TestHelper | 创建临时 GameObject、异步等待、断言辅助 |
| 测试模板 | EditMode/PlayMode 代码模板，AI 按模板写 |
| 测试组织 | 统一 Tests/ 文件夹 + asmdef |
| CI 文档 | Unity 命令行运行测试的配置说明 |
| 测试优先级 | 各模块测试优先级建议 |

### 1.3 不做什么（防止 Scope Creep）

| 不做 | 原因 |
|------|------|
| ❌ 改框架支持 Mock | 不改 GameGlobal 为接口持有，影响大收益低 |
| ❌ 业务测试 | 业务层自行编写 |
| ❌ 性能测试 | Unity Profiler 负责 |
| ❌ 自动生成测试代码 | AI 按模板手写，不自动生成 |

---

## 2. 整体架构

```
┌─────────────────────────────────────────┐
│            测试代码                      │
│  EditMode Tests (纯 C# 逻辑)            │
│  PlayMode Tests (集成测试)              │
└──────────────────┬──────────────────────┘
                   │ 使用
                   ▼
┌─────────────────────────────────────────┐
│           TestHelper / AssertHelper      │
│  ├─ CreateGameObject<T>()               │
│  ├─ WaitFrames / WaitForCondition       │
│  ├─ Approximately (Vector3)             │
│  └─ InitializeGameGlobal()              │
└──────────────────┬──────────────────────┘
                   │ 测试
                   ▼
┌─────────────────────────────────────────┐
│         Runtime 模块 (各 IModule)        │
│  Edit Mode: EventBus/FSM/Entity/...     │
│  Play Mode: ResMgr/UI/Audio/...         │
└─────────────────────────────────────────┘

        │ CI
        ▼
┌─────────────────┐
│  Unity -runTests │  (命令行)
└─────────────────┘
```

---

## 3. 测试分层策略

### 3.1 Edit Mode（纯 C# 逻辑）

**测试对象**：不依赖 GameGlobal/Unity 的纯 C# 类。

| 模块 | 可测内容 |
|------|----------|
| EventBus | 发布/订阅/EventDomain 隔离/引用计数 |
| FSM/HSM | 状态转换/层级嵌套/条件转换 |
| Entity Component | 组件添加/查询/池化 |
| GameDataStore | 数据域/Command 执行/校验 |
| Scheduler/Timer | 延迟/周期/帧驱动（需 mock 时间） |
| Log | 日志分级/Tag 过滤 |

**特点**：
- 直接 `new` 实例，不依赖 GameGlobal。
- 快速、独立、可并行。
- 用 `[Test]` 特性。

### 3.2 Play Mode（集成测试）

**测试对象**：依赖 Unity/GameGlobal 的模块。

| 模块 | 可测内容 |
|------|----------|
| ResMgr | 资源加载/引用计数/场景加载 |
| Config Manager | SO 加载/校验/热重载 |
| UI Framework | 页面 Push/Pop/弹窗队列 |
| Audio Manager | 播放/音量组/淡入淡出 |
| Input Manager | 上下文切换/输入缓冲 |
| Localization | 语言切换/文本获取 |
| Save/Load | 序列化/版本迁移/槽位 |
| Level/Scene Manager | 关卡加载/过渡 |

**特点**：
- 需要真实 Unity 环境 + GameGlobal 初始化。
- 较慢，串行执行。
- 用 `[UnityTest]` + `UniTask.ToCoroutine`。

### 3.3 不改框架的理由

- GameGlobal 是静态类持有具体类，改为接口持有影响大。
- 纯 C# 逻辑（FSM/Entity/EventBus 等）是框架核心，Edit Mode 可充分覆盖。
- 依赖 Unity 的模块用 Play Mode 真实环境测试，更可靠。

---

## 4. 测试组织

### 4.1 目录结构

```
Packages/com.pumpgf.framework/
  ├─ Runtime/
  │   ├─ PumpGF.Runtime.asmdef
  │   └─ ...（各模块代码）
  └─ Tests/
      ├─ PumpGF.Tests.asmdef          ← 测试程序集
      ├─ EventBus/
      │   └─ EventBusTests.cs
      ├─ FSM/
      │   └─ StateMachineTests.cs
      ├─ Entity/
      │   └─ EntityManagerTests.cs
      ├─ GameDataStore/
      │   └─ GameDataStoreTests.cs
      ├─ Scheduler/
      │   └─ SchedulerTests.cs
      ├─ ResMgr/
      │   └─ ResMgrPlayModeTests.cs
      ├─ UI/
      │   └─ UIFrameworkPlayModeTests.cs
      └─ TestHelper/
          ├─ TestHelper.cs
          └─ AssertHelper.cs
```

### 4.2 asmdef 配置

```
PumpGF.Tests.asmdef:
  name: PumpGF.Tests
  references:
    - PumpGF.Runtime
  optionalPlatformDefines:  # 支持编辑器和 PlayMode
    - UNITY_INCLUDE_TESTS
  defines: []
  includePlatforms: []  # 空表示所有平台（Edit + Play）
```

> Edit Mode 和 Play Mode 测试可放同一 asmdef，Unity 自动按 `[Test]`/`[UnityTest]` 分配。

---

## 5. TestHelper 工具类

### 5.1 TestHelper

```
public static class TestHelper
{
    // ── 临时 GameObject 管理 ──
    // 创建临时 GameObject，测试结束自动销毁
    static T CreateComponent<T>(string name = "TestObject") where T : Component;
    static GameObject CreateGameObject(string name = "TestObject");

    // ── 异步等待 ──
    static UniTask WaitFrames(int frames);
    static UniTask WaitForCondition(Func<bool> condition, float timeoutSeconds = 5f);
    static UniTask WaitForSeconds(float seconds);

    // ── Play Mode 辅助 ──
    // 初始化 GameGlobal（Play Mode 测试用）
    static UniTask InitializeGameGlobalAsync();
    // 清理 GameGlobal
    static void CleanupGameGlobal();

    // ── 测试工具 ──
    // 在 Play Mode 中运行异步测试
    static IEnumerator RunAsync(Func<UniTask> testAction);
}
```

### 5.2 AssertHelper

```
public static class AssertHelper
{
    // Vector3 近似相等
    static void Approximately(Vector3 expected, Vector3 actual, float tolerance = 0.0001f);

    // float 近似相等
    static void Approximately(float expected, float actual, float tolerance = 0.0001f);

    // 确认已 Dispose
    static void IsDisposed(IDisposable disposable);

    // 确认抛出异常
    static void Throws<TException>(Action action) where TException : Exception;

    // 确认在指定时间内完成
    static async UniTask CompletesWithin(Func<UniTask> asyncAction, float timeoutSeconds);
}
```

---

## 6. 测试模板

### 6.1 EditMode 测试模板

```csharp
using NUnit.Framework;
using PumpGF;

public class {ModuleName}Tests
{
    [Test]
    public void {MethodName}_{Scenario}_ExpectedResult()
    {
        // Arrange
        // var sut = new SystemUnderTest();

        // Act
        // var result = sut.DoSomething();

        // Assert
        // Assert.AreEqual(expected, result);
    }

    [SetUp]
    public void SetUp()
    {
        // 每个测试前初始化
    }

    [TearDown]
    public void TearDown()
    {
        // 每个测试后清理
    }
}
```

### 6.2 PlayMode 测试模板

```csharp
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine;
using System.Collections;
using Cysharp.Threading.Tasks;
using PumpGF;

public class {ModuleName}PlayModeTests
{
    [UnityTest]
    public IEnumerator {MethodName}_{Scenario}_ExpectedResult()
        => UniTask.ToCoroutine(async () =>
    {
        // Arrange
        await TestHelper.InitializeGameGlobalAsync();

        // Act
        // var result = await GameGlobal.XXX.DoSomethingAsync();

        // Assert
        // Assert.IsNotNull(result);
    });

    [TearDown]
    public void TearDown()
    {
        TestHelper.CleanupGameGlobal();
    }
}
```

### 6.3 模板获取

- Editor Tools 菜单 `PumpGF/Generate/Test Template`。
- 或文档中复制。
- AI 写测试时引用此模板。

---

## 7. CI 支持

### 7.1 命令行运行测试

```bash
# Edit Mode 测试
Unity -batchmode -runTests \
  -projectPath . \
  -testPlatform editmode \
  -testResults TestResults/editmode-results.xml \
  -logFile TestResults/editmode.log

# Play Mode 测试
Unity -batchmode -runTests \
  -projectPath . \
  -testPlatform playmode \
  -testResults TestResults/playmode-results.xml \
  -logFile TestResults/playmode.log

# 退出码：0=成功，2=失败，3=无法运行
```

### 7.2 CI 配置文档

框架提供 CI 配置说明文档（不内置脚本），业务自行接入：
- GitHub Actions
- Jenkins
- GitLab CI

文档包含：
- Unity 命令行示例
- 结果 XML 解析
- 失败通知

---

## 8. 各模块测试优先级

| 优先级 | 模块 | 测试类型 | 理由 |
|--------|------|----------|------|
| **高** | EventBus | Edit Mode | 核心通信，频繁使用 |
| **高** | FSM/HSM | Edit Mode | 核心逻辑，复杂转换 |
| **高** | Entity Component | Edit Mode | 核心组合，Query 性能 |
| **中** | GameDataStore | Edit Mode | 数据中枢，校验逻辑 |
| **中** | Scheduler/Timer | Edit Mode | 定时调度，边界条件 |
| **中** | Save/Load | Play Mode | 持久化，版本迁移 |
| **中** | ResMgr | Play Mode | 资源加载，引用计数 |
| **低** | Config Manager | Play Mode | 配置加载，SO 校验 |
| **低** | UI Framework | Play Mode | 页面栈，弹窗队列 |
| **低** | Audio Manager | Play Mode | 播放，音量组 |
| **低** | Input Manager | Play Mode | 上下文，缓冲 |
| **低** | Localization | Play Mode | 语言切换 |
| **低** | Level/Scene | Play Mode | 关卡加载 |

---

## 9. API 契约（公开接口）

### 9.1 TestHelper

```
static class TestHelper
{
    static T CreateComponent<T>(string name = "TestObject") where T : Component;
    static GameObject CreateGameObject(string name = "TestObject");

    static UniTask WaitFrames(int frames);
    static UniTask WaitForCondition(Func<bool> condition, float timeoutSeconds = 5f);
    static UniTask WaitForSeconds(float seconds);

    static UniTask InitializeGameGlobalAsync();
    static void CleanupGameGlobal();

    static IEnumerator RunAsync(Func<UniTask> testAction);
}
```

### 9.2 AssertHelper

```
static class AssertHelper
{
    static void Approximately(Vector3 expected, Vector3 actual, float tolerance = 0.0001f);
    static void Approximately(float expected, float actual, float tolerance = 0.0001f);
    static void IsDisposed(IDisposable disposable);
    static void Throws<TException>(Action action) where TException : Exception;
    static UniTask CompletesWithin(Func<UniTask> asyncAction, float timeoutSeconds);
}
```

---

## 10. 使用示例（伪代码）

### 10.1 EditMode 测试（EventBus）

```csharp
using NUnit.Framework;
using PumpGF;

public class EventBusTests
{
    private EventBus _bus;

    [SetUp]
    public void SetUp() => _bus = new EventBus();

    [TearDown]
    public void TearDown() => _bus.Dispose();

    [Test]
    public void Publish_SubscribedHandler_ReceivesEvent()
    {
        // Arrange
        int received = 0;
        _bus.Subscribe<TestEvent>(e => received = e.Value);

        // Act
        _bus.Publish(new TestEvent(42));

        // Assert
        Assert.AreEqual(42, received);
    }

    [Test]
    public void Domain_Isolate_DoesNotCrossDomains()
    {
        // Arrange
        var globalReceived = false;
        var battleReceived = false;
        _bus.Subscribe<TestEvent>(e => globalReceived = true);

        var domain = _bus.GetDomain("Battle");
        domain.Subscribe<TestEvent>(e => battleReceived = true);

        // Act
        domain.Publish(new TestEvent(1));

        // Assert
        Assert.IsFalse(globalReceived, "全局不应收到域内事件");
        Assert.IsTrue(battleReceived, "域应收到事件");
    }
}

public readonly struct TestEvent : IDataCommand { public readonly int Value; ... }
```

### 10.2 EditMode 测试（FSM）

```csharp
using NUnit.Framework;
using PumpGF;

public class StateMachineTests
{
    [Test]
    public void Transition_WhenConditionMet_ChangesState()
    {
        var sm = StateMachineBuilder.Create("Test")
            .State("Idle")
                .TransitionTo("Run").When(() => true)
            .State("Run")
            .InitialState("Idle")
            .Build();

        sm.Tick(0.016f);

        Assert.AreEqual("Run", sm.CurrentState.Name);
    }
}
```

### 10.3 PlayMode 测试（ResMgr）

```csharp
using NUnit.Framework;
using UnityEngine.TestTools;
using System.Collections;
using Cysharp.Threading.Tasks;
using PumpGF;

public class ResMgrPlayModeTests
{
    [UnityTest]
    public IEnumerator LoadAsset_ReturnsHandle() => UniTask.ToCoroutine(async () =>
    {
        // Arrange
        await TestHelper.InitializeGameGlobalAsync();

        // Act
        var handle = await GameGlobal.ResMgr.LoadAssetAsync<GameObject>("TestPrefab");

        // Assert
        Assert.IsNotNull(handle.Asset);
        Assert.IsTrue(handle.IsValid);

        // Cleanup
        handle.Dispose();
    });

    [TearDown]
    public void TearDown() => TestHelper.CleanupGameGlobal();
}
```

### 10.4 TestHelper 使用

```csharp
[Test]
public void CreateComponent_AutoDestroy_AfterTest()
{
    var go = TestHelper.CreateGameObject("Temp");
    var comp = go.AddComponent<Rigidbody>();
    Assert.IsNotNull(comp);
    // TearDown 自动销毁
}

[UnityTest]
public IEnumerator WaitForCondition_Timeout() => UniTask.ToCoroutine(async () =>
{
    bool flag = false;
    // 模拟延迟设置
    UniTask.Delay(100).ToCoroutine(() => flag = true).Forget();

    await TestHelper.WaitForCondition(() => flag, timeoutSeconds: 2f);
    Assert.IsTrue(flag);
});
```

---

## 11. 实现检查清单

- [ ] `PumpGF.Tests` asmdef 创建，引用 Runtime
- [ ] `Tests/` 文件夹按模块分子文件夹
- [ ] `TestHelper` 静态类（CreateGameObject/WaitFrames/WaitForCondition/InitializeGameGlobal）
- [ ] `AssertHelper` 静态类（Approximately/IsDisposed/Throws/CompletesWithin）
- [ ] EditMode 测试模板文档
- [ ] PlayMode 测试模板文档
- [ ] `PumpGF/Generate/Test Template` 菜单（Editor Tools 集成）
- [ ] CI 命令行文档（Unity -runTests）
- [ ] 各模块测试优先级文档
- [ ] `InitializeGameGlobalAsync` / `CleanupGameGlobal` 实现
- [ ] 测试结束自动清理临时 GameObject
- [ ] 所有 TestHelper/API 有中文 XML 注释

---

## 12. 依赖关系

| 依赖项 | 方向 | 说明 |
|--------|------|------|
| PumpGF.Runtime | 引用 | 被测试的框架代码 |
| Unity Test Framework | 引用 | NUnit + Unity 测试运行器 |
| UniTask | 引用 | 异步测试（ToCoroutine） |
| 无 IModule 依赖 | — | 测试代码不依赖 GameGlobal 初始化（EditMode） |

---

## 13. 关键使用规范（业务层 Coding AI 必读）

### 13.1 纯 C# 逻辑用 Edit Mode 测试

- ❌ 禁止：FSM/Entity/EventBus 用 Play Mode 测试（慢）
- ✅ 正确：直接 `new` 实例，Edit Mode `[Test]` 测试

### 13.2 依赖 Unity 的用 Play Mode 测试

- ResMgr/UI/Audio 等用 `[UnityTest]` + `UniTask.ToCoroutine`。

### 13.3 测试必须清理

- 临时 GameObject 用 `TestHelper.CreateGameObject`（自动清理）。
- Play Mode 测试 `TearDown` 调 `TestHelper.CleanupGameGlobal`。

### 13.4 AI 写完代码后运行对应测试

- AI 完成模块代码后，应运行该模块的测试验证。
- 测试失败时修复代码，不是修改测试。

### 13.5 测试命名规范

- `{MethodName}_{Scenario}_{ExpectedResult}`
- 示例：`Publish_SubscribedHandler_ReceivesEvent`

---

**文档结束。实现阶段请严格遵循本契约。**
