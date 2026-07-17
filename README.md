# PumpGF

**PumpGF** 是一个面向个人 / 小团队的 Unity 游戏开发框架，基于 [R3](https://github.com/Cysharp/R3)（响应式编程）+ [UniTask](https://github.com/Cysharp/UniTask)（高性能异步）+ [DOTween](http://dotween.demigiant.com/)（动画）等成熟库构建，涵盖资源、UI、音频、输入、存档、本地化、状态机、事件、生命周期、对象池、调度器、实体组件、关卡场景、调试控制台等常见模块。

作者使用 AI 辅助（Vibe Coding）持续迭代维护，目标是"拿来即用、能跑通、可读、可扩展"。

---

## 适用场景

- 想快速开始一个 Unity 单机 / 小规模联机游戏项目，又不想从零搭轮子
- 认可 R3 + UniTask 这套 **响应式 + 异步** 的开发思路
- 使用 **Unity 2022.3 LTS**（尤其是 2022.3.62f3c1 及以后的补丁版本）
- 使用 AI 辅助编程时，希望框架自带一套 AI 可读的规范和模块设计文档

---

## ⚠️ 支持的引擎版本（强制）

| 引擎版本 | 是否支持 | 说明 |
|---|---|---|
| **Unity 2022.3 LTS**（推荐 2022.3.62f3c1 或更新） | ✅ **唯一官方支持** | 作者主力开发环境 |
| Unity 2022.2 ~ 2022.3.61 | ⚠️ 理论可用 | 未验证，需自行测试 |
| Unity 6 (6000.x) / Unity 2023 | ❌ 未测试 | 可能可以，作者不做保证 |
| Unity 2021 及更早 | ❌ **绝对不支持** | 缺 `destroyCancellationToken`、C# 9 语法等，会产生近 40 处编译错误 |
| 团结引擎 Tuanjie 1.9.x | ❌ **官方不支持**（可自行迁移，见文末） | 团结引擎禁止加载框架内置的 R3 相关 DLL，需自行做迁移 |

> **本框架只对 Unity 2022.3 LTS 提供官方支持。** 其他版本请不要用来提问题，作者概不负责。

---

## 安装步骤（Unity 2022.3 LTS）

框架已把所有第三方依赖以**源码 / DLL** 形式内置在 [`Vendor/`](Vendor) 目录（包括 UniTask、R3.Unity 集成层、R3 核心 DLL、DOTween / DOTween Pro、KCC 等），因此安装只需**一步**：

### 唯一步骤：通过 UPM 添加 PumpGF 的 Git URL

1. 打开 Unity 项目
2. 顶部菜单 **`Window → Package Manager`** 打开包管理器
3. 点击窗口左上角的 **`+`** 按钮 → 选择 **`Add package from git URL...`**
4. 粘贴以下地址后点击 `Add`：

   ```
   https://github.com/OtterSea/PumpGF.git
   ```

5. 等待 Unity 拉取并编译完成，Console 应无编译错误
6. 在代码中即可使用：

   ```csharp
   using PumpGF;
   using R3;
   using Cysharp.Threading.Tasks;
   ```

> 💡 如果你更习惯直接编辑 `Packages/manifest.json`，等效于在 `dependencies` 里追加一条：
> ```json
> "com.pumpgf.framework": "https://github.com/OtterSea/PumpGF.git"
> ```

---

## 内置的第三方库

| 库 | 位置 | 备注 |
|---|---|---|
| **UniTask**（源码） | [`Vendor/UniTask/`](Vendor/UniTask) | 高性能异步 |
| **R3.Unity 集成层**（源码） | [`Vendor/R3.Unity/`](Vendor/R3.Unity) | R3 的 Unity 桥接层 |
| **R3 核心 DLL** | [`Vendor/R3.Unity/Runtime/Plugins/`](Vendor/R3.Unity/Runtime/Plugins) | 响应式编程库主体 |
| **DOTween / DOTween Pro** | [`Vendor/Demigiant/`](Vendor/Demigiant) | 动画 |
| **KCC（Kinematic Character Controller）** | [`Vendor/KCC/`](Vendor/KCC) | 角色控制器 |
| **UniTask.DOTween 扩展**（已改造） | [`Vendor/UniTask/Runtime/External/DOTween/`](Vendor/UniTask/Runtime/External/DOTween) | 让 DOTween 支持 `await` |

框架同时依赖以下 Unity 官方 UPM 包，Unity 会通过 [`package.json`](package.json) 自动解析安装：

- `com.unity.addressables`
- `com.unity.inputsystem`
- `com.unity.cinemachine`

---

## 文档索引

框架的详细使用请阅读以下文档（**给 AI 和人类使用者阅读**）：

| 文档 | 说明 |
|---|---|
| [`AI编程规范/框架说明.md`](AI编程规范/框架说明.md) | 框架能力路由表、模块清单 |
| [`AI编程规范/开发编程规范.md`](AI编程规范/开发编程规范.md) | 代码规范 |
| [`AI编程规范/R3与UniTask使用指南.md`](AI编程规范/R3与UniTask使用指南.md) | 响应式与异步编程指南 |
| [`AI编程规范/KCC使用指南.md`](AI编程规范/KCC使用指南.md) | 角色控制器使用指南 |
| [`AI编程规范/对象池使用指南.md`](AI编程规范/对象池使用指南.md) | 对象池使用指南 |
| [`AI编程规范/踩坑记录.md`](AI编程规范/踩坑记录.md) | **踩过的坑（AI 遇到报错优先查这个）** |
| [`AI编程规范/PumpGF框架功能清单/`](AI编程规范/PumpGF框架功能清单) | 各模块的功能设计指南 |

---

## 附录：在团结引擎（Tuanjie 1.9.x）中使用 PumpGF

> ⚠️ 本节仅作**技术记录**。作者不对团结引擎做官方支持，也不承诺后续版本兼容性，仅提供一个可行的迁移思路供高级用户参考。

### 背景

团结引擎（Tuanjie 1.9.x）基于 Unity 2022 LTS，但对 .NET BCL 程序集的加载做了限制：将 `Microsoft.Bcl.AsyncInterfaces`、`Microsoft.Bcl.TimeProvider` 等 BCL 程序集视为**引擎内置模块**，**不允许 `Plugins/` 目录再加载同名 DLL**。因此，框架内置在 [`Vendor/R3.Unity/Runtime/Plugins/`](Vendor/R3.Unity/Runtime/Plugins) 里的三个 R3 相关 DLL 在团结引擎下会引发加载冲突，导致项目无法正常打开。

R3 官方对此类场景明确建议：改用 [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) 从 NuGet 源安装 R3，以规避 DLL 与引擎内置模块的冲突。

### 迁移步骤

1. **通过 UPM 安装 NuGetForUnity**：在项目 `Packages/manifest.json` 中追加 NuGetForUnity 的 Git URL 依赖：

   ```json
   "com.github-glitchenzo.nugetforunity": "https://github.com/GlitchEnzo/NuGetForUnity.git?path=/src/NuGetForUnity/Assets/NuGet"
   ```

   保存后打开团结引擎，等待 NuGetForUnity 拉取完成。此时顶部菜单栏应出现 **`NuGet`** 菜单项。

2. **删除框架内置的 R3 相关 DLL**：进入 [`Vendor/R3.Unity/Runtime/Plugins/`](Vendor/R3.Unity/Runtime/Plugins)，**删除以下三份 DLL 及其对应的 `.meta` 文件**：

   - `R3.dll` + `R3.dll.meta`
   - `Microsoft.Bcl.AsyncInterfaces.dll` + `Microsoft.Bcl.AsyncInterfaces.dll.meta`
   - `Microsoft.Bcl.TimeProvider.dll` + `Microsoft.Bcl.TimeProvider.dll.meta`

   > ⚠️ Unity 官方 UPM 包默认为**只读**。如果 PumpGF 是以 Git URL 方式引入的，`Packages/PumpGF/` 目录不可直接修改。此时需要把 PumpGF 改为**本地路径引用**（如 `"com.pumpgf.framework": "file:../Packages/PumpGF"`），或在本地维护一份 fork，再执行删除操作。

3. **通过 NuGetForUnity 安装 R3**：在 Unity 顶部菜单点 **`NuGet → Manage NuGet Packages`**，搜索 `R3`（Author: Cysharp），点击 **Install**。NuGetForUnity 会自动解析并安装 R3 及其 BCL 依赖，DLL 会被放置到 `Assets/Packages/` 目录下。

4. **等待编译验证**：Unity 会自动重新编译。理想结果：Console 无编译报错，PumpGF 与业务代码均可正常 `using R3;`。

### 已知风险

- 团结引擎版本迭代较快，NuGetForUnity 与团结引擎的兼容性未必长期稳定
- 若 NuGetForUnity 无法通过 Git URL 拉取，可从其 GitHub Release 下载 `.unitypackage` 手动导入
- 若需打 IL2CPP 包，可能还需额外配置 `link.xml` 防止 R3 相关类型被裁剪，请参考 R3 官方文档
- 迁移完成后，PumpGF 后续升级时需注意：不要覆盖被删除的 `Plugins/` 目录相关文件

---

## 许可证

请根据你实际使用的第三方库（R3、UniTask、DOTween、KCC 等）遵守各自的许可证。PumpGF 本身作为个人框架，暂不做额外授权限制。
