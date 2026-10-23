# 设计：题目来源、确认界面与评估报告

> 状态：**已实现（P1–P3）**。实现落点见 §5 与 §10；未做的部分明确列在 §2 非目标。
> 目标读者：实现者；也用于判断改动是否越界。

## 1. 问题

当前考核题只能手写 JSON 放进 `<workspace>/.openloop/eval/tasks/*.json`（`docs/CONFORMANCE.md` §4.4）。问题：

- 字段多（`seed` / `scope` / `checks`），用户出题成本高；
- Loop 已经在收集证据（findings、traces、子 agent 记录），但没有出口把「观察到的失败」变成题；
- Gate 结果只存在于内存，UI 只有一条汇总通知（`SessionView.cs:762`），用户看不到逐题对比；
- 失败候选和裁决已落账，但缺少面向人的报告页。

## 2. 目标与非目标

**目标**

1. Loop 能从已有证据起草题目（draft），用户确认后才进入正式题集。
2. 确认界面是结构化的（任务/起始文件/判定条件/来源），不要求用户读 JSON。
3. 每次 Gate 评估产出可回看的报告页：逐题 baseline vs candidate、成本、步数、证据。
4. 不破坏「题集对 Worker/Loop 不可见」的隔离（`docs/CONFORMANCE.md` §5.3、T10）。

**非目标**

- 不自动采纳题目，不自动触发演进（仍由用户点「演进一轮」）。
- 不做题库市场/跨工作区共享。
- 不让 Loop 读取 `eval/`（含 runs/reports）；报告只给人看。
- 不新增检查类型；沿用现有 5 种（`EvalTask.cs:105-112`）。

## 3. 信任边界（硬约束）

| 约束 | 理由 |
|---|---|
| Loop 只能写 `.openloop/loop/` 下的草稿，不能写 `eval/tasks/` | 沿用 §5.5「Loop 只读证据」；题目入库必须经人 |
| 草稿必须引用证据 id（finding/trace/session） | 防止凭空想象题目；可回指 |
| 草稿生成与特征候选（`LoopAgent`）分两次调用、不共享上下文 | 防止「自己出题自己判卷」 |
| 采纳后题目即 held-out：Worker 工具（`.openloop` 屏蔽）与 `read_evidence`（deny `eval`）都读不到 | 现有 `WorkspaceScope.cs:21`、`ReadEvidenceTool.cs:19-20` |
| 判定条件只允许确定性的文件/命令检查 | 避免 LLM 判卷；与 Gate 可复现性一致 |
| 同一题重复采纳需拦截（prompt+checks 指纹） | 防止题集膨胀和刷分 |

## 4. 数据契约

### 4.1 草稿 `loop/task-drafts/<draftId>.json`

Loop 可写；用户确认后改写 `status`。采用 per-file（与 surveys 同风格），避免数组文件并发写。

```json
{
  "id": "d-20261023-153000-1",
  "status": "draft",
  "reason": "用户要求批量写文件时，Worker 只回复“已完成”而未落盘",
  "source": {
    "finding": "F-1a2b3c4d",
    "trace": "traces/<traceId>",
    "session": "<sessionId>"
  },
  "task": {
    "id": "batch-write-8",
    "problem": "谎报完成：批量写入",
    "prompt": "在工作区创建 f01.txt 到 f08.txt，每个文件写自己的编号。",
    "seed": {},
    "scope": ["f01.txt"],
    "checks": [
      {"type": "file_exists", "path": "f01.txt"},
      {"type": "file_equals", "path": "f01.txt", "text": "1"}
    ]
  },
  "createdAt": "2026-10-23T15:30:00",
  "decidedAt": null,
  "decidedBy": null
}
```

字段校验（入库草稿时即执行，复用 `TaskSet.Validate` 的规则）：

- `task.id` 非空、`prompt` 非空、`checks` 非空且类型合法；
- `source` 至少一个非空引用；
- `checks` 至少一条确定性检查（`file_*` 或 `command`）；
- 与现有 tasks 及草稿做 `Fingerprint` 去重（`EvalTask.cs:73`）。

### 4.2 采纳

用户点「采纳」时：

1. 对 `task` 再跑一次完整校验；
2. 写 `<workspace>/.openloop/eval/tasks/<task.id>.json`（UTF-8 无 BOM）；
3. 草稿 `status=accepted`、`decidedAt/decidedBy=user`；
4. 可选「试跑基线」：用 `AgentTaskRunner` 在当前版本跑一次，报告通过/失败；**基线通过时提示「该题区分度不足」**，但不阻止入库（人有权保留）。

「丢弃」只改 `status=rejected`，不删文件。

### 4.3 报告 `eval/reports/<reportId>/`

Gate 结束后由 Core 落盘，用户页只读。

```json
{
  "id": "r-20261023-160000",
  "at": "2026-10-23T16:00:00Z",
  "binary": "<RuntimeInfo.Version>",
  "baselineVersion": 3,
  "candidateVersion": 4,
  "adopted": true,
  "reason": "候选 4/5 > 基线 3/5",
  "baselinePassed": 3,
  "candidatePassed": 4,
  "total": 5,
  "cost": 0.42,
  "build": {"ok": true, "detail": ""},
  "conformance": [{"name": "可回指", "ok": true, "detail": ""}],
  "tasks": [
    {
      "id": "batch-write-8",
      "fingerprint": "<sha256>",
      "baseline": {"passed": false, "steps": 6, "cost": 0.01,
                   "detail": "f03.txt 不存在", "evidence": "eval/runs/batch-write-8-v3-..."},
      "candidate": {"passed": true, "steps": 9, "cost": 0.02,
                    "detail": "全部通过", "evidence": "eval/runs/batch-write-8-v4-..."}
    }
  ]
}
```

`report.json` 之外再写 `report.md`（同样的信息，给人看；便于直接打开证据目录）。

## 5. 组件与改动点

### 5.1 Core

| 组件 | 文件（新/改） | 说明 |
|---|---|---|
| 草稿存储 | 已加 `Store/TaskDrafts.cs` | `Load/Add/Update/Accept/Reject`；`Accept` 校验 + 写 `eval/tasks` + 按指纹去重 |
| 出题工具 | 已加 `Tools/Builtin/ProposeTaskTool.cs` | 参数 `task`(对象) + `reason` + `source`(对象或字符串)；只写草稿 |
| 工具注册 | 已改 `Tools/ToolRegistry.cs` | 仅 `Loop(...)` 追加 `ProposeTaskTool`；`Workspace(...)` 不变 |
| 报告存储 | 已加 `Loop/EvalReport.cs` | `EvalReportStore.Save(workspace, EvalReport)` → `eval/reports/<id>/`；`List`；放在 Loop 侧避免 Store→Loop 反向依赖 |
| 报告落盘 | 已改 `Loop/Gate.cs` | `EvaluateAsync` 返回前落盘（含构建/符合性短路），`GateReport.ReportPath` 供 UI 用 |
| 题集校验复用 | 已改 `Store/EvalTask.cs` | `TaskSet.ValidateTask` / `NormalizeTask` 公开，草稿与题集共用（逻辑不变） |

`GateReport` / `TaskComparison` 已有所需字段（`Gate.cs:39-51`、`:19-31`），只需补 `steps`（`TaskOutcome.Steps` 已有）和持久化。

### 5.2 Loop 提示词

`App/LoopPrompt.cs` 增加一条规则：

- 观察到可复现的失败模式时，用 `propose_task` 落草稿；必须给 `source`；
- 不得把猜测写成判定条件；不得直接操作 `eval/`；
- 一轮最多起草 1 题，避免批量噪声。

### 5.3 UI（App）

| 位置 | 行为 |
|---|---|
| Worker 工具栏 | 新增「题目候选 (N)」按钮，N = `status=draft` 数；点击开面板 |
| 候选面板 | 列表卡片：`problem`、`prompt`、起始文件（键 + 截断内容）、检查项自然语言（如「f01.txt 内容等于 1」）、来源（finding 摘要 + 打开 trace）；按钮：采纳 / 编辑 / 丢弃 |
| 编辑 | 已实现为「预览 ⇄ JSON 编辑」切换：保存前走 `TaskSet.ValidateTask` + 去重校验，错误显示在状态行。结构化表单未做（见 §2 非目标/后续） |
| 采纳后 | 提示「已加入题集」，可选「现在跑一次基线」 |
| 评估报告 | 工具栏「评估报告」：历史列表 + 详情页（顶部结论/版本/成本，表格逐题对比，证据目录可打开） |
| Loop 页 | 可选：状态栏显示草稿数，不新增第二个出题入口 |

面板/页面按现有模式实现：`SessionView` + `.Designer.cs` 成对，运行时初始化放 `Initialize(...)`；AntdUI 控件属性以 `AntdUI.xml` 为准。

## 6. 端到端流程

```
Loop 会话（观察）
  └─ record_finding / propose_task ──> .openloop/loop/task-drafts/*.json
                                              │
用户打开「题目候选」 ── 采纳/编辑/丢弃 ────────┤
                                              └─ 采纳 ──> .openloop/eval/tasks/<id>.json
用户点「演进一轮」
  └─ LoopAgent 提案（看不到题）
       └─ Gate: 构建 → 符合性 → 每题 baseline/candidate 双跑
            ├─ 裁决写入账本（现有）
            └─ 报告写入 .openloop/eval/reports/<id>/
用户打开「评估报告」 ── 逐题对比 + 证据目录
```

## 7. 失败与边界

| 情况 | 处理 |
|---|---|
| 草稿 JSON 非法 | 工具返回 `ok:false` 并指出字段；不落盘 |
| 草稿与现有题重复 | 拒绝并返回已存在的 task id |
| 用户采纳时题 id 冲突 | 阻止；提示改用编辑或丢弃旧题 |
| 基线试跑通过 | 允许入库，但标注「区分度不足」 |
| 报告写盘失败 | Gate 结果不丢：账本裁决照写，UI 提示报告缺失 |
| Loop 尝试读 `eval/` | 现有工具/证据通道拒绝（保持 T10） |
| Loop 尝试直接写 tasks | 无写权限（`.openloop` 被 Worker scope 拒绝；Loop 工具也不暴露写通道） |

## 8. 分阶段落地

1. **P1 草稿闭环**：`TaskDrafts` + `ProposeTaskTool` + LoopPrompt 规则 + 候选面板（只读展示 + 采纳/丢弃）。验收：Loop 起草 → 用户采纳 → 题进入 `tasks`。
2. **P2 编辑与试跑**：结构化编辑 + 基线试跑 + 去重/冲突提示。
3. **P3 报告页**：`EvalReports` + Gate 落盘 + 报告列表/详情 + 证据目录跳转。

每阶段独立可用；P1 不依赖 P3，P3 不依赖 P1。

## 9. 验收与测试

- `TaskDraftsTests`：非法草稿拒绝、采纳写文件、重复拒绝、丢弃不写题集。
- `ProposeTaskToolTests`：只在 `Loop` 注册；Worker 注册表找不到该工具。
- `EvalReportsTests`：`GateReport` → 文件 → 读回字段一致；拣选/排序。
- 符合性新增向量（建议）：
  - **T11 题目入库**：Loop 起草后未经确认，`eval/tasks/` 无新增；确认后才出现。
  - **T12 报告可审计**：一次 Gate（采纳与拒绝各一）后，`eval/reports/` 有逐题对比，且与账本裁决一致。
- UI 冒烟：无题 → 起草 → 采纳 → 演进 → 报告页打开证据目录，250% DPI 下无裁切。

## 10. 实现落点（已验证）

- 草稿：`TaskDrafts` 读写 `.openloop/loop/task-drafts/<id>.json`；`Accept` 写 `eval/tasks` 并把 `Source` 固定为 `user`，指纹去重覆盖已有题与草稿。
- 出题：`propose_task` 只在 `ToolRegistry.Loop` 注册；Worker 的 `ToolRegistry.Workspace` 找不到该工具。
- 报告：`Gate.EvaluateAsync` 无论采纳、平局、构建/符合性短路都会写 `eval/reports/<id>/report.json` 与 `report.md`，`GateReport.ReportPath` 回传相对路径。
- UI：Worker 侧栏新增「题目候选 N」「评估报告 N」；候选对话框支持预览、JSON 编辑、采纳、丢弃、采纳后试跑基线；报告对话框按历史列表展示 `report.md` 并可打开证据目录。
- 测试：`TaskDraftsTests`（草稿/去重/采纳/工具注册/报告读写）+ `Gate_writes_human_readable_report`。
- 契约：`docs/CONFORMANCE.md` §5.5 已允许 Loop 写 `loop/task-drafts`，仍禁止直接写 `eval/tasks`。
- 未做：结构化字段编辑器、跨工作区题库、自动触发演进、Loop 读取 eval 报告。
