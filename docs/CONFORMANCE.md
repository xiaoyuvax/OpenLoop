# OpenLoop 符合性指南（语言中性）

> 对应 `CHARTER.md` charter/3。本文件定义"任何语言、任何 UI 的实现，怎样才算 OpenLoop"。
> 判定标准是**跑通第 6 节的测试向量**，不是"照抄参考实现"。参考实现（本仓库 C# 代码）只是其中一种满足方式。

## 1. 判定方式

一个实现符合本规范，当且仅当：

1. 具备第 3 节的子系统与职责边界；
2. 产出第 4 节的数据契约（字段可多，不可少；未列字段实现可自行扩展）；
3. 满足第 5 节的行为契约；
4. 第 6 节全部向量为通过。

不要求：语言、进程模型、UI 框架、存储介质（文件/SQLite 皆可）、模型提供方、目录的绝对路径。
要求：`.openloop/` 的相对布局与文件语义（这是跨实现交换证据的基础）。

## 2. 术语

| 词 | 含义 |
|---|---|
| Worker | 面向用户执行任务的前台 agent |
| Loop | 观察 Worker、收集证据、推动下一版本的后台 agent |
| 版本（def） | 提示词 / 技能 / 策略 / 模型参数的集合，可无编译升级 |
| 基因组（genome） | §12 规定的最小可再生成分，随版本绑定 |
| 考核题（eval task） | Gate 用的隐藏回归案例，对 Worker 与 Loop 不可见 |
| trace | 一次运行的证据（消息、步骤、结论） |
| 账本（ledger） | 版本、采纳、裁决的追加记录 |
| Gate | 比较现役与候选、决定是否采纳的判定器 |

## 3. 必须存在的子系统

| 子系统 | 必须做 | 不得做 |
|---|---|---|
| Worker 循环 | 读消息 → 解析工具调用 → 执行 → 回填结果 → 收敛或达上限退出 | 直接执行自然语言指令（必须有显式工具调用） |
| 工具层 | 文件读写/列目录/搜索/命令执行；命令为 argv 数组、不经 shell | 越出工作区根；把密钥传给子进程 |
| Loop 循环 | 只读证据；产出 findings / 评价提问；不得修改工作区 | 修改用户文件；读取考核题内容 |
| 存储 | `.openloop/` 追加写；损坏时不得丢历史 | 覆盖已写入的 trace/账本行 |
| Gate | 同题同 seed 双跑；候选不低于基线才采纳；平局保留旧版 | 用不同任务集比较 |
| 版本管理 | 成对替换二进制+版本包；记录 parent；可回滚 | 只换其一 |

## 4. 数据契约（最小字段）

路径均相对于工作区根 `<workspace>/.openloop/`。JSON 一律 UTF-8、无 BOM。行式文件（`.jsonl`）每行一个完整 JSON 对象，只追加。

### 4.1 `ledger.jsonl`
```json
{"version":1,"at":"2026-01-01T00:00:00","by":"user","note":"","features":[{"id":"f1","params":{}}]}
```

### 4.2 `defs/vN.json`
```json
{"version":1,"at":"...","by":"...","note":"...","features":[{"id":"f1","params":{}}]}
```
`features` 的 `id` 必须在 `features` 注册表（实现自定）中存在；未注册即拒绝激活。

### 4.3 `traces/<id>/`
- `messages.jsonl`：`{"role":"user|assistant|tool","content":"..."}`
- `steps.jsonl`：`{"tool":"run_command","args":{...},"ok":true,"ms":12}`
- `final.md`：最终答案；`run.json`：`{"id","model","cost","steps","outcome"}`

### 4.4 `eval/tasks/*.json`
```json
{"id":"write-hello","problem":"基础写入","prompt":"...","seed":{"a.txt":"x"},
 "checks":[{"type":"file_exists","path":"a.txt"},
           {"type":"file_contains","path":"a.txt","text":"x"},
           {"type":"file_equals","path":"a.txt","text":"x"},
           {"type":"file_missing","path":"b.txt"},
           {"type":"command","args":["dotnet","--version"],"expectExit":0}]}
```
检查类型至少支持以上五种；未知类型视为该题失败。

### 4.5 `loop/findings.jsonl`
```json
{"id":"...","kind":"access|claim|quality|review|other","severity":"info|warn|high",
 "summary":"...","evidence":"trace/<id>#4","suggestion":"...","at":"..."}
```

### 4.6 `loop/reviews.jsonl`
```json
{"id":"...","session":"...","dimension":"tool_usage","verdict":"good|mixed|bad|unclear",
 "title":"...","attribution":"agent|model|user|unknown","note":"...","at":"..."}
```

### 4.7 `loop/surveys/<session>.json`
数组，元素：
```json
{"id":"s-1","sessionId":"...","anchorTurn":3,"title":"...","body":"...",
 "state":"pending|answered","answerSummary":"...","fields":[
   {"key":"verdict","label":"","kind":"choice|multi|text|scale","options":["合理","有问题","说不清"]}]}
```
约束：同一会话 `pending` 条目 ≤ 3；`answered` 必须同时写一条 `reviews.jsonl`。

### 4.8 `versions/vN.json`（二进制车道）
```json
{"version":7,"parent":6,"origin":"...","commit":"<sha>","binarySha":"<sha256>",
 "defSha":"<sha256>","genome":["charter","prompts","tools","gate","agents-md","trace-schema","rollback"],
 "at":"..."}
```

## 5. 行为契约

### 5.1 工具调用
- 模型以**单行 JSON** 发起：`tool-call: {"tool":"<名>","args":{...}}`；解析失败按"格式错误"处理，最多重试一次，随后停止并报告。
- 工具失败必须回填为 `tool-result: {"ok":false,"error":"..."}`，不得吞掉。
- 每步检查取消信号；达到步数/预算上限即退出，并说明原因。

### 5.2 安全
- 文件类工具：先把目标解析为真实路径（解析符号链接），再判断是否位于工作区根之下；越界一律失败。
- 命令执行：只接受 `argv` 数组；危险命令（删除根目录、格式化磁盘、关机等）直接拒绝；风险命令须经人类批准，无批准通道即失败。
- 子进程环境只保留白名单变量，API 密钥不得进入。
- 每轮任务开始前生成可回滚快照（git 或等价机制）。

### 5.3 Gate
- 现役与候选在**同一考核题集**上各跑一次，题目在独立临时工作区、按 `seed` 铺底。
- 采纳条件：候选通过数 ≥ 基线通过数；相等时保留现役。
- 每次裁决写 `VerdictRecord`（候选/基线通过数、是否采纳、理由），可审计。
- 考核题内容不得进入 Worker/Loop 的提示词或可见工具结果。

### 5.4 版本与升级
- 版本包与二进制必须同版本替换；`versions/vN.json` 记录 `parent`，缺 `parent` 的版本不得激活。
- 升级只对新会话生效；回滚到 `parent` 必须可用，并记录原因。
- 二进制车道前置：`origin`、`commit sha`、工作区干净；缺一项则拒绝立项并记账（退化到版本包车道）。

### 5.5 Loop 的边界
- Loop 只读证据、只写 `findings/reviews/surveys` 与 `loop/task-drafts`；不得写用户文件，也不得直接写 `eval/tasks`（题目须经用户确认）。
- 提问只针对 agent 行为（工具选择/用法/顺序/验证/范围/效率/偷懒/停止时机），不针对答案内容。
- 归因必须给出 `agent | model | unknown`；只有 `agent` 归因进入改进候选。

## 6. 最小测试向量

统一约定：`W` 为临时工作区；"观察到"指对应文件/字段存在且满足断言。

| ID | 给定 | 期望 |
|---|---|---|
| T1 工具执行 | `W` 内提问"创建 hello.txt 写 ok" | `W/hello.txt` 内容为 `ok`；trace 有 ≥1 条 `steps.jsonl` |
| T2 路径越界 | 工具调用读 `../secret.txt` | 失败；`secret.txt` 内容未出现在 trace |
| T3 命令不经 shell | 工具调用 `["echo","a","&&","echo","b"]` | 只执行 echo 并把 `&&` 当普通参数；不得产生两条命令 |
| T4 密钥不外泄 | 设置 `OPENROUTER_API_KEY=sk-test` 后运行一次命令 | 子进程环境无该变量 |
| T5 危险命令 | 工具调用 `rm -rf /` | 直接拒绝，且未启动子进程 |
| T6 Gate 采纳 | 候选在 3 题通过 3，基线通过 2 | 采纳；`VerdictRecord.adopted=true` |
| T7 Gate 平局 | 候选 2/3，基线 2/3 | 不采纳，现役不变 |
| T8 版本回滚 | 激活 v2 后回滚 | 现役回到 v1；`versions/v2.json` 仍在 |
| T9 提问配额 | 同一会话连续创建 4 条 pending | 第 4 条被拒绝；`pending` 计数为 3 |
| T10 证据隔离 | Loop 工具尝试读 `eval/tasks/` | 被拒绝或该路径不可见 |

任一向量失败即不符合；向量可脚本化（无 UI 依赖），实现方须提供一键运行方式。

参考实现的跑法：dotnet test tests/OpenLoop.Core.Tests --filter FullyQualifiedName~ConformanceTests（ConformanceTests.cs 的方法名与 T 编号一一对应；T8 当前标记为 Skip，因为版本登记/回滚尚未实现）。

## 7. 里程碑顺序（建议）

1. Worker 循环 + 工具层（T1–T5）
2. 账本 / trace / 快照
3. Gate + 考核题（T6–T7）
4. Loop 证据与提问（T9–T10）
5. 版本登记、成对替换、回滚（T8）
6. 二进制开发车道（worktree、构建、登记）

## 8. 明确不规定

- UI 形态、窗口/终端、是否有图形界面；
- 存储实现（文件即可，SQLite/对象存储皆可，但路径与字段须满足第 4 节）；
- 模型与供应商、定价与 token 统计方式；
- 内部类名、文件划分、并发模型。
