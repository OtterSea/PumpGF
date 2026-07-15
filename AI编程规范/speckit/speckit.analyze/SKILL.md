---
name: speckit.analyze
description: |
  Step 6: 对 spec/plan/tasks 三份产物做只读静态审计。
---

# SpecKit: Analyze — 跨产物一致性分析

## 用途

在进入编码前，对 spec/plan/tasks 三份产物做静态审计，确保产物间一致。产物：`.specify/<主题>/6.analyze.md`。

## 前置条件

- `.specify/<主题>/1.spec.md` 已存在
- `.specify/<主题>/3.plan.md` 已存在
- `.specify/<主题>/4.tasks.md` 已存在

## 工作流

### Step 1: 加载全部上下文（只读）

- 读取 spec / plan / tasks 三份文件
- 若存在 clarify.md 同时加载
- 读取 constitution 获取规范约束
- 用 Glob/Grep 按需校验代码现状（验证引用有效性）

### Step 2: 构建三域抽取表

从三份产物中抽取可比对的原子项：
- **Requirements**（from spec）— 功能点、数据字段、事件、边界、验收项
- **Design**（from plan）— 文件清单、模块接口、事件列表、数据模型
- **Tasks**（from tasks）— 每个 Task 的目标/文件/验证标准/依赖

### Step 3: 八类一致性检查

| # | 类别 | 检查要点 |
|---|------|---------|
| 1 | 需求覆盖 | 每条 spec 需求是否被 plan 和 tasks 承接 |
| 2 | 设计漂移 | plan 是否引入 spec 未要求的能力（镀金） |
| 3 | 任务完整性 | plan 文件清单是否全部出现在 tasks 中 |
| 4 | 引用有效性 | 引用的文件/事件/配置是否真实存在（Glob/Grep） |
| 5 | 依赖有效性 | 任务依赖图是否有循环、孤岛 |
| 6 | 规范冲突 | 是否违反 constitution 约束 |
| 7 | 生命周期 | 事件订阅/资源加载是否有配对清理 |
| 8 | 风险覆盖 | spec 风险/边界是否在 tasks 中有对策 |

### Step 4: 严重度分级

- **Critical** — 必须修复才能进入 implement
- **Major** — 强烈建议修复
- **Minor** — 建议修复
- **Info** — 仅记录

### Step 5: 输出分析报告

写入 `.specify/<主题>/<N>.analyze.md`，包含：产物概览、三域抽取表、八类检查结果、覆盖矩阵、问题汇总、修复建议、放行判定。

### Step 6: 放行交互

- 输出摘要（Critical/Major 计数 + 总体结论）
- 存在 Critical 时必须用户显式二次确认才放行

## 产物格式

- 路径：`.specify/<主题>/<N>.analyze.md`
- 总体结论：PASS / PASS WITH WARNINGS / FAIL
- 幂等：可反复运行，追加版本 vN

## 约束

- **绝不修改** spec/plan/tasks 内容（只读审计）
- 不写业务代码
- 不整读大文件
- 不伪造项目不存在的模块/事件
- 存在 Critical 时不默认放行

## 下一步

> 分析结果：
> - PASS → 执行 `/speckit.implement` 开始编码
> - PASS WITH WARNINGS → 可编码，注意跟进 Warnings
> - FAIL → 回到 `/speckit.plan` 或 `/speckit.tasks` 修复
