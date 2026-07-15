# PumpGF

20260711 全新游戏开发框架，基于 Vibe 编程。

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
| **Addressables** | UPM（Unity 自动解析） |

详细的框架说明与开发规范见 [`AI编程规范/框架说明.md`](AI编程规范/框架说明.md)。
