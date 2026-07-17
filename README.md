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

## 依赖库总览

| 库 | 引入方式 | 位置 |
|---|---|---|
| **UniTask** | Git URL（UPM） | 项目层 `Packages/manifest.json` |
| **R3.Unity 集成层** | Git URL（UPM） | 项目层 `Packages/manifest.json` |
| **R3 核心 DLL** | 已内置 | [`Vendor/R3.Unity/Runtime/Plugins/`](Vendor/R3.Unity/Runtime/Plugins) |
| **DOTween / DOTween Pro** | 已内置（源码 + `DOTweenPro.dll`） | [`Vendor/Demigiant/`](Vendor/Demigiant) |
| **KCC（Kinematic Character Controller）** | 已内置（源码） | [`Vendor/KCC/`](Vendor/KCC) |
| **UniTask.DOTween 扩展** | 已内置且做过改造 | [`Vendor/UniTask/Runtime/External/DOTween/`](Vendor/UniTask/Runtime/External/DOTween) |
| **Addressables / InputSystem / Cinemachine / TextMeshPro** | Unity 官方 UPM | 项目层 `Packages/manifest.json` |

除了 UniTask 和 R3.Unity 集成层需要从 UPM 拉取，**其他依赖都已经内置在框架里**，安装完框架即可开箱即用。

---

## 安装步骤（仅针对 Unity 2022.3 LTS）

### 前置条件

- 已安装 Unity 2022.3 LTS（推荐 2022.3.62f3c1 或更新的补丁版本）
- 已有一个空的或现有的 Unity 项目

### 方式一（推荐）：使用 Unity Package Manager 图形界面

Unity 2022 支持在 Package Manager 里通过 "Add package from git URL" 直接拉取 Git 仓库，无需手工编辑 `manifest.json`。

1. 打开 Unity 项目
2. 顶部菜单 **`Window → Package Manager`** 打开包管理器
3. 点击窗口左上角的 **`+`** 按钮 → 选择 **`Add package from git URL...`**
4. **依次**粘贴以下三个地址，每粘贴一个都点 `Add` 并等待完成后再进行下一个（顺序无所谓）：

   ```
   https://github.com/OtterSea/PumpGF.git?path=Packages/PumpGF
   ```
   ```
   https://github.com/Cysharp/R3.git?path=src/R3.Unity/Assets/R3.Unity#1.3.0
   ```
   ```
   https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask
   ```

5. 三个包全部拉取完成后，Unity 会自动编译，Console 应无编译错误
6. 此时你可以在自己代码里：

   ```csharp
   using PumpGF;
   using R3;
   using Cysharp.Threading.Tasks;
   ```

   开始使用框架。

> 💡 如果只想装 PumpGF 主包看看，仅粘贴第一条 URL 即可；但由于 PumpGF 依赖 R3 与 UniTask，缺少后两者会导致大量编译错误。

### 方式二：直接编辑 `Packages/manifest.json`

如果你更习惯编辑 `manifest.json`，打开项目根目录下的 `Packages/manifest.json`，在 `dependencies` 中添加以下三条依赖（如果已有其他条目，合并即可，顺序无所谓）：

```json
{
  "dependencies": {
    "com.pumpgf.framework": "https://github.com/OtterSea/PumpGF.git?path=Packages/PumpGF",
    "com.cysharp.r3": "https://github.com/Cysharp/R3.git?path=src/R3.Unity/Assets/R3.Unity#1.3.0",
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask"
  }
}
```

保存后打开或刷新 Unity，等待包解析完成即可。

> 📌 如果是**本地开发**（例如你已经把 PumpGF 克隆到磁盘），可以把第一行改成本地路径引用，例如：`"com.pumpgf.framework": "file:../Packages/PumpGF"`。

### 关于 R3 核心 DLL

R3 的 UPM 集成包（`com.cysharp.r3`）**只包含 Unity 集成层的源码**，不包含核心 `R3.dll`。核心 DLL（`R3.dll`、`Microsoft.Bcl.AsyncInterfaces.dll`、`Microsoft.Bcl.TimeProvider.dll`）**已内置**在框架内的 [`Vendor/R3.Unity/Runtime/Plugins/`](Vendor/R3.Unity/Runtime/Plugins) 目录，无需额外安装。

> ⚠️ **常见坑**：这三个 DLL 的 `.meta` 文件必须把 `Editor` 平台设置为 `enabled: 1`。如果被误关闭，Editor 里会报 `error CS0234: The type or namespace name 'Collections' does not exist in the namespace 'R3'` 一类的错误。修复方法：在 Unity Inspector 里选中三个 DLL，把 `Editor` 平台勾选启用，或者直接编辑对应的 `.meta` 文件。

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

团结引擎（Tuanjie 1.9.x）基于 Unity 2022 LTS，但对 .NET BCL 程序集的加载做了限制：

- 团结引擎将 `Microsoft.Bcl.AsyncInterfaces`、`Microsoft.Bcl.TimeProvider` 等 BCL 程序集视为**引擎内置模块**，**不允许 `Plugins/` 目录再加载同名 DLL**
- 因此，框架内置在 [`Vendor/R3.Unity/Runtime/Plugins/`](Vendor/R3.Unity/Runtime/Plugins) 里的三个 R3 相关 DLL 在团结引擎下会引发加载冲突，导致项目无法正常打开
- R3 官方对此类场景明确建议：改用 [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) 从 NuGet 源安装 R3，以规避 DLL 与引擎内置模块的冲突

### 迁移步骤

如果你确认要在团结引擎项目中使用 PumpGF，请按以下步骤操作：

#### 1. 通过 UPM 安装 NuGetForUnity

在项目 `Packages/manifest.json` 中追加 NuGetForUnity 的 Git URL 依赖（示例，请以 NuGetForUnity 官方 README 的最新地址为准）：

```json
{
  "dependencies": {
    "com.github-glitchenzo.nugetforunity": "https://github.com/GlitchEnzo/NuGetForUnity.git?path=/src/NuGetForUnity/Assets/NuGet"
  }
}
```

保存后打开 Unity，等待 NuGetForUnity 拉取完成。此时 Unity 顶部菜单栏应出现 **`NuGet`** 菜单项。

#### 2. 删除框架内置的 R3 相关 DLL

进入以下目录，**删除三份 DLL 及其对应的 `.meta` 文件**：

```
Packages/PumpGF/Vendor/R3.Unity/Runtime/Plugins/
├── R3.dll                              ← 删除
├── R3.dll.meta                         ← 删除
├── Microsoft.Bcl.AsyncInterfaces.dll   ← 删除
├── Microsoft.Bcl.AsyncInterfaces.dll.meta  ← 删除
├── Microsoft.Bcl.TimeProvider.dll      ← 删除
└── Microsoft.Bcl.TimeProvider.dll.meta ← 删除
```

删除完成后，`Plugins/` 目录可保留为空目录（连同 `Plugins.meta`），也可以一并删除。

> ⚠️ Unity 官方 UPM 包默认为**只读**。如果 PumpGF 是以 Git URL 或注册表方式引入的，`Packages/PumpGF/` 目录不可直接修改。此时需要把 PumpGF 改成**本地路径引用**（如 `"com.pumpgf.framework": "file:../Packages/PumpGF"`），或直接在本地维护一份 fork，再执行删除操作。

#### 3. 通过 NuGetForUnity 安装 R3

1. 在 Unity 顶部菜单点 **`NuGet` → `Manage NuGet Packages`**
2. 在弹出的窗口顶部搜索框输入：`R3`
3. 从搜索结果中定位 **`R3`**（Author: Cysharp），点击右侧 **Install**
4. NuGetForUnity 会自动解析并安装 R3 及其 BCL 依赖（`Microsoft.Bcl.AsyncInterfaces`、`Microsoft.Bcl.TimeProvider` 等），DLL 会被放置到 `Assets/Packages/` 目录下

#### 4. 等待编译并验证

Unity 会自动重新编译。理想结果：Console 无编译报错，PumpGF 与业务代码均可正常 `using R3;`。

### 已知风险

- 团结引擎版本迭代较快，NuGetForUnity 与团结引擎的兼容性未必稳定
- 如果 NuGetForUnity 无法通过 Git URL 拉取（例如网络问题或仓库结构变动），可从其 GitHub Release 下载 `.unitypackage` 手动导入
- 若需要打 IL2CPP 包，可能还需要额外配置 `link.xml` 防止 R3 相关类型被裁剪，具体请参考 R3 官方文档
- 迁移完成后，PumpGF 后续升级时需要注意：不要覆盖被删除的 `Plugins/` 目录相关文件

如遇更多问题，可参考 [`AI编程规范/踩坑记录.md`](AI编程规范/踩坑记录.md) 顶部关于团结引擎兼容性的说明。

---

## 许可证

请根据你实际使用的第三方库（R3、UniTask、DOTween、KCC 等）遵守各自的许可证。PumpGF 本身作为个人框架，暂不做额外授权限制。
