# PumpGF

20260711 全新游戏开发框架，基于 Vibe 编程。

## ⚠️ 环境要求（强制）

| 项 | 要求 | 说明 |
|----|------|------|
| **Unity / 团结引擎** | **≥ Unity 2022.2** 或 **团结引擎 1.9.x（基于 2022 LTS）** | 框架依赖 [`GameObject.destroyCancellationToken`](https://docs.unity3d.com/2022.2/Documentation/ScriptReference/GameObject-destroyCancellationToken.html)、C# 9 语言特性等 Unity 2022.2+ API |
| **.NET / C#** | C# 9（Unity 2022 LTS 默认） | 框架源码使用 target-typed new、record 等 C# 9 语法 |
| **Scripting Backend** | Mono / IL2CPP 均可 | 已在 Editor + IL2CPP 打包路径验证 |

> ❌ **不要在 Unity 2021 或更早版本使用！**  
> 2021 缺少 `destroyCancellationToken`、`Dictionary.Remove(key, out value)` 等 API，会产生近 40 处编译错误。  
> 之前作者在 Unity 2021 环境让 AI 生成过一次代码，遇到过大规模的兼容性坑（详见文末 [已知坑合集](#已知坑合集踩过的雷)）。

## 安装

### 方式一：以本地 Package 方式引入（推荐）

将 `com.pumpgf.framework` 目录整体放到目标 Unity 项目的 `Packages/` 下，然后在 `Packages/manifest.json` 的 `dependencies` 中加入以下条目（顺序无所谓）：

```json
{
  "dependencies": {
    "com.pumpgf.framework": "file:com.pumpgf.framework",
    "com.cysharp.r3": "https://github.com/Cysharp/R3.git?path=src/R3.Unity/Assets/R3.Unity#1.3.0",
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask"
  }
}
```

### 关于 R3 核心 DLL

R3 的 Unity 集成包（`com.cysharp.r3` Git URL）**仅包含 Unity 集成层的源码**，不带核心 `R3.dll`。核心 DLL（`R3.dll`、`Microsoft.Bcl.AsyncInterfaces.dll`、`Microsoft.Bcl.TimeProvider.dll`）已内置在框架内的 [`Runtime/Plugins/`](Runtime/Plugins) 目录里，无需额外安装。

> ⚠️ **常见坑**：这三个 DLL 的 `.meta` 文件里必须把 `Editor` 平台设置为 `enabled: 1`。如果被误关闭，会出现类似
>
> ```
> UnityFrameProvider.cs(1,10): error CS0234: The type or namespace name 'Collections' does not exist in the namespace 'R3'
> ```
>
> 的编译错误。修复方法：在 Unity Inspector 里选中三个 DLL，把 `Editor` 平台勾选启用，或者直接编辑对应 `.meta`。

## 依赖

| 库 | 引用方式 |
|----|----------|
| **UniTask** | Git URL（见上方 manifest.json） |
| **R3.Unity** | Git URL（见上方 manifest.json） |
| **R3 核心 DLL** | 已内置于 [`Runtime/Plugins/`](Runtime/Plugins) |
| **UniTask.DOTween 扩展** | 已内置于 [`Vendor/UniTask/Runtime/External/DOTween/`](Vendor/UniTask/Runtime/External/DOTween/)（依赖 `DOTween-Scripts` asmdef） |
| **Addressables** | UPM（Unity 自动解析） |

详细的框架说明与开发规范见 [`AI编程规范/框架说明.md`](AI编程规范/框架说明.md)。

---

## 已知坑合集（踩过的雷）

以下是本框架开发过程中曾经踩过的坑，供 AI/人类开发者参考避免。**AI 编码时如遇到相同错误，请优先按此清单排查**。

### 1. R3.Subject / R3.Observable ≠ System.IObservable

R3 为了性能，**没有让 `Observable<T>` 继承 `System.IObservable<T>`**。它们是两套 API。  
- 编译错误：`error CS0266: Cannot implicitly convert type 'R3.Subject<T>' to 'System.IObservable<T>'`  
- ✅ 正确：对外暴露事件流用 `R3.Observable<T>`（`Subject<T>` 可隐式转换为它），而不是 `System.IObservable<T>`。
- ❌ 错误：用 `System.IObservable<T>` 做返回类型或字段类型。

### 2. Unity 2021 缺少 GameObject.destroyCancellationToken

- Unity 2022.2+ 才有 `Component.destroyCancellationToken` / `GameObject.destroyCancellationToken`。
- Unity 2021 编译错误：`error CS1061: 'GameObject' does not contain a definition for 'destroyCancellationToken'`
- ✅ 兼容方案：如果必须支持 2021，改用 UniTask 提供的扩展 `owner.GetCancellationTokenOnDestroy()`（见 [`AsyncTriggerExtensions.cs`](Vendor/UniTask/Runtime/Triggers/AsyncTriggerExtensions.cs)）。
- ✅ 推荐方案：直接强制 Unity 2022.2+（本框架的做法）。

### 3. UniTask.DOTween 扩展默认关闭

- `Vendor/UniTask/Runtime/External/DOTween/DOTweenAsyncExtensions.cs` 原始版本包在 `#if UNITASK_DOTWEEN_SUPPORT` 内，宏由 `UniTask.DOTween.asmdef` 通过 `versionDefines` 检测 UPM 包 `com.demigiant.dotween` 是否安装来触发。
- 本框架用源码方式引入 DOTween（`Vendor/Demigiant/`），**不通过 UPM 安装**，所以宏永远不会被定义 → `Tween.ToUniTask()` 找不到 → 各 `.DOFade(...).ToUniTask(ct)` 全部报错。
- ✅ 已修复：改造 [`UniTask.DOTween.asmdef`](Vendor/UniTask/Runtime/External/DOTween/UniTask.DOTween.asmdef)，直接引用 `DOTween-Scripts`；同时移除 `DOTweenAsyncExtensions.cs` 里的 `#if/#endif` 包裹。

### 4. TMP_Settings.defaultFontAsset 只读

- Unity 版本更新后 `TMP_Settings.defaultFontAsset` 变为只读属性，不能直接赋值。
- ✅ 现改为：切换语言时通过 `LocalizationMgr.OnFontChanged` 事件通知业务层，业务层自行遍历 `TMP_Text` 组件更新字体。

### 5. UnityEngine.Object 与 System.Object 命名歧义

- 在同时 `using UnityEngine;` 和写了 `System.Object`（或 `object` 隐式）的文件里，`Object.Destroy(...)` / `Object.DontDestroyOnLoad(...)` 会产生歧义。
- ✅ 用 `using UObject = UnityEngine.Object;` 别名 + 显式 `UObject.Destroy(...)`。

### 6. Addressables 相关命名空间容易漏

- `Addressables` 类在 `UnityEngine.AddressableAssets`，`AsyncOperationHandle` 在 `UnityEngine.ResourceManagement.AsyncOperations`，`SceneInstance` 在 `UnityEngine.ResourceManagement.ResourceProviders`。三者常一起用，缺一个就报 `CS0246` / `CS0103`。

### 7. UniTask 是 struct，不能用 `??=`

- `UniTask` 是值类型，不是引用类型。写 `_initTask ??= InitCoreAsync()` 会报错。
- ✅ 用 `UniTask?` 可空包装 + 手动 `if (_initTask == null)` 判空。

### 8. ResMgr.LoadAssetAsync 泛型约束

- `LoadAssetAsync<T>` 内部走 Addressables，约束 `T : UnityEngine.Object`。
- 如果外层接口（如 `IConfigProvider.LoadConfigAsync<T> where T : class`）比这个宽松，需要在实现里先按 `UnityEngine.Object` 加载再向下转型。

### 9. R3.dll 的 Editor 平台勾选

见前文"关于 R3 核心 DLL"。

---

## 快速开始

参考 [`AI编程规范/宪章`](AI编程规范/constitution.md) 学习框架能力路由表；参考 [`AI编程规范/框架说明.md`](AI编程规范/框架说明.md) 了解模块清单。
