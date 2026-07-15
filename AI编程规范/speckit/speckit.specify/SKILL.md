---
name: speckit.specify
description: |
  Step 1: 将自然语言需求翻译为结构化需求规格文档。
---

# SpecKit: Specify — 需求解析

## 用途

将用户的自然语言需求转化为结构化的需求规格文档，供后续 plan/tasks/implement 使用。产出 `.specify/<主题>/1.spec.md`。

## 前置条件

- 推荐已有 `Assets/AI编程规范/_kilocode/constitution.md`（合规基准）
- 用户提供了需求描述（文字/口头/文档）

## 工作流

### Step 1: 需求收集与理解

- 仔细阅读用户需求描述（功能、背景、约束、参考）
- 读取 `Assets/AI编程规范/_kilocode/constitution.md` 了解项目架构规范
- 用 Glob/Grep 探索项目现有模块/代码，补充理解
- 如需求不够明确，用 `AskUserQuestion` 提出关键问题

### Step 2: 需求结构化

按以下结构输出到 `.specify/<主题>/<N>.spec.md`：

1. **概述** — 一句话核心目的
2. **背景与动机** — 为什么需要、解决什么问题
3. **功能需求** — 核心功能点（checklist）、交互需求、数据需求、事件/通信需求
4. **非功能需求** — 性能、内存、兼容性
5. **涉及的现有系统** — 需修改/新建/依赖的模块
6. **边界条件与异常场景** — 处理方式
7. **验收标准** — 可验证的完成标准（checklist）

### Step 3: 需求确认

- 展示 spec 给用户确认
- 根据反馈迭代更新

## 产物格式

- 路径：`.specify/<主题>/<N>.spec.md`
- 序号：按主题目录内执行步骤编号
- 语言：简体中文 + Markdown
- 引用：代码文件/类名用 Markdown 链接指向实际路径

## 约束

- 不生成 plan/tasks，不写业务代码
- 不引用项目中不存在的模块/文件（Grep 验证）
- 不整读大文件（协议/生成代码等按关键字 Grep）

## 下一步

> 需求规格已生成。推荐后续：
> - 有模糊点 → `/speckit.clarify`
> - 质量审计 → `/speckit.checklist`
> - 已足够清晰 → `/speckit.plan`
