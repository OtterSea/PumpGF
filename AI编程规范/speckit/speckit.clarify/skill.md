---
name: speckit.clarify
description: |
  Step 2: 对 spec 进行澄清。
---

# SpecKit: Clarify — 需求澄清

## 用途

对 spec 文档进行质询式澄清，按 11 类风险分类逐项排查模糊点，通过结构化提问消除歧义，产出高置信度的需求基线。产物：`.specify/<主题>/2.clarify.md` + 回写 spec。

## 前置条件

- `.specify/<主题>/1.spec.md` 已存在

## 工作流

### Step 1: 读取并静默审阅

- 读取 spec 文档，输出审阅摘要：核心目标、已明确的关键点、风险领域
- 用 Glob/Grep 对照项目现状验证 spec 中引用的模块/事件/配置是否真实存在
- 读取 constitution 确认约束边界

### Step 2: 按 11 类风险分类盘点

对每个类别标注是否需要澄清（Yes/No/N/A）：

| # | 类别 | 审查要点 |
|---|------|---------|
| 1 | Scope | 需求边界、In/Out-of-scope |
| 2 | Roles | 触发者是谁、权限要求 |
| 3 | I/O | 输入来源/格式、输出内容 |
| 4 | Data | 字段类型/范围、持久化策略 |
| 5 | UI | 界面入口、状态转换、生命周期 |
| 6 | Event/SDK | 事件方向、订阅生命周期 |
| 7 | Config | 涉及的配置表及字段 |
| 8 | Perf | 帧率预算、GC 压力、对象池 |
| 9 | Edge | 空数据、极端值、时序问题 |
| 10 | Deps | 模块层级、通信方式 |
| 11 | Accept | 如何判定完成、可量化指标 |

每类最多精挑 0-3 个最关键的疑点。

### Step 3: 结构化提问

- **必须**使用 `AskUserQuestion` 工具，每次 1-4 个问题
- **每个问题必须附带 3-4 个离散选项**让用户直接选择，禁止写自由文本问题
- 每个选项包含 label（简短标签）+ description（说明影响/含义）
- 有明确推荐时，在 label 末尾加 `(Recommended)` 排第一
- 优先顺序：Scope → Roles → I/O → Data → Event → UI → Perf → Edge → Deps → Accept → Config
- 不询问 spec 已明确写清的内容
- **禁止**在正文中写开放式问题等待用户回复，所有提问都通过 AskUserQuestion + 选项形式

### Step 4: 回写澄清结论

- 每轮答复后立即更新 spec 对应章节（single source of truth）
- 追加记录到 `.specify/<主题>/<N>.clarify.md`

## 产物格式

- 澄清记录：`.specify/<主题>/<N>.clarify.md`
- spec 回写：原文件追加/修改对应章节
- 包含：澄清类别盘点表、问答记录、spec 变更摘要、遗留 TBD

## 约束

- 不跳过 spec 阅读直接提问
- 不一次堆砌超过 4 个问题
- 不问开放大问题（"你觉得怎么做"）
- 不整读大文件
- 不伪造项目不存在的模块/事件
- 未澄清清楚时不建议进入 plan

## 下一步

> 澄清完成后：
> - 可进一步 `/speckit.checklist` 验证质量
> - 或直接 `/speckit.plan` 生成开发计划
