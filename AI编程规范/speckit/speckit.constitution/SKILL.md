---
name: speckit.constitution
description: |
  创建或增量更新项目宪法。
---

# SpecKit: Constitution — 项目宪法治理

## 用途

创建或增量更新 `Assets/AI编程规范/_kilocode/constitution.md`，形成可被所有 SpecKit skill 引用的权威约束源。宪法是最小且权威的共识，每条都应能被直接援引。

## 前置条件

- 无（首次创建）或已有 `Assets/AI编程规范/_kilocode/constitution.md`（更新模式）

## 工作流

### Step 1: 盘点现状

- 检测 `Assets/AI编程规范/_kilocode/constitution.md` 是否存在 → 决定创建/更新模式
- 若存在，读取并输出摘要（版本号、章节目录、规模）
- 用 Glob/Grep 快速了解项目结构（不整读大文件）

### Step 2: 询问变更意图

用 `AskUserQuestion` 明确：
- 变更类型：增量补丁 / 新增章节 / 修订现有章节 / 整体重写
- 影响范围：哪些章节/主题需要变更
- 触发原因：事故 / 约定变更 / 新模块 / 框架升级

### Step 3: 采集具体诉求

- 新增/删除/修改的条款文本
- 每条变更的触发原因（写入版本说明）
- 是否需要同步提示下游 skill

### Step 4: 条款生成规则

每条宪法条款必须：
- 可审计 — 配有证据（代码路径/事故记录）
- 可执行 — 用"必须/禁止/推荐"强约束词
- 可落地 — 在当前项目结构下真实可遵守
- 命名精确 — 文件/类/协议给出确切路径
- 无冲突 — 不与现有条款矛盾

### Step 5: 版本化写回

- 归档旧版本到 `Assets/AI编程规范/_kilocode/constitution.history/constitution-v{旧版本}-{日期}.md`
- 更新主文件：版本号递增（MAJOR.MINOR.PATCH）、日期更新
- 维护变更日志章节

### Step 6: 下游同步提示

输出受影响的下游 skill 列表，提示用户复核。本 skill 不直接改下游文件。

## 产物格式

- 主文件：`Assets/AI编程规范/_kilocode/constitution.md`
- 历史快照：`Assets/AI编程规范/_kilocode/constitution.history/constitution-v{旧版本}-{日期}.md`
- 版本号：MAJOR（重写）、MINOR（新增章节）、PATCH（修订）

## 约束

- 不修改 `.specify/*` 或业务代码
- 不写入未经用户确认的条款
- 不引用项目中不存在的模块/路径
- 不在一次调用里同时做"整体重写 + 补丁"

## 下一步

> 宪法就绪后，使用 `/speckit.specify` 开始需求分析。
> 若变更涉及编码规范，建议复核相关 skill 是否仍一致。
