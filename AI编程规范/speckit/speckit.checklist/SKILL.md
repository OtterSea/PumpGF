---
name: speckit.checklist
description: |
  Step 5: 为 spec 文档生成逐条断言需求的完整性/清晰度/一致性/可测性。
 
---

# SpecKit: Checklist — 需求质量清单

## 用途

为当前 spec 生成定制化断言清单，逐条验证需求文字的完整性、清晰度、一致性、可测性和可行性。每条断言可证伪（Pass/Fail/Warn/N-A）。产物：`.specify/<主题>/5.checklist.md`。

## 前置条件

- `.specify/<主题>/1.spec.md` 已存在

## 工作流

### Step 1: 加载需求

- 读取 spec 文件；若存在 clarify.md 一并读取
- 用 Glob/Grep 做最小必要的项目现状校验（验证 spec 引用的模块/事件是否真实存在）
- 读取 constitution 获取项目约束

### Step 2: 询问侧重点（可跳过）

用 `AskUserQuestion` 让用户选择清单风格：
- 平衡（默认）— 覆盖全部 5 维度
- 偏完整性 — 重点查漏
- 偏可测性 — 重点查验收标准
- 偏一致性 — 重点查术语统一

### Step 3: 按 5 维度生成断言

针对 spec 实际内容逐章节生成：

- **D1 完整性** — 5W1H 是否说完？数据字段类型/来源？边界？
- **D2 清晰度** — 代词指代？模糊量词？术语一致？
- **D3 一致性** — 内部自洽？与 clarify 决策一致？与项目现状一致？
- **D4 可测性** — 验收标准可观测？边界值？错误路径期望行为？
- **D5 可行性** — 是否违反 constitution 红线？引用是否真实？

### Step 4: 评估每条断言

每条清单项包含：
- ID：`CHK-<维度>-<序号>`
- 断言：一句可证伪的陈述
- 证据：指向 spec 具体章节
- 结果：Pass / Fail / Warn / N-A
- 严重度（Fail/Warn 时）：Critical / Major / Minor / Info
- 修复建议

### Step 5: 输出清单文档

写入 `.specify/<主题>/<N>.checklist.md`，包含摘要表、分维度详细结果、按严重度聚合的 TODO、放行判定。

## 产物格式

- 路径：`.specify/<主题>/<N>.checklist.md`
- 总体结论：PASS / PASS WITH WARNINGS / FAIL
- 幂等：每次运行追加新版本 vN

## 约束

- 不生成通用模板式清单（必须基于当前 spec 内容定制）
- 不直接修改 spec 正文（只能追加 TODO 段落，征得同意后）
- 不写业务代码
- 不整读大文件
- 不写无法证伪的断言

## 下一步

> 清单结果：
> - PASS → 进入 `/speckit.plan`
> - PASS WITH WARNINGS → 建议先补齐 Major 项
> - FAIL → 回到 `/speckit.specify` 修正后重跑
