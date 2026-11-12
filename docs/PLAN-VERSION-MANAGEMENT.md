# 版本管理实施方案

> 状态：**P0–P5 全部实施并实测**（见 §11.1 实施进展）。
> 文中「要做/将改为」的设计与现状有差异时，以 §11.1 的落地说明为准；§12 单列已验证的既有能力。
> 本文取代 `docs/PLAN-DUAL-PROCESS.md` 中「更新通道的生产者 = `scripts/publish-role.ps1`」与
> 「壳覆盖由独立重启脚本执行」两处描述（那两处已同步改写为历史记录）。
> 已实现并已验证的部分单列在 §12，不与计划混写。

## 1. 目标与原则

1. **版本号是「依赖闭包指纹」的可读标签**，不是构件编号。身份三元组：
   `版本号`（人读）+ `源码快照`（git tree/提交）+ `产物哈希`（目录后缀 `-sha8`）。
2. **版本号只有一个来源**：各仓库自己的 `<Version>`。程序集版本由它派生，**只做构建期断言**，
   不参与任何「用哪个版本 / 要不要升版」的判断。
3. **上游变了，下游必须升**：组件自身内容变化，或它依赖的版本/指纹变化，都算污染；升版沿依赖边级联。
4. **I 主控，壳执行**：I 决定升谁、升到几、什么时候升；壳只做 stage / restart / 回滚；
   A 只负责「换 I」（I 不能换自己，也不能替换运行中的壳）。
5. **运行态不出现平台相关文本脚本**：不生成、不调用 `.ps1/.cmd/.bat/.sh`；
   需要换进程时由组件自己的二进制带子命令完成。
6. **构建前必须有源码快照**，但**不自动提交到分支**；分支提交与 push 只在人工触发时发生。

## 2. 版本节点与依赖图

七个节点，部署态出现四个（两种壳不会同时存在；若同时存在则共用同一个号、同步升版）。

| 节点 | 版本声明位置 | 部署产物 | 备注 |
|---|---|---|---|
| core | `src/OpenLoop.Core/OpenLoop.Core.csproj` | 无（被嵌入） | 现缺 `<Version>` |
| service | `src/OpenLoop.Service/OpenLoop.Service.csproj` | 无（被嵌入） | 现缺 `<Version>` |
| a | `src/OpenLoop.A/OpenLoop.A.csproj` | 有 | |
| i | `src/OpenLoop.I/OpenLoop.I.csproj` | 有 | |
| shell | `src/OpenLoop.App/OpenLoop.App.csproj`（Windows）／`src/OpenLoop.Shell/OpenLoop.Shell.csproj`（其他平台） | 有 | **一个号，两种产物**；只允许一个文件承载 `<Version>`，另一处读取它 |
| puppetcore | `<ComponentRoot>/PuppetCore/Puppet.Common.targets` | 无 | 一处管全家族；I 可写 |
| chatflow | `<ComponentRoot>/ChatFlow/ChatFlow/ChatFlow.csproj` | 无 | I 可写 |

边（只算**编译期**依赖）：

```
puppetcore → service → { a, i, shell }
chatflow   → shell
core       → service
```

不算边的情况：

- `OpenLoop.App` 对 A/I 的 `ReferenceOutputAssembly="false"` 是**打包**，不是依赖（改 A 不得让壳升号）。
- `FastColoredTextBox` 走 `Vax-FCTB` 包进入 chatflow，不单列节点（见 §6.4）。

## 3. 版本号字段关系

| 字段 | 来源 | 用途 |
|---|---|---|
| `<Version>` | 各仓库人工/自动写入 | **唯一权威**；级联与文件名都用它 |
| `AssemblyVersion` | SDK 由 `<Version>` 派生（`0.1.3.0`） | 仅断言；未强签名，无绑定力 |
| `FileVersion` | 同上 | 文件属性 |
| `InformationalVersion` | `<Version>+<sha>[.dirty]` | **唯一对外的构建身份**：health / `--info` / manifest / trace |
| `PackageVersion` | `<Version>`（只在打包时存在） | 依赖侧的身份；不打包的组件没有这个概念 |
| 目录后缀 `-sha8` | 产物 exe 的 SHA256 前 8 位 | 产物内容身份（≠ git 提交） |

实施要求：

- **统一出口**：新增 `ComponentInfo.Local()`，读入口程序集的 `InformationalVersion`；
  `BusHost`（现用 `GetName().Version.ToString(3)`）、`SourceSync`（运行版本）、`IterationRunner.ExeVersion`、
  `Core.Agent.RuntimeInfo`（现取 `typeof(AgentLoop).Assembly`，永远报 Core 的 `1.0.0`）全部改走它。
- **`.dirty` 标记**：构建时工作树不干净则追加 `.dirty`（MSBuild target 读 `git status`，声明式），
  manifest 同时记 `source.dirty`。
- **一致性断言**（构建期）：
  `InformationalVersion` 的号部分 == `<Version>`；被引用的 dll 的 `AssemblyName.Version` 的
  `major.minor.build` == 声明的依赖版本。
- 现有 csproj 里没有 `<AssemblyVersion>` / `<FileVersion>` 覆盖，保持「只写 `<Version>`」。

## 4. 依赖身份与指纹

**数据来源**：`obj/project.assets.json`（restore 后生成，含直接 + 传递依赖的解析结果）。
不调用 `dotnet list package`（慢且是第二份真相）。

```
fingerprint(node) = SHA256(
    "tree:" + 源码 tree 哈希(node) + "\n" +
    "deps:" + sort(
        for 每个直接/传递包:   "pkg:" + id + "@" + version
        for 每个项目引用依赖:  "proj:" + id + "@" + version + "#" + fingerprint(依赖)
    )
)
```

- 项目引用的依赖按拓扑序算，递归取指纹；包依赖取解析后的版本号。
- `bundled`（`ReferenceOutputAssembly=false`）不进指纹。
- 外部仓库（PuppetCore / ChatFlow）的源码 tree 用 `Snapshots.Tree(root)` 计算，失败时退回 `HEAD` 提交号。

**manifest 增补**（`src/OpenLoop.Service/Shell/VersionManifest.cs`）：

```json
{
  "role": "a", "version": "0.1.3", "protocol": 1,
  "exeSha256": "…", "filesSha256": "…",
  "source": { "ref": "refs/openloop/version/a/0.1.3", "rev": "…", "tree": "…", "dirty": true,
              "changed": ["src/OpenLoop.A/Program.cs", "…"] },
  "deps": [ { "id": "core", "kind": "project", "version": "0.1.3", "fingerprint": "…" },
            { "id": "service", "kind": "project", "version": "0.1.3", "fingerprint": "…" },
            { "id": "puppet.core", "kind": "package", "version": "1.7.2" } ]
}
```

**运行态基准**：`versions/state.json`（只有 I 写）——库节点没有产物，必须单独存基准：

```json
{ "core": { "version": "0.1.3", "fingerprint": "…", "deps": [ … ], "at": "…" },
  "service": { … }, "a": { … }, "i": { … }, "shell": { … },
  "puppetcore": { … }, "chatflow": { … } }
```

`state.json` 缺失（首次运行）→ 所有节点判为污染，全量升一次号（已确认接受）。

## 5. 升版算法

```
1. 生成源码快照：Snapshots.Tree(SourceRoot) 及各组件仓库 tree
2. dotnet restore（在当前 csproj 状态下）
3. 拓扑序 Core → Service → {A,I,Shell}，并先算 puppetcore / chatflow：
      fingerprint != state.fingerprint  → 该节点受污染
      任一依赖受污染/升号 → 该节点也算受污染（级联）
4. 受污染节点：target = VersionBump.NextVersion(该节点 <Version>, 运行版本)
       （源码号高于运行号用源码号，否则 patch+1；自动只做 patch）
5. 一次性写回所有 target 的 <Version>（含外部仓库的单一版本文件）
6. dotnet build → 发布受影响的部署节点 → 产物自己写 manifest
7. 成功：写 state.json、写 update-log.jsonl、manifest 落 deps
8. 失败：**保留新号**（已确认），日志记一次失败；修复后重跑发布同一个号
```

规则：

- **手动与自动升版是同一个操作**，只差日志里的 `by`（`user` / `agent` / `i`）。
- 日志字段：`at / by / node / from / to / reason(dirty|dep) / sourceRef / result`，
  写进现有 `runtime/update-log.jsonl`。
- **回撤是显式操作**：把 `<Version>` 改回旧号 + 记一条 `revert` 日志；不做自动回退，不改写历史。
- 版本号只增不减；失败不产生「未登记的二进制」（发布失败即没有产物目录）。

## 6. 依赖模式与一致性强制

### 6.1 包依赖

- 引入 **Central Package Management**：新增 `Directory.Packages.props`，
  `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`，
  各 csproj 的 `PackageReference` 去掉 `Version`。
- 收敛现有声明：`Puppet.Core` / `Puppet.Core.WinForms` 1.7.2、`AntdUI` 2.4.15、
  `CDS.Markdown.Lite` 1.5.7（App 与 ChatFlow 各钉一次，本仓收敛到 props 一处）、
  `System.Security.Cryptography.ProtectedData` 10.0.0。
- 托管依赖清单 `versions.lock.json`（仓库内，跨仓包的唯一允许版本）+ `Directory.Build.targets`
  里的 target：任何 `PackageReference` 版本与清单不符 → `Error`。

### 6.2 源码依赖

- `ProjectReference` 用相对路径，确保开发态（同级仓库）与安装态（`<安装根>/source/<repo>`）通用；
  ChatFlow 现有写法已验证两种布局都落到同一相对位置。
- **同一依赖全仓只允许一种模式**：target 扫描「同一程序集既被 ProjectReference 又被 PackageReference」→ `Error`。

### 6.3 库节点

- `Core` / `Service` 补 `<Version>`、显式 `<IsPackable>false</IsPackable>`（避免 `dotnet pack` 产出 `1.0.0` 假包）。
- 它们不发布，只有版本号与指纹进 `state.json`。

### 6.4 本轮确定的三处修正

1. 删除 `src/OpenLoop.App/OpenLoop.App.csproj` 里的 `<PackageReference Include="ChatFlow" Version="0.1.8" />`
   （与 ChatFlow 的 ProjectReference 重复，同一程序集两种模式）。
2. ChatFlow 仓库改为**只消费 `Vax-FCTB` 包**：删掉它基于「同级源码是否存在」的 conditional
   ProjectReference。理由：FCTB 是强签名控件库、不参与迭代；conditional 会让同一份 OpenLoop 源码
   在不同机器上编出不同依赖闭包（源码版默认 `1.0.0.0` vs 包版 `2.19.x`）。
   OpenLoop 侧本来就是经 ChatFlow 传递引用 FCTB（`FilePreviewView.cs` 直接用其类型），无需再显式引用。
3. `CDS.Markdown.Lite`、`AntdUI` 收进 CPM，删掉 csproj 内的版本号。

### 6.5 外部仓库写号

- I 可以写 `<安装根>/source/*` 与开发态同级仓库的**版本文件**；只允许改 `<Version>` 一行，
  写前校验「除版本号外内容不变」，否则停手并报告。
- PuppetCore 的版本在 `Puppet.Common.targets`，一次升号等于整族升号；ChatFlow 在 `ChatFlow.csproj`。
- 外部仓库写号同样进失败日志与回撤范围。

## 7. 构建前快照、提交与 push

- **快照（自动）**：沿用 `Snapshots.Tree` → `Snapshots.CommitTree` → `Snapshots.Pin(refs/openloop/version/<node>/<ver>)`；
  不碰真实索引、工作区与分支；manifest 记 `source.*`（§4）。
- **不做分支自动提交**：`git add -A && git commit` 会动共享索引、把临时文件写进永久历史，
  且撤销只能改写历史；tree/私有 ref 删除即可，风险为零。
- **人工触发接口**（I 侧，壳只做按钮/透传）：

```
GET  /loop/git/status                     → 托管仓库清单、脏文件、diff 统计、最近发布的版本/ref、token
POST /loop/git/commit  { repos, message?, push?, token }
```

约束：

1. 仓库白名单：OpenLoop 源根 + `ComponentRoot(...)`；不接受任意路径。
2. `token` = 预览时文件清单的哈希；提交时重算，不一致即拒绝（多 agent 共用一个工作副本）。
3. 按仓库逐个提交，只 `git add` 预览列出的路径；消息默认由版本日志生成，可覆盖。
4. `push` 只在显式传 true 时执行；`GIT_TERMINAL_PROMPT=0`；失败保留本地提交并原样报错；永不 force / 自动 rebase。
5. 最近一次发布或构建失败时默认拒绝提交（可显式强制）。
6. 壳侧的迭代窗口有两个按钮：「仅提交」和「提交并推送」。点击时先取一次预览（拿到 token 与文件数，
   写进进度框），再原样带回提交；推送失败时本地提交保留、错误原样显示。按钮点击本身就是显式强制，
   所以界面调用恒带 `force`，服务端那道护栏只拦自动调用。
7. 按钮可用性同样只有一处口径：计划里没有任何部件要升版/重编时「开始迭代」禁用（不再提供「重编一次做检查」）；
   工作树干净（三个托管仓库都没有改动）时两个 git 按钮禁用。窗口打开、每轮结束、每次提交后都会重问一次状态，
   I 不可达时按「没有改动」处理（提交本来也要走 I）。

## 8. 迭代入口迁到 I

- `IterationRunner.RunAsync` 的执行位置从壳挪到 I；壳的 `IterationForm` 保留为客户端（零 UI 改动），
  改为「向 I 提交 + 轮询进度」。
- 顺序不变（已在 `IterationRunner.RunAsync` 中成立）：
  `build → 发布壳 → 发布/stage/重启 A → 写 I 交接单 → A 换 I → 等结果 → 新 I 更新壳`。
- A 忙时 restart 返回 409：整条链停在原地等，不强行重启。
- 失败按**整条链**回退（现有 `RolesToRollback`/`RollbackAsync` 的快照语义），不只退单个角色。
- 壳两种产物：`PublishShellAsync` 按平台选 `OpenLoop.App`（Windows）/`OpenLoop.Shell`（其他）。

## 9. 壳自更新（去 PowerShell）

- 删除 `IterationRunner.RestartScript`、`Process.Start("powershell.exe")`、`PruneShell` 的备份分支与相关测试。
- 改为新壳自己当 helper：

```
旧壳: Process.Start(<新壳目录>/OpenLoop[.exe], "--apply-shell-update <新壳目录>") → 旧壳退出
新壳: 等旧壳进程退出 → 备份将被覆盖的文件 → 复制 → 写 started 标记 → 正常启动
      8 秒内没进界面：还原备份并启动旧壳
```

- 该子命令与 `--info` / `--write-manifest` 一样走 `HeadlessHost.TryManage`（A/I）或壳自己的参数解析，逻辑全在 C#、可单测、跨平台。
- 壳进入 `current.json`（`shell` 键 + `previous.json`）留作后续可选步骤，本轮不做。

## 10. 平台脚本清除清单

| 对象 | 处置 |
|---|---|
| `scripts/publish-role.ps1` | 删除（能力 = `VersionBump` + `Iteration.PublishAsync` + `/bus/stage`） |
| `scripts/publish-linux.ps1` | 删除；布局改为 MSBuild target 或文档中的 `dotnet publish` 命令 |
| `IterationRunner.RestartScript` + `powershell.exe` | 删除，换 `--apply-shell-update` |
| 文档中的引用 | `CHARTER.md`、`docs/IMPLEMENTATION.md:534`、`docs/PLAN-DUAL-PROCESS.md:590,1303-1314` 同步修改 |

## 11. 实施顺序与验收

| 阶段 | 内容 | 验收 |
|---|---|---|
| P0 | CPM + `IsPackable` + Core/Service `<Version>` + 删 App 的 ChatFlow 包引用 + ChatFlow 侧改 FCTB 方式 | `dotnet build OpenLoop.slnx`；`dotnet test` 两个测试工程全绿；包版本冲突/模式混用能在构建期报错 |
| P1 | 指纹 + `state.json` + `PlanAsync` 改指纹驱动（7 节点级联） | 单测：只改 Core → 五个节点升、chatflow/puppetcore 不动；改 ChatFlow → 只有 shell 升；首次无 state → 全量升一次 |
| P2 | I 接管迭代入口；删 `publish-role.ps1` | Windows 真机：迭代一次 → A/I 版本目录与 `current.json` 与 manifest 的 deps 一致；WSL：真实二进制发布/回退 |
| P3 | `/loop/git/status`、`/loop/git/commit` | 预览 → 提交 → push 全链路；token 过期被拒；无凭据时失败但不丢本地提交 |
| P4 | `--apply-shell-update` 替掉 PowerShell | Windows：换壳成功 + 新壳 8 秒内崩 → 自动还原；Linux：同一路径可用 |
| P5 | 文档与旧路径清理 | 全仓 `rg "publish-role|powershell|\.ps1"` 只剩无关项 |

每个阶段结束：`dotnet build OpenLoop.slnx`（Release 当作类型检查）+ 两个测试工程 + 至少一条真实二进制路径。

### 11.1 实施进展（2026-11-11）

| 阶段 | 状态 | 落地内容与实测 |
|---|---|---|
| P0 | 完成 | `Directory.Packages.props`（CPM）；Core/Service 补 `<Version>` + `IsPackable=false`；App 删掉 ChatFlow 包回退（只留源码引用）；ChatFlow 只消费 `Vax-FCTB`；CDS/AntdUI 收进 CPM。实测：`dotnet build` 0 错误，Core 227 通过 / Service 156 通过；FCTB 解析为 `2.19.0.225`（程序集 2.19.0.225） |
| P1 | 完成 | `VersionScan`（指纹 + 拓扑）、`VersionState`（`state.json` / `state.pending.json`）、`Decide`（冷启动全量升、级联、人工改号、失败重试同号）全部落地；`VersionBump` 扩到 7 节点；壳号单一来源 `ShellVersion.props`。实测：首次全量升一次 → 发布成功后再次计划全部「不变」 |
| P2 | 完成 | `IterationService` + `/api/loop/iteration[/plan|/stop]`；`IterationForm` 与 Puppet 入口改为「要计划 → 提交 → 轮询」；`publish-role.ps1`、`publish-linux.ps1` 删除；`PublishShellAsync` 按平台选项目。实测（Windows 真机）：整轮成功，A 0.1.4 / I 0.1.6 / 壳 0.1.6 换版，`current.json` 与 `manifest.json` 的目录哈希一致，`state.json` 已提升 |
| P3 | 完成 | `GitService` + `GET /api/loop/git/status`、`POST /api/loop/git/commit`：仓库白名单、按文件的 token 校验、按仓库逐个提交、显式 push、不 force。实测：`status` 返回三个仓库的清单与 token；token 过期被拒；白名单外的路径被拒（单测 + 真机 API） |
| P4 | 完成 | `ShellUpdate`（`--apply-shell-update`）+ `LaunchRestarter` 重写；`RestartScript` 与 PowerShell 调用删除。实测：`iteration-restart.log` 记录 `restart ok`，安装目录直接升到 0.1.8，临时目录不再产生 `.ps1` |
| P5 | 完成 | `docs/IMPLEMENTATION.md` 的更新通道说明改为 I 的迭代入口；`docs/PLAN-DUAL-PROCESS.md` 里两处旧脚本段落标注「已删除/历史」 |

实施中确定的几处实现细节（与上文设计的差异）：

1. **换 I 那一步会杀掉执行者**：迭代在 I 里跑，A 换 I 时本进程被杀。因此迭代状态落 `runtime/iteration.json`，
   新 I 启动后接手等待 `loop-update-result.json` 并补结论；壳的发布目录在进度里即时落盘，否则重启壳的意图会丢。
2. **I 不能被 `bus.Busy` 挡住**：迭代接管后 `Busy` 只看 `LoopService`，否则 A 请求重启 I 会被 409 卡死。
3. **指纹不能用裸 tree**：版本文件就在被哈希的目录里，写号会让指纹变化、下一轮再升一版（无限跳号）。
   现在对版本文件做「`<Version>` 行中性化」后参与哈希（`VersionScan.VersionNeutralHash`，有单测）。
4. **版本目录名用整份产物哈希**：exe（apphost）在同一版本下字节不变，只改库会让新产物覆盖正在运行的目录（文件锁）。
5. **Puppet 在包模式下不是版本节点**：消费方式由 `project.assets.json` 的 `type` 判定；走包时它的版本已在
   消费者的包闭包里，不再单独写号（否则源码号与包版本会脱节）。ChatFlow 走源码，所以是节点。
6. **Puppet 已切源码引用**（2026-11-11）：`Puppet.Core` / `Puppet.Core.WinForms` 改为同级 `<repo>/PuppetCore`
   的 ProjectReference，`Directory.Packages.props` 里对应的包版本声明已删。它现在是一个真正的节点
   （版本文件 `Puppet.Common.targets`），首次进入计划会升一次号并级联到 Service → A/I/壳；切回包模式只需把
   引用换回 `PackageReference`（节点会自动消失）。

## 12. 已实现并已验证（不计入本计划）

- 产物清单：`--write-manifest` 由产物自己写 `manifest.json`（协议号 + 目录哈希），`/bus/stage` 校验
  协议号、角色、目录哈希；无清单的旧包按老规则放行（`VersionManifest.cs`、`BusHandler.Stage`）。
- `current.json` 损坏时用 `.bak` 兜底、坏文件留档；`previous.json` 记录上一好版本；崩溃循环 3 次自动回退
  （`VersionStore.cs`、`Supervisor.cs`）。
- WSL 真机验证过：清单校验（协议不符/被篡改/无清单三态）、崩循环回退到上一版本。
- Windows 真机验证过：发布 → stage → restart → `current.json` / `previous.json` 落账、health 一致。

## 13. 风险与未验证项

1. `Puppet.Core` 在 Linux 上能否直接源码编译（只看过 csproj，未在 Linux 实编）。
2. Windows 下「新壳等旧壳退出再复制」的文件锁释放时延：已实测到旧壳退出后仍有短暂占用，复制改为按文件重试 30 秒；
   仍极端的情况下会走「还原并起旧壳」。
3. 外部仓库写号与用户/其他 agent 同时编辑的冲突（只设计了「除版本号外内容不变」的前置校验）。
4. 部署态没有组件源码时无法构建——发行包必须带 `<安装根>/source/*` 克隆或各组件提交号清单。
5. `state.json` 首次缺失导致全量升号（已接受，但会让第一次发布的四个版本同时跳号）。
6. ChatFlow 改包依赖后，壳内的 FCTB 版本从「源码 1.0.0.0」变为「包 2.19.x」；强签名下这是一次性的
   程序集身份变化，需在真机确认 ChatFlow 渲染与 `FilePreviewView` 均正常。
7. `/api/loop/git/commit` 的 `push` 只做过代码路径与 `GIT_TERMINAL_PROMPT=0` 检查，没在没有凭据的机器上
   实推；提交本身依赖本机 git 身份（没有 `user.name/email` 时 commit 会失败并原样报错）。
8. Puppet 切源码引用后，发行包必须带 `PuppetCore` 源码（或改回包引用）；`Puppet.Core.Wpf` 不参与构建。
