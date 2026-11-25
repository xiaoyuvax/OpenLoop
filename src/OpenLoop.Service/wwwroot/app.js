// OpenLoop 浏览器壳：React + Ant Design 5（vendor 本地引入）。
// 布局对齐 WinForms：顶栏（菜单/标题/设置）→ 卡片标签页（会话 + 每个会话）→
// 会话页 = 头部（标题 + 选择/操作/上下文/侧栏）+ 聊天|侧栏 + 输入区（输入框 + 工具条）。
const { createElement: h, useEffect, useMemo, useRef, useState } = React;
const {
  ConfigProvider, Dropdown, Tabs, Button, Input, Select, Space, Typography, Tag,
  Progress, Modal, Switch, Segmented, Tooltip, Empty, Popconfirm, Table, theme, message
} = antd;
const { Text } = Typography;
// 界面语言：settings.language（"" 跟随系统；只有系统语言是英文才显示英文，与 WinForms 一致）。
// 中文是源语言，直接写在代码里；英文查 EN 表，查不到就原样显示。
let LANG = 'zh';
function applyLanguage(value) {
  LANG = String(value || navigator.language || '').toLowerCase().startsWith('en') ? 'en' : 'zh';
  document.documentElement.lang = LANG === 'en' ? 'en' : 'zh-CN';
  return LANG;
}

// 带 ${} 插值的句子：整句进表，用 {name} 占位，避免把一句话拆成几段各翻各的。
const TPL = {
  '已压缩上下文：{before} → {after} tokens': 'Context compacted: {before} → {after} tokens',
  '{command} 是 WinForms 壳的命令，Web 壳暂不支持；请在 WinForms 壳里用': '{command} is a WinForms shell command, not supported in the web shell; use it in the WinForms shell',
  '已选 {n}/8': 'Selected {n}/8',
  '移除附件「{name}」？': 'Remove attachment "{name}"?',
  'v{version} · {features} 特征 · {rules} 规则': 'v{version} · {features} features · {rules} rules',
  '题目 {n}': '{n} tasks',
  '候选 {cp}/{n}，基线 {bp}/{n}': 'candidate {cp}/{n}, baseline {bp}/{n}',
  '删除提供商「{name}」？': 'Delete provider "{name}"?',
  '拉取到 {n} 个模型': 'Fetched {n} models',
  '确定卸载技能「{name}」？\n\n会删除整个目录，不可恢复：\n{dir}': 'Uninstall skill "{name}"?\n\nThis deletes the whole directory and cannot be undone:\n{dir}',
  '卸载{where}插件 {id}？\n\n整个目录会移进 OpenLoop 的插件回收目录，需要时能捞回来。\n{dir}': 'Uninstall the {where} plugin {id}?\n\nThe whole directory moves into OpenLoop\'s plugin recycle folder and can be restored.\n{dir}',
  '清单声明了 {n} 个模型，这里不改 models 与工具/MCP 字段。': 'The manifest declares {n} models; models and tool/MCP fields are not changed here.'
};
function tf(fmt, vars) {
  let s = LANG === 'en' ? (TPL[fmt] ?? fmt) : fmt;
  for (const k in vars) s = s.split('{' + k + '}').join(String(vars[k]));
  return s;
}

const EN = {
  "已允许 Puppet 截图": "Puppet screenshots allowed",
  "已关闭 Puppet 截图": "Puppet screenshots off",
  "允许 Puppet 截图": "Allow Puppet screenshots",
  "一个可自我迭代的开放式Agent": "A self-iterating open agent",
  "设置": "Settings",
  "项目": "Project",
  "（空工作区）": "(empty workspace)",
  "新建会话": "New session",
  "搜索会话": "Search sessions",
  "删除选中的 ": "Delete selected ",
  " 个会话？": " sessions?",
  "对话历史会一起删除，不可恢复。": "Chat history is deleted with them. This cannot be undone.",
  "删除": "Delete",
  "取消": "Cancel",
  "删除选中": "Delete selected",
  "全部项目": "All projects",
  "暂无会话": "No sessions",
  "会话": "Sessions",
  "状态": "Status",
  "工作区": "Workspace",
  "历史": "History",
  "会话私信": "Session DM",
  "Puppet 动作": "Puppet action",
  "协作提示": "Collab notice",
  "输出中…": "Streaming…",
  "思考": "Reasoning",
  "最近使用": "Recent",
  "全部模型": "All models",
  "附件最多 8 个": "At most 8 attachments",
  "请先停止当前任务，再压缩上下文": "Stop the current task before compacting context",
  "正在压缩上下文，最长约 2 分钟…": "Compacting context, up to ~2 minutes…",
  "没有可压缩的上下文": "Nothing to compact",
  "压缩失败：": "Compaction failed: ",
  "继续": "Continue",
  "发送失败：": "Send failed: ",
  "输入框为空": "The composer is empty",
  "停止失败：": "Stop failed: ",
  "已插队，本轮收尾后立刻发送": "Jumped the queue: sends right after this turn wraps up",
  "已入队，本轮结束后发送": "Queued: sends when this turn ends",
  "失败：": "Failed: ",
  "已切换模型": "Model switched",
  "切换失败：": "Switch failed: ",
  "选择": "Select",
  "删除这个会话？": "Delete this session?",
  "停止当前轮": "Stop this turn",
  "打断（下一 step 前收尾）": "Interrupt (wrap up before the next step)",
  "压缩上下文": "Compact context",
  "复制工作区": "Copy workspace path",
  "已复制工作区": "Workspace path copied",
  "已执行": "Done",
  "操作 ▾": "Actions ▾",
  "上下文占用：": "Context usage: ",
  "暂无对话": "No messages",
  "：": ": ",
  "已允许": "Allowed",
  "允许一次": "Allow once",
  "已允许同类": "Allowed for this kind",
  "同类都允许": "Allow this kind",
  "已拒绝": "Denied",
  "拒绝": "Deny",
  "排队": "Queued",
  "自动：agent 插入（立即发送）": "Auto: inserted by the agent (sent immediately)",
  "立即": "Now",
  "撤回": "Retract",
  "全部提供商": "All providers",
  "模型": "Model",
  "生图模型": "Image model",
  "只看免费": "Free only",
  "努力 Auto": "Effort Auto",
  "发送": "Send",
  "停止": "Stop",
  "上传或粘贴文本/图片附件（最多 8 个）": "Upload or paste text/image attachments (up to 8)",
  "📎 附件": "📎 Attach",
  "添加附件…": "Add attachment…",
  "移除附件": "Remove attachment",
  "移除": "Remove",
  "可上传或粘贴文本/图片": "Text or image files",
  "描述任务（Enter=本步后发送，Ctrl+Enter=本轮后发送，Shift+Enter=软换行）": "Describe the task (Enter=send after this step, Ctrl+Enter=send after this turn, Shift+Enter=soft newline)",
  "待答 ": "Surveys ",
  "考核点: —": "Eval: —",
  "版本: ": "Version: ",
  " 特征 · ": " features · ",
  " 规则": " rules",
  "努力": "Effort",
  "上下文": "Context",
  "成本": "Cost",
  "读不到清单：": "Cannot read the list: ",
  "Puppet Action 清单 · 实例 ": "Puppet actions · instance ",
  " 个 action": " actions",
  "Puppet Action 清单 · 读取中…": "Puppet actions · loading…",
  "筛选 action / 参数 / 说明": "Filter by action / params / description",
  "刷新": "Refresh",
  "读取中…": "Loading…",
  "没有可用的 action": "No actions available",
  "参数": "Params",
  "说明": "Description",
  "壳启用 Puppet（B 面）后才能列出 action；清单来自壳的 /appagent/manifest。": "Actions are listed once the shell enables Puppet (B face); the list comes from the shell's /appagent/manifest.",
  "双击一行把这条命令填进输入框：/puppet <action> <参数…>（参数用自然语言写，模型自己对应）": "Double-click a row to put the command into the composer: /puppet <action> <params…> (write params in natural language; the model maps them)",
  "详情": "Detail",
  "旁路": "Side work",
  "旁路未接入浏览器": "Side work is not wired into the browser",
  "先填工作目录": "Set the working directory first",
  "账本未初始化（先跑一次或新建会话）": "Ledger not initialized (run once or create a session)",
  "读账本失败：": "Cannot read the ledger: ",
  "已采纳 v": "Adopted v",
  "成功": "OK",
  "已回滚": "Rolled back",
  "绝对路径": "Absolute path",
  "演进": "Evolve",
  "Harness 管当前工作区的五类对象：Prompt（系统提示词模板 + AGENTS.md）、Skill、Rule、Limit、Guard（含连接重试、免费额度恢复等自愈参数）；": "The Harness page manages this workspace's five object kinds: Prompt (system-prompt template + AGENTS.md), Skill, Rule, Limit and Guard (the guards carry connect retry, free-quota recovery and other self-healing parameters);",
  "改提示词与 AGENTS.md 在桌面壳里用 /sysprompt、/agentsmd 让模型改，用户不直接编辑文本。": "to change the system prompt or AGENTS.md, use /sysprompt or /agentsmd in the desktop shell and let the model edit it — you never edit the text yourself.",
  "演进是攒够证据后的一次集中分诊：harness 类出候选并过考核题，源码类写派单，未知留到下一批。": "Evolution is one batched triage once enough evidence accumulated: harness items become candidates judged by the eval tasks, source items become a work order, unknown items wait for the next batch.",
  "观察用 /report <内容> 记入 findings，只落账、不立即处理。": "Record an observation with /report <text>; it only lands in findings and is not handled immediately.",
  "模型（留空用默认）": "Model (blank = default)",
  "演进启动失败：": "Evolve failed to start: ",
  "自动演进已开启": "Auto-evolve on",
  "自动演进已关闭": "Auto-evolve off",
  "设置失败：": "Setting failed: ",
  "自动演进": "Auto-evolve",
  "规则与回滚": "Rules and rollback",
  "规则 id，如 rule.style": "Rule id, e.g. rule.style",
  "规则内容": "Rule content",
  "工作目录和规则 id 都要填": "Both the working directory and the rule id are required",
  "已登记 ": "Registered ",
  "登记失败：": "Register failed: ",
  "登记规则": "Register rule",
  "回滚到上一版？": "Roll back to the previous version?",
  "会写成新版本，旧版本文件不动。": "This writes a new version; older version files stay untouched.",
  "回滚": "Roll back",
  "已回滚，当前 v": "Rolled back, now v",
  "回滚失败：": "Rollback failed: ",
  "回滚上一版": "Roll back one version",
  "迭代": "Iterate",
  "迭代启动失败：": "Iteration failed to start: ",
  "开始迭代": "Start iteration",
  "（跳过）": " (skipped)",
  "生成计划失败：": "Plan generation failed: ",
  "生成计划": "Generate plan",
  "连壳": "Shell",
  "形态": "Form",
  "框架依赖（需装 .NET）": "Framework-dependent (needs .NET)",
  "自带框架": "Self-contained",
  "账本": "Ledger",
  "技能": "Skills",
  " 个启用": " enabled",
  "（无）": "(none)",
  "svg 渲染失败": "svg render failed",
  "截图区域为空": "The screenshot area is empty",
  "读取设置失败：": "Cannot read settings: ",
  "读取技能失败：": "Cannot read skills: ",
  "读取插件失败：": "Cannot read plugins: ",
  "完全许可会关闭路径边界、命令审批与环境变量隔离，子进程可能拿到密钥，也可以直接访问网络与任意路径。确定启用？": "Full access turns off path boundaries, command approval and env isolation: child processes may see keys and can reach the network and any path. Enable it?",
  "已保存": "Saved",
  "保存失败：": "Save failed: ",
  "正在拉取模型…": "Fetching models…",
  "拉取失败": "Fetch failed",
  "拉取失败：": "Fetch failed: ",
  "先拉取或添加一个模型": "Fetch or add a model first",
  "正在测试连接…": "Testing connection…",
  "连接正常：": "Connected: ",
  "连接失败": "Connection failed",
  "连接失败：": "Connection failed: ",
  "已卸载 ": "Uninstalled ",
  "卸载失败：": "Uninstall failed: ",
  "先填技能目录的绝对路径": "Enter the absolute path of the skill directory first",
  "已安装到 ": "Installed to ",
  "安装失败：": "Install failed: ",
  "插件 id 不能为空": "Plugin id is required",
  "已保存，会话里的提供商下拉重开一次就能看到新模型": "Saved. Reopen the provider dropdown in a session to see the new models",
  "全局": "Global",
  "当前工作区": "This workspace",
  "正在读取设置…": "Loading settings…",
  "语言": "Language",
  "跟随系统": "Follow system",
  "中文": "中文",
  "主题色": "Theme",
  "浅色": "Light",
  "深色": "Dark",
  "灰蓝": "Gray blue",
  "完全许可": "Full access",
  "关闭路径边界、命令审批与环境隔离，可直连网络": "Turns off path boundaries, command approval and env isolation; direct network access",
  "自动审批": "Auto-approve",
  "一切工具调用都无需审批（含工作区外访问与风险操作）": "No approval for any tool call (including outside-workspace access and risky operations)",
  "把全局与项目 AGENTS.md 注入系统提示词": "Inject global and project AGENTS.md into the system prompt",
  "思考过程": "Reasoning",
  "新回话默认展开「思考与工具过程」": "New turns expand \"reasoning and tool steps\" by default",
  "改行为版本：护栏、阈值、规则与技能顺序": "Changes the behavior version: guardrails, thresholds, rules and skill order",
  "演进的对象是 Agent 的行为版本：只改当前工作区的护栏、阈值、规则与技能顺序（.openloop 激活集），": "Evolution targets the agent's behavior version: only this workspace's guardrails, thresholds, rules and skill order (the .openloop activation set);",
  "不编译、不换二进制；攒够新证据就自动跑一轮 A/B 考核，候选更好才采纳。": "no compile, no binary swap; with enough new evidence an A/B eval runs and the candidate is adopted only if better.",
  "自动迭代": "Auto-iterate",
  "改程序版本：工作树源码构建升版，换掉 A/I": "Changes the program version: build the worktree sources, bump and swap A/I",
  "迭代的对象是 OpenLoop 程序本身：把工作树里的源码改动构建、升版并替换 A/I（不连壳，壳仍由 Loop 页「迭代」换）。": "Iteration targets OpenLoop itself: build the source changes in the worktree, bump and replace A/I (the shell is not included; the shell is still swapped by \"Iterate\" on the Loop page).",
  "演进采纳后自动跑一次，会重启 A、中断正在跑的会话；工作树有未提交改动时跳过。": "Runs once right after an evolution is adopted; it restarts A and interrupts running sessions, and is skipped while the worktree has uncommitted changes.",
  "主题与语言在重启 OpenLoop 后生效；完全许可、自动审批、自动演进与自动迭代立即生效。": "Theme and language apply after restarting OpenLoop; full access, auto-approve, auto-evolve and auto-iterate apply immediately.",
  "启用协作": "Enable collab",
  "给 Worker 与 Loop 会话挂 CollabStart / CollabMonitor / ReadSession": "Gives Worker and Loop sessions CollabStart / CollabMonitor / ReadSession",
  "监测间隔": "Poll interval",
  "10 秒": "10 s",
  "30 秒": "30 s",
  "1 分钟": "1 min",
  "2 分钟": "2 min",
  "5 分钟": "5 min",
  "沿用上下文": "Reuse context",
  "协作会话带上发起方已完成的对话，复用前缀缓存（同模型才生效）": "Collab sessions carry the initiator's finished turns to reuse the prefix cache (same model only)",
  "协作会话是平级的独立会话，不是子 agent：它们不共享发起方的工具调用栈，只在同一工作区各干一段。": "Collab sessions are peer sessions, not sub-agents: they share no tool-call stack with the initiator and each works a slice of the same workspace.",
  "发起方启动后不再自己动手，只做监测与协调；监测汇报以仿用户消息进入它的队列（队列里用特殊底色标出），": "Once started the initiator stops doing the work itself and only monitors and coordinates; progress reports enter its queue as user-like messages (highlighted in the queue),",
  "全部会话结束或不可恢复地中止后自动停止。暂不支持跨工作区协作。": "and stop automatically once every session ends or aborts unrecoverably. Cross-workspace collaboration is not supported yet.",
  "权限审批": "Approval",
  "委派给 OpenCode 时，每个动作都经本应用审批": "Every action is approved here when delegated to OpenCode",
  "服务通过 ~/.local/state/opencode/service.json 自动发现；未就绪时不会出现在模型列表里。": "The service is discovered via ~/.local/state/opencode/service.json; it stays out of the model list until ready.",
  "免审批（默认）": "No approval (default)",
  "每次审批": "Approve every action",
  "关闭": "Close",
  "只读抓取 http/https 网页正文并转纯文本；不执行 JavaScript，内容按不可信数据处理。": "Read-only fetch of http/https page text as plain text; JavaScript is not executed and content is treated as untrusted data.",
  "内网与环回地址一律拒绝；只抓文本/HTML/JSON/XML，单页最多 2MB，结果默认 6000 字符，一轮最多 8 次。Worker 与 Loop 同时生效。": "Private-network and loopback addresses are refused; only text/HTML/JSON/XML up to 2 MB per page is fetched, 6000 characters by default, at most 8 per turn. Applies to Worker and Loop alike.",
  "（插件）": " (plugin)",
  "提供商 ": "Provider ",
  "提供商": "Provider",
  "还没有提供商": "No providers yet",
  "添加": "Add",
  "名称": "Name",
  "接口地址": "Base URL",
  "密钥": "API key",
  "由插件清单管理": "Managed by the plugin manifest",
  "已保存（留空表示不改）": "Saved (blank keeps it unchanged)",
  "粘贴 API Key": "Paste API key",
  "拉取模型": "Fetch models",
  "测试": "Test",
  "插件提供商：模型的增删写回它的 plugin.json": "Plugin provider: model changes are written back to its plugin.json",
  "插件提供商当前不可用（服务未就绪），可以先停用": "This plugin provider is unavailable (service not ready); you can disable it",
  "模型 id，回车加入": "Model id, Enter to add",
  "加入": "Add",
  "这个提供商还没有模型": "This provider has no models yet",
  "取消勾选=停用（不进出模型下拉）": "Unchecking disables it (kept out of the model dropdown)",
  "属性": "Properties",
  "「拉取模型」会请求提供商的 /models；「测试」会真的发一次最小补全。属性可设显示名称与上下文上限。": "\"Fetch models\" requests the provider's /models; \"Test\" really sends one minimal completion. Properties set the display name and context limit.",
  "插件贡献的提供商标着「（插件）」，模型增删写回插件清单，停用状态存在 settings.json。": "Providers contributed by plugins are marked \"(plugin)\"; model changes are written back to the plugin manifest and disabled state lives in settings.json.",
  "范围": "Scope",
  "先在项目里打开一个会话，或切到「全局」管理全局技能": "Open a session in a project first, or switch to \"Global\" to manage global skills",
  "安装": "Install",
  "含 SKILL.md 的目录（绝对路径）": "Directory containing SKILL.md (absolute path)",
  "没有可管理的技能": "No skills to manage",
  "项目 .opencode": "Project .opencode",
  "卸载": "Uninstall",
  "项目技能停用/启用走账本（由 I 收敛）；全局技能只改本机开关。.opencode/skills 里的技能由 opencode 管理，这里只能看。": "Enabling/disabling project skills goes through the ledger (converged by I); global skills only flip a local switch. Skills in .opencode/skills are managed by opencode and are read-only here.",
  "存放位置": "Locations",
  "工作区 ": "Workspace ",
  "（还没打开工作区，只看全局）": "(no workspace open yet; global only)",
  "全局 ": "Global ",
  "还没有插件": "No plugins yet",
  "编辑": "Edit",
  "存盘即生效，会话里的提供商下拉几秒内自己补上。「编辑」只改 provider 型插件的地址与密钥，": "Saved changes apply at once and the provider dropdown picks them up within seconds. \"Edit\" only changes the URL and key of provider plugins;",
  "工具/MCP 型插件的清单自己去目录里改；「卸载」把整个目录移进回收目录，可捞回。": "change the manifest of tool/MCP plugins in their directory; \"Uninstall\" moves the whole directory to a recycle folder and can be restored.",
  "同名插件工作区优先，全局新插件点「添加」时写进当前工作区。": "Workspace plugins win over same-name global ones; \"Add\" writes new plugins into the current workspace.",
  "普通": "Plain",
  "工具": "Tool",
  "插件": "Plugin",
  "协作": "Collab",
  "保存": "Save",
  "编辑插件": "Edit plugin",
  "添加插件": "Add plugin",
  "会写进当前工作区的插件目录（没有工作区就写全局）": "Writes into the current workspace's plugin directory (global when there is no workspace)",
  "插件 id（也是目录名）": "Plugin id (also the directory name)",
  "下拉里显示的名字，留空用 id": "Name shown in the dropdown; blank uses the id",
  "OpenAI 兼容根地址，例如 http://127.0.0.1:3065/v1": "OpenAI-compatible base URL, e.g. http://127.0.0.1:3065/v1",
  "可选：GET 这个地址取 data[].id 当模型清单": "Optional: GET this URL and use data[].id as the model list",
  "可选：界面上的模型前缀，发上游前会去掉，例如 a2a/": "Optional: model prefix shown in the UI, stripped before sending upstream, e.g. a2a/",
  "写入同目录 key.txt；留空表示不改": "Written to key.txt in the same directory; blank keeps it unchanged",
  "写入同目录 key.txt，可留空": "Written to key.txt in the same directory; can be blank",
  "只建 provider 贡献，模型清单靠 ModelsUrl 自动发现。": "Only the provider contribution is created; the model list comes from ModelsUrl discovery.",
  "模型属性": "Model properties",
  "确定": "OK",
  "显示名称（留空沿用模型 id）": "Display name (blank keeps the model id)",
  "上下文上限（token，0 表示自动）": "Context limit (tokens, 0 = auto)",
  "拿不到能力位：": "Cannot read capabilities: ",
  "会话已删除：": "Session deleted: ",
  "已重连 ": "Reconnected ",
  "会话读取失败：": "Cannot read the session: ",
  "Puppet 截图失败：": "Puppet screenshot failed: ",
  "已删除会话 ": "Deleted session ",
  "删除失败：": "Delete failed: ",
  "新建失败：": "Create failed: ",
  "会话列表": "Session list",
  "关于": "About",
  "没有运行中的会话": "No running sessions",
  "会话列表还没加载完": "The session list is still loading",
  "打开所有活动会话": "Open all active sessions",
  "关闭所有非活动会话": "Close all inactive sessions",
  "关闭所有 Worker 会话": "Close all Worker sessions",
  "正在连接…": "Connecting…",
  "创建": "Create",
  "工作目录（绝对路径）": "Working directory (absolute path)",
  "模型，如 opencode/xxx（留空用默认）": "Model, e.g. opencode/xxx (blank = default)",
  "OpenLoop 浏览器壳": "OpenLoop browser shell",
  "角色：": "Roles: ",
  "默认工作区：": "Default workspace: ",
  "模型目录：": "Model catalog: ",
  " 个": "",
  "未就绪": "not ready",
  "会话：": "Sessions: ",
};
const t = s => (LANG === 'en' ? (EN[s] ?? s) : s);


async function api(path, options = {}) {
  const res = await fetch(path, { headers: { 'Content-Type': 'application/json' }, ...options });
  const text = await res.text();
  if (!res.ok) {
    let msg = text;
    try { const j = JSON.parse(text); msg = j.error || j.message || text; } catch { /* 非 JSON 错误体 */ }
    throw new Error(msg || ('HTTP ' + res.status));
  }
  return text ? JSON.parse(text) : null;
}
const post = (path, body) => api(path, { method: 'POST', body: JSON.stringify(body ?? {}) });

// 与 WinForms 的 SessionView.TryRunCommand 同一组命令：Web 只接自己能真正执行的，其余明确回绝，别把「/命令」当正文发给模型
const WEB_COMMANDS = ['/compact'];
const SHELL_COMMANDS = ['/btw', '/complain', '/fork', '/new', '/sysprompt', '/agentsmd', '/puppet', '/review', '/optimize'];
const commandOf = text => {
  const m = /^(\S+)(?:\s|$)/.exec(text || '');
  return m ? m[1].toLowerCase() : '';
};

function baseName(path) {
  const s = (path || '').replace(/[\\/]+$/, '');
  return s.split(/[\\/]/).pop() || s || '—';
}

const stateDot = state => ({
  running: '#1677ff', done: '#52c41a', interrupted: '#ff4d4f', idle: '#bfbfbf'
}[state] || '#bfbfbf');

const Dot = ({ state }) => h('span', {
  className: state === 'running' ? 'ol-dot-running' : undefined,
  style: {
    display: 'inline-block', width: 8, height: 8, borderRadius: 4,
    background: stateDot(state), marginRight: 6, flex: 'none'
  }
});

const Muted = ({ children, style }) => h(Text, { type: 'secondary', style: { fontSize: 12, ...style } }, children);

// Puppet 页面截图开关：只有人能勾（Puppet 自己关不掉），放「关于」里随时可达
const PuppetRow = ({ shot, setShot }) => h('div', { style: { display: 'flex', alignItems: 'center', gap: 8, marginTop: 12 } },
  h(Switch, {
    size: 'small', checked: shot,
    onChange: v => {
      setShot(v);
      localStorage.setItem('openloop.shot', v ? '1' : '0');
      message.success(v ? t('已允许 Puppet 截图') : t('已关闭 Puppet 截图'));
    }
  }),
  h(Muted, null, t('允许 Puppet 截图')));

// ---------------- 顶栏 ----------------

function TopBar({ menu, onSettings }) {
  return h('div', {
    style: {
      height: 54, flex: 'none', display: 'flex', alignItems: 'center', gap: 10,
      padding: '0 10px', background: '#fff', borderBottom: '1px solid #f0f0f0'
    }
  },
    h(Dropdown, { menu, trigger: ['click'] },
      h(Button, { type: 'text', style: { width: 38, height: 34, borderRadius: 8 } }, '☰')),
    h('div', { style: { display: 'flex', alignItems: 'baseline', gap: 10, minWidth: 0 } },
      h('span', { style: { fontSize: 17, fontWeight: 700 } }, 'OpenLoop'),
      h('span', { style: { fontSize: 12, color: '#8c8c8c' } }, t('一个可自我迭代的开放式Agent'))),
    h('div', { style: { flex: 1 } }),
    h(Tooltip, { title: t('设置') },
      h(Button, { type: 'text', style: { width: 38, height: 34, borderRadius: 8 }, onClick: onSettings }, '⚙')));
}

// ---------------- 会话列表（Tab0） ----------------

function OverviewPage({ sessions, project, setProject, onOpen, onNew, onDelete, onLoop, caps }) {
  const [q, setQ] = useState('');
  const [picked, setPicked] = useState([]);

  const projects = useMemo(() => {
    const map = new Map();
    for (const s of sessions) {
      const ws = s.workspace || '';
      const p = map.get(ws) || { ws, name: baseName(ws), count: 0 };
      p.count++;
      map.set(ws, p);
    }
    return [...map.values()].sort((a, b) => a.name.localeCompare(b.name));
  }, [sessions]);

  const rows = useMemo(() => {
    const key = q.trim().toLowerCase();
    return sessions.filter(s =>
      (project == null || (s.workspace || '') === project) &&
      (!key || (s.title || '').toLowerCase().includes(key) || (s.id || '').toLowerCase().includes(key)));
  }, [sessions, project, q]);

  const allPicked = rows.length > 0 && rows.every(r => picked.includes(r.id));
  const toggle = id => setPicked(p => p.includes(id) ? p.filter(x => x !== id) : [...p, id]);

  const removePicked = async () => {
    for (const id of picked) await onDelete(id);
    setPicked([]);
  };

  return h('div', { style: { display: 'flex', height: '100%', minHeight: 0 } },
    h('div', {
      style: {
        width: 272, flex: 'none', borderRight: '1px solid #f0f0f0', background: '#fff',
        display: 'flex', flexDirection: 'column', minHeight: 0
      }
    },
      h('div', { style: { padding: '10px 12px 6px', fontSize: 12, color: '#8c8c8c' } }, t('工作区')),
      h('div', { className: 'ol-scroll', style: { flex: 1, minHeight: 0, padding: '0 8px' } },
        projects.map(p => h('div', {
          key: p.ws || '∅',
          className: 'ol-item' + (project === p.ws ? ' active' : ''),
          onClick: () => setProject(p.ws),
          title: p.ws || t('（空工作区）')
        },
          h('span', { className: 'ol-avatar' }, (p.name || '?').slice(0, 1).toUpperCase()),
          h('span', { style: { flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } }, p.name),
          h(Muted, null, p.count)))),
      h('div', { style: { padding: 10, borderTop: '1px solid #f0f0f0', display: 'grid', gap: 8 } },
        h(Button, { block: true, onClick: onLoop }, 'Harness'),
        h(Button, { block: true, onClick: onNew }, t('新建会话')))),

    h('div', { style: { flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', minHeight: 0 } },
      h('div', { style: { display: 'flex', gap: 8, padding: '10px 16px' } },
        h(Input, {
          value: q, onChange: e => setQ(e.target.value), allowClear: true,
          placeholder: t('搜索会话'), style: { maxWidth: 480 }
        }),
        h('div', { style: { flex: 1 } }),
        picked.length
          ? h(Popconfirm, {
              title: t('删除选中的 ') + picked.length + t(' 个会话？'),
              description: t('对话历史会一起删除，不可恢复。'),
              okText: t('删除'), cancelText: t('取消'), okButtonProps: { danger: true },
              onConfirm: removePicked
            }, h(Button, { danger: true }, t('删除选中')))
          : h(Button, { disabled: true }, t('删除选中')),
        h(Button, { type: 'primary', onClick: onNew }, t('新建会话'))),
      h('div', { style: { padding: '0 16px 8px' } },
        h(Muted, null, project || t('全部项目'))),
      h('div', { className: 'ol-scroll', style: { flex: 1, minHeight: 0, padding: '0 16px 12px' } },
        rows.length === 0
          ? h(Empty, { image: Empty.PRESENTED_IMAGE_SIMPLE, description: t('暂无会话') })
          : h('div', { className: 'ol-table' },
            h('div', { className: 'ol-thead' },
              h('input', {
                type: 'checkbox', checked: allPicked,
                onChange: () => setPicked(allPicked ? [] : rows.map(r => r.id))
              }),
              h('span', { style: { flex: 1 } }, t('会话')),
              h('span', { style: { width: 90 } }, t('状态')),
              h('span', { style: { width: 220 } }, t('工作区'))),
            rows.map(s => h('div', {
              key: s.id, className: 'ol-trow', onClick: () => onOpen(s.id)
            },
              h('input', {
                type: 'checkbox', checked: picked.includes(s.id),
                onClick: e => e.stopPropagation(), onChange: () => toggle(s.id)
              }),
              h('span', { style: { flex: 1, minWidth: 0, display: 'flex', alignItems: 'center' } },
                h(Dot, { state: s.state }),
                h('span', { style: { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } },
                  s.title || baseName(s.workspace) || s.id),
                s.live === false ? h(Tag, { style: { marginLeft: 8 } }, t('历史')) : null),
              h('span', { style: { width: 90 } }, h(Muted, null, s.state || 'idle')),
              h('span', { style: { width: 220, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } },
                h(Muted, null, s.workspace || '—'))))))));
}

// ---------------- 会话页 ----------------

function Turn({ turn, bodyRef }) {
  // 角色靠左右位置区分（用户靠右、宽度封顶一半；助手贴左占满），不再显示「你 / OpenLoop」标注。
  return h('div', { className: 'ol-turn' },
    // 协作提示（agent 自动插入的仿用户消息）：折叠成一行标题 + 淡黄底，详情点开看
    turn.notice ? h('div', { className: 'ol-turn' },
      h('details', { className: 'ol-details ol-notice' },
        h('summary', null, (turn.noticeKind === 'mention' ? t('会话私信') : turn.noticeKind === 'puppet' ? t('Puppet 动作') : t('协作提示'))
          + ' · ' + (turn.user || '').split('\n')[0]),
        h('pre', null, turn.user || ''))) : (turn.user ? h('div', { className: 'ol-msg ol-msg-user' },
      h('div', { className: 'ol-body' }, turn.user)) : null),
    h('div', { className: 'ol-msg ol-msg-ai' + (turn.user ? ' ol-msg-next' : '') },
      h('div', { ref: bodyRef, className: 'ol-body' }, turn.assistant || ''),
      turn.streaming ? h('div', { className: 'ol-streaming' }, t('输出中…')) : null,
      turn.reasoning ? h('details', { className: 'ol-details' },
        h('summary', null, t('思考')),
        h('pre', null, turn.reasoning)) : null,
      (turn.timeline || turn.toolLines) ? h('pre', { className: 'ol-tools' }, turn.timeline || turn.toolLines) : null));
}

function SessionPage({ snapshot, models, recent, images, activeId, shot, setShot, onModel, onDelete, reloadModels }) {
  const [text, setText] = useState('');
  const [model, setModel] = useState('');
  const [effort, setEffort] = useState('');
  const [freeOnly, setFreeOnly] = useState(true);
  const [provider, setProvider] = useState('');
  const [sideOpen, setSideOpen] = useState(true);
  const [sideTab, setSideTab] = useState('detail');
  const [puppetSheet, setPuppetSheet] = useState(null);   // Puppet Action 清单：null=未读，{error} 或壳的 manifest
  const [puppetFilter, setPuppetFilter] = useState('');
  const [selectMode, setSelectMode] = useState(false);
  const [files, setFiles] = useState([]);
  const [imageModel, setImageModel] = useState('');
  const [imageOpen, setImageOpen] = useState(false);
  const chatRef = useRef(null);
  const answerRef = useRef(null);
  const prevRun = useRef(null);   // 上一次快照的会话 id 与是否运行中：用来识别「本轮刚正常收尾」
  const fileInput = useRef(null);

  useEffect(() => {
    setModel(snapshot?.model || '');
    setEffort(snapshot?.effort || '');
  }, [snapshot?.id, snapshot?.model, snapshot?.effort]);

  // 与 WinForms 侧栏「Puppet」页同一个数据源：壳的 /appagent/manifest（Web 侧经 /api/puppet/actions 代理）
  const loadPuppet = async () => {
    setPuppetSheet(prev => ({ ...(prev || {}), loading: true }));
    try { setPuppetSheet({ ...(await api('/api/puppet/actions')), at: new Date() }); }
    catch (e) { setPuppetSheet({ error: e.message }); }
  };
  useEffect(() => { if (sideOpen && sideTab === 'puppet') loadPuppet(); }, [sideTab, sideOpen]);

  useEffect(() => {
    const el = chatRef.current;
    if (!el) return;
    const nowRunning = snapshot?.state === 'running';
    const prev = prevRun.current;
    prevRun.current = { id: snapshot?.id, running: nowRunning };
    // 正常收尾：把最后一条正文的开头对齐视口顶部，长回答从头读（流式期间照旧跟随末尾）。
    // 正文不足一屏时浏览器把 scrollTop 钳到上限，仍停在末尾；与 WinForms 的 ScrollToAnswerStart 一致。
    if (prev && prev.id === snapshot?.id && prev.running && !nowRunning
        && snapshot?.outcome === 'ok' && answerRef.current) {
      const box = el.getBoundingClientRect();
      el.scrollTop = Math.max(0,
        answerRef.current.getBoundingClientRect().top - box.top + el.scrollTop - 12);
      return;
    }
    // 收尾之后不再自动跟随：否则下一次轮询又会把刚对齐到开头的正文拽回末尾
    if (!nowRunning) return;
    if (el.scrollTop + el.clientHeight >= el.scrollHeight - 160) el.scrollTop = el.scrollHeight;
  }, [snapshot]);

  const providers = useMemo(() => {
    const set = new Set();
    for (const m of models) {
      const slash = m.id.indexOf('/');
      if (slash > 0) set.add(m.id.slice(0, slash));
    }
    return [...set].sort();
  }, [models]);

  const choices = useMemo(() => {
    const imageIds = new Set((images || []).map(m => m.id));
    const list = models
      .filter(m => !imageIds.has(m.id))
      .filter(m => (!freeOnly || m.free) && (!provider || m.id.startsWith(provider + '/')))
      .map(m => ({ value: m.id, label: m.name }));
    // 与 WinForms 一致：最近用过的置顶，用分组标题隔开（各自带一行文字，比纯色块更清楚）
    const rank = new Map(recent.map((id, i) => [id, i]));
    const head = list.filter(o => rank.has(o.value)).sort((a, b) => rank.get(a.value) - rank.get(b.value));
    if (!head.length) return list;
    const tail = list.filter(o => !rank.has(o.value));
    return [
      { label: t('最近使用'), options: head },
      ...(tail.length ? [{ label: t('全部模型'), options: tail }] : [])
    ];
  }, [models, recent, freeOnly, provider, images]);

  const imageChoices = useMemo(() => (images || [])
    .filter(m => (!freeOnly || m.free) && (!provider || m.id.startsWith(provider + '/')))
    .map(m => ({ value: m.id, label: m.name || m.id })), [images, freeOnly, provider]);

  useEffect(() => {
    if (imageChoices.some(m => m.value === imageModel)) return;
    setImageModel(imageChoices[0]?.value || '');
  }, [imageChoices]);

  const running = snapshot?.state === 'running';
  const value = text.trim();
  const lastTurn = (snapshot?.turns || [])[(snapshot?.turns || []).length - 1];
  const retry = !running && !value && !!lastTurn && lastTurn.failed
    && /provider\.free-limit|\b429\b|\b402\b|rate limit|too many requests|quota|usage limit|insufficient|credits|payment required|额度|余额/i.test(lastTurn.assistant || '');
  const pct = snapshot?.contextLimit > 0 && snapshot?.contextTokens != null
    ? Math.min(100, Math.round(snapshot.contextTokens / snapshot.contextLimit * 100)) : 0;

  const pickFiles = async (e) => {
    const picked = Array.from(e.target.files || []);
    e.target.value = '';
    await acceptFiles(picked);
  };

  const acceptFiles = async (picked) => {
    const next = [];
    for (const file of picked) {
      if (files.length + next.length >= 8) { message.warning(t('附件最多 8 个')); break; }
      const data = await new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(String(reader.result || '').split(',')[1] || '');
        reader.onerror = reject;
        reader.readAsDataURL(file);
      });
      next.push({ name: file.name, data });
    }
    if (next.length) setFiles(list => [...list, ...next].slice(0, 8));
  };

  // 手动压缩上下文（/compact 与「操作 ▾ → 压缩上下文」同一条路）
  const compactContext = async () => {
    if (!activeId) return;
    if (running) return message.warning(t('请先停止当前任务，再压缩上下文'));
    const hide = message.loading(t('正在压缩上下文，最长约 2 分钟…'), 0);
    try {
      const r = await post(`/api/sessions/${activeId}/compact`, {});
      hide();
      if (r?.ok) message.success(tf('已压缩上下文：{before} → {after} tokens', { before: r.before, after: r.after }));
      else message.warning(r?.error || t('没有可压缩的上下文'));
    } catch (e) { hide(); message.error(t('压缩失败：') + e.message); }
  };

  const send = async (immediate = true) => {
    if (!activeId) return;
    // 命令不走普通对话：/compact 直接执行；WinForms 才有的命令回绝并保留原文，免得模型收到一句看不懂的「/xxx」
    const command = value.startsWith('/') ? commandOf(value) : '';
    if (WEB_COMMANDS.includes(command)) { setText(''); return compactContext(); }
    // /report：只落 findings（A 面同一落账入口），不启动会话、不触发演进
    if (command === '/report') {
      try {
        const r = await post(`/api/sessions/${activeId}/report`, { text: value.slice(7).trim() });
        setText('');
        if (r?.ok) message.success(r.message); else message.warning(r?.message || t('缺少内容'));
      } catch (e) { message.error(t('记录失败：') + e.message); }
      return;
    }
    if (SHELL_COMMANDS.includes(command)) {
      return message.warning(tf('{command} 是 WinForms 壳的命令，Web 壳暂不支持；请在 WinForms 壳里用', { command }));
    }
    if (!value && !files.length) {
      if (retry) {
        try { await post(`/api/sessions/${activeId}/messages`, { text: t('继续'), tag: 'task' }); }
        catch (e) { message.error(t('发送失败：') + e.message); }
        return;
      }
      if (!running) return message.warning(t('输入框为空'));
      try { await post(`/api/sessions/${activeId}/stop`); } catch (e) { message.error(t('停止失败：') + e.message); }
      return;
    }
    const attachments = files.map(f => ({ name: f.name, data: f.data }));
    setText('');
    setFiles([]);
    try {
      await post(`/api/sessions/${activeId}/messages`,
        { text: value, tag: 'task', immediate, imageModel, attachments });
      if (running) message.success(immediate ? t('已插队，本轮收尾后立刻发送') : t('已入队，本轮结束后发送'));
    } catch (e) { setText(value); setFiles(attachments); message.error(t('发送失败：') + e.message); }
  };

  const act = async (action, body, ok) => {
    if (!activeId) return;
    try { await post(`/api/sessions/${activeId}/${action}`, body); if (ok) message.success(ok); }
    catch (e) { message.error(t('失败：') + e.message); }
  };

  const pickModel = async (id, nextEffort) => {
    setModel(id);
    if (nextEffort !== undefined) setEffort(nextEffort);
    if (!activeId) return;
    try {
      await post(`/api/sessions/${activeId}/model`, { model: id, effort: nextEffort !== undefined ? nextEffort : effort });
      message.success(t('已切换模型'));
      if (reloadModels) reloadModels();   // 重拉一次，让「最近使用」跟上
    } catch (e) { message.error(t('切换失败：') + e.message); }
  };

  const header = h('div', {
    style: {
      height: 44, flex: 'none', display: 'flex', alignItems: 'center', gap: 10,
      padding: '0 16px', borderBottom: '1px solid #f0f0f0', background: '#fff'
    }
  },
    h(Dot, { state: snapshot?.state }),
    h('span', { style: { fontWeight: 600, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } },
      snapshot?.title || snapshot?.id || t('会话')),
    h('div', { style: { flex: 1 } }),
    h('label', { style: { display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: '#8c8c8c' } },
      h('input', { type: 'checkbox', checked: selectMode, onChange: e => setSelectMode(e.target.checked) }), t('选择')),
    selectMode
      ? h(Popconfirm, {
          title: t('删除这个会话？'), description: t('对话历史会一起删除，不可恢复。'),
          okText: t('删除'), cancelText: t('取消'), okButtonProps: { danger: true },
          onConfirm: () => onDelete(snapshot?.id)
        }, h(Button, { size: 'small', danger: true }, t('删除')))
      : h(Dropdown, {
          menu: {
            items: [
              { key: 'stop', label: t('停止当前轮'), disabled: !running },
              { key: 'interrupt', label: t('打断（下一 step 前收尾）'), disabled: !running },
              { key: 'resume', label: t('继续'), disabled: running },
              { key: 'compact', label: t('压缩上下文'), disabled: running },
              { type: 'divider' },
              { key: 'copy', label: t('复制工作区') }
            ],
            onClick: ({ key }) => {
              if (key === 'copy') {
                navigator.clipboard?.writeText(snapshot?.workspace || '');
                return message.success(t('已复制工作区'));
              }
              if (key === 'compact') { compactContext(); return; }
              act(key, null, t('已执行'));
            }
          }
        }, h(Button, { size: 'small', type: 'text' }, t('操作 ▾'))),
    h(Tooltip, { title: t('上下文占用：') + (snapshot?.contextTokens ?? '—') + ' / ' + (snapshot?.contextLimit || '—') },
      h(Progress, { type: 'circle', size: 26, percent: pct, showInfo: false, strokeWidth: 8 })),
    h(Muted, { style: { width: 34, textAlign: 'right' } }, pct + '%'),
    h(Button, { size: 'small', type: 'text', onClick: () => setSideOpen(v => !v) }, sideOpen ? '»' : '«'));

  const chat = h('div', { ref: chatRef, className: 'ol-scroll', style: { flex: 1, minHeight: 0, padding: '12px 20px' } },
    !snapshot
      ? h(Empty, { image: Empty.PRESENTED_IMAGE_SIMPLE, description: t('暂无对话') })
      : (snapshot.turns || []).length === 0
        ? h(Empty, { image: Empty.PRESENTED_IMAGE_SIMPLE, description: t('暂无对话') })
        : (snapshot.turns || []).map((t, i) => h(Turn, {
            key: i, turn: t,
            bodyRef: i === (snapshot.turns || []).length - 1 ? answerRef : undefined
          })),
    (snapshot?.prompts || []).map(p => h('div', { key: p.id, className: 'ol-prompt' },
      h('div', { style: { flex: 1, minWidth: 0 } },
        h('div', { style: { fontWeight: 600, whiteSpace: 'pre-wrap' } },
          (p.category ? '[' + p.category + '] ' : '') + (p.tool ? p.tool + t('：') : '') + (p.text || '')),
        p.reason ? h(Muted, null, p.reason) : null),
      h(Space, { size: 4, style: { flex: 'none' } },
        h(Button, { size: 'small', type: 'primary', onClick: () => act('approval', { id: p.id, decision: 1 }, t('已允许')) }, t('允许一次')),
        h(Button, { size: 'small', onClick: () => act('approval', { id: p.id, decision: 2 }, t('已允许同类')) }, t('同类都允许')),
        h(Button, { size: 'small', danger: true, onClick: () => act('approval', { id: p.id, decision: 0 }, t('已拒绝')) }, t('拒绝'))))));

  const pending = (snapshot?.pending || []).length
    ? h('div', { className: 'ol-pending' },
        h(Muted, { style: { flex: 'none' } }, t('排队')),
        (snapshot.pending || []).map((item, i) => {
          // agent 自动插入的（协作监测汇报）用特殊底色标出，用户敲的仍是普通底色
          const auto = (snapshot.pendingAuto || [])[i] === true
          return h('div', { key: i, className: 'ol-pending-row' + (auto ? ' ol-pending-auto' : '') },
            auto ? h('span', { className: 'ol-pending-icon', title: t('自动：agent 插入（立即发送）') }, '🤖') : null,
            h('span', { title: item, style: { flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } }, item),
            auto ? null : h(Button, { size: 'small', type: 'link', onClick: () => act('pending', { index: i, action: 'now' }) }, t('立即')),
            h(Button, { size: 'small', type: 'link', danger: true, onClick: () => act('pending', { index: i, action: 'remove' }) }, t('撤回')))
        }))
    : null;

  const toolbar = h('div', { style: { display: 'flex', alignItems: 'center', gap: 8, marginTop: 6, flexWrap: 'nowrap', minWidth: 0 } },
    h('div', { style: { flex: 1, minWidth: 8 } }),
    h(Muted, { style: { flex: '0 1 auto', minWidth: 0, maxWidth: 220, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } },
      '📁 ' + (snapshot?.workspace || '—')),
    providers.length
      ? h(Select, {
          value: provider || '', onChange: setProvider, style: { width: 140 },
          options: [{ value: '', label: t('全部提供商') }, ...providers.map(p => ({ value: p, label: p }))]
        }) : null,
    models.length
      ? h(Select, {
          value: model || undefined, onChange: id => pickModel(id), style: { width: 280 },
          showSearch: true, optionFilterProp: 'label', placeholder: t('模型'),
          options: choices.length ? choices : [{ value: model, label: model }].filter(o => o.value)
        })
      : h(Input, { value: model, onChange: e => setModel(e.target.value), onBlur: () => pickModel(model), placeholder: t('模型'), style: { width: 280 } }),
    imageChoices.length
      ? h('span', { style: { display: 'flex', alignItems: 'center', gap: 6 } },
          h(Button, {
            size: 'small', title: t('生图模型'), type: imageOpen ? 'primary' : 'default',
            onClick: () => setImageOpen(v => !v)
          }, '🖼'),
          imageOpen ? h(Select, {
            value: imageModel || undefined, onChange: setImageModel, style: { width: 200 },
            showSearch: true, optionFilterProp: 'label', placeholder: t('生图模型'),
            options: imageChoices
          }) : null)
      : null,
    h('span', { style: { display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: '#8c8c8c' } },
      t('只看免费'),
      h(Switch, { size: 'small', checked: freeOnly, onChange: setFreeOnly })),
    h(Select, {
      value: effort || '', onChange: v => pickModel(model, v), style: { width: 110 },
      options: [{ value: '', label: t('努力 Auto') }, { value: 'low', label: 'Low' },
        { value: 'medium', label: 'Medium' }, { value: 'high', label: 'High' }]
    }),
    h(Button, {
      type: 'primary', style: { width: 96 },
      danger: running && !value, onClick: () => send(true)
    }, running ? (value ? t('发送') : t('停止')) : retry ? t('继续') : t('发送')));

  const attachMenu = {
    items: [
      { key: 'add', label: t('添加附件…') },
      ...files.map((file, i) => ({ key: 'f' + i, label: '✕ ' + file.name }))
    ],
    onClick: ({ key }) => {
      if (key === 'add') { fileInput.current?.click(); return; }
      const i = Number(key.slice(1));
      const file = files[i];
      if (!file) return;
      Modal.confirm({
        title: t('移除附件'),
        content: tf('移除附件「{name}」？', { name: file.name }),
        okText: t('移除'), cancelText: t('取消'),
        onOk: () => setFiles(list => list.filter((_, j) => j !== i))
      });
    }
  };
  const attachRow = h('div', { style: { display: 'flex', alignItems: 'center', gap: 8, marginTop: 6, flexWrap: 'wrap', fontSize: 12 } },
    h(Dropdown, { menu: attachMenu, trigger: ['click'], placement: 'topLeft' },
      h('span', {
        style: { cursor: 'pointer', color: '#1677ff', flex: 'none' },
        title: t('上传或粘贴文本/图片附件（最多 8 个）')
      }, t('📎 附件'))),
    files.length ? h(Muted, { style: { flex: 'none' } }, files.length + '/8') : null,
    h('input', { ref: fileInput, type: 'file', multiple: true, style: { display: 'none' }, onChange: pickFiles }));

  const composer = h('div', {
    style: { flex: 'none', padding: '4px 16px 8px', borderTop: pending ? '0' : '1px solid #f0f0f0' },
    onDragOver: e => e.preventDefault(),
    onDrop: e => { e.preventDefault(); acceptFiles(e.dataTransfer?.files); }
  },
    h(Input.TextArea, {
      value: text, onChange: e => setText(e.target.value), autoSize: { minRows: 3, maxRows: 10 },
      placeholder: t('描述任务（Enter=本步后发送，Ctrl+Enter=本轮后发送，Shift+Enter=软换行）'),
      onKeyDown: e => {
        // 与 WinForms 一致：Enter 即时发送（运行中=打断本轮），Ctrl+Enter 排队等本轮返回，Shift+Enter 软换行；输入法组字中的 Enter 不算
        if (e.key !== 'Enter' || e.shiftKey || e.nativeEvent?.isComposing) return;
        e.preventDefault();
        send(!e.ctrlKey);
      },
      onPaste: e => {
        // 与 WinForms 一致：剪贴板里是文件/图片就追加成附件，纯文本照常粘贴
        const picked = Array.from(e.clipboardData?.files || []);
        if (!picked.length) return;
        e.preventDefault();
        acceptFiles(picked);
      }
    }),
    attachRow,
    toolbar);

  const loopSide = h('div', { style: { padding: 12, display: 'grid', gap: 8 } },
    h(HarnessModeRow, { workspace: snapshot?.workspace || '' }),
    h(Button, { size: 'small' }, t('待答 ') + ((snapshot?.prompts || []).length)),
    h(Muted, null, 'def: —'),
    h(Muted, null, t('考核点: —')),
    h(Muted, { style: { wordBreak: 'break-all' } }, 'trace: ' + (snapshot?.traceDirectory || '—')),
    h(Muted, null, t('版本: ') + (snapshot?.activationVersion
      ? 'v' + snapshot.activationVersion + ' · ' + snapshot.featureCount + t(' 特征 · ') + snapshot.ruleCount + t(' 规则') : '—')));

  const detailSide = h('div', { style: { padding: 12, display: 'grid', gap: 6 } },
    h(Muted, null, t('模型')), h('div', { style: { wordBreak: 'break-all' } }, snapshot?.model || '—'),
    h(Muted, null, t('努力')), h('div', null, snapshot?.effort || 'Auto'),
    h(Muted, null, t('上下文')),
    h('div', null, (snapshot?.contextTokens != null ? snapshot.contextTokens.toLocaleString() : '—') + ' / '
      + (snapshot?.contextLimit > 0 ? snapshot.contextLimit.toLocaleString() : '—')),
    h(Muted, null, t('成本')), h('div', null, snapshot?.cost != null ? snapshot.cost.toFixed(4) : '—'),
    h(Muted, null, t('状态')), h('div', null, [snapshot?.state, snapshot?.outcome].filter(Boolean).join(' · ') || '—'),
    h(Muted, null, t('工作区')), h('div', { style: { wordBreak: 'break-all' } }, snapshot?.workspace || '—'));

  const puppetAll = ((puppetSheet && puppetSheet.actions) || []).map(a => {
    const params = (a.parameters || []).slice()
      .sort((x, y) => (x.required ? 0 : 1) - (y.required ? 0 : 1));
    return {
      name: a.name,
      desc: a.desc || '',
      paramText: params.length ? params.map(p => p.name + (p.required ? '' : '?')).join(', ') : '—'
    };
  }).sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }));

  const puppetQ = puppetFilter.trim().toLowerCase();
  const puppetRows = puppetQ
    ? puppetAll.filter(r => (r.name + ' ' + r.paramText + ' ' + r.desc).toLowerCase().includes(puppetQ))
    : puppetAll;

  const puppetHint = puppetSheet?.error
    ? t('读不到清单：') + puppetSheet.error
    : puppetSheet
      ? t('Puppet Action 清单 · 实例 ') + (puppetSheet.instance || '—') + ' · ' + puppetRows.length
        + (puppetQ ? ' / ' + puppetAll.length : '') + t(' 个 action')
        + (puppetSheet.at ? ' · ' + puppetSheet.at.toLocaleTimeString() : '')
      : t('Puppet Action 清单 · 读取中…');

  const puppetSide = h('div', { style: { padding: 12, display: 'grid', gap: 8 } },
    h(Input, {
      size: 'small', allowClear: true, placeholder: t('筛选 action / 参数 / 说明'),
      value: puppetFilter, onChange: e => setPuppetFilter(e.target.value)
    }),
    h('div', { style: { display: 'flex', alignItems: 'center', gap: 8 } },
      h(Muted, { style: { flex: 1, minWidth: 0 } }, puppetHint),
      h(Button, { size: 'small', loading: !!puppetSheet?.loading, onClick: loadPuppet }, t('刷新'))),
    h(Table, {
      size: 'small', rowKey: 'name', pagination: false, dataSource: puppetRows, className: 'ol-puppet-table',
      locale: { emptyText: puppetSheet?.loading ? t('读取中…') : t('没有可用的 action') },
      onRow: row => ({ onDoubleClick: () => setText('/puppet ' + row.name + ' ') }),
      columns: [
        { title: 'Action', dataIndex: 'name', width: 116, ellipsis: true },
        { title: t('参数'), dataIndex: 'paramText', width: 104, ellipsis: true },
        { title: t('说明'), dataIndex: 'desc', width: 120, ellipsis: true }
      ]
    }),
    h(Muted, null, puppetSheet?.error
      ? t('壳启用 Puppet（B 面）后才能列出 action；清单来自壳的 /appagent/manifest。')
      : t('双击一行把这条命令填进输入框：/puppet <action> <参数…>（参数用自然语言写，模型自己对应）')));

  const side = h('div', {
    style: {
      width: 372, flex: 'none', borderLeft: '1px solid #f0f0f0', background: '#fff',
      display: 'flex', flexDirection: 'column', minHeight: 0
    }
  },
    h('div', { style: { padding: 8, borderBottom: '1px solid #f0f0f0' } },
      h(Segmented, {
        block: true, size: 'small', value: sideTab, onChange: setSideTab,
        options: [{ label: t('详情'), value: 'detail' }, { label: t('旁路'), value: 'btw' },
          { label: 'Harness', value: 'loop' }, { label: 'Puppet', value: 'puppet' }]
      })),
    h('div', { className: 'ol-scroll', style: { flex: 1, minHeight: 0 } },
      sideTab === 'loop' ? loopSide
        : sideTab === 'detail' ? detailSide
          : sideTab === 'puppet' ? puppetSide
            : h('div', { style: { padding: 24 } }, h(Empty, { image: Empty.PRESENTED_IMAGE_SIMPLE, description: t('旁路未接入浏览器') }))));

  return h('div', { style: { display: 'flex', flexDirection: 'column', height: '100%', minHeight: 0 } },
    header,
    h('div', { style: { flex: 1, minHeight: 0, display: 'flex' } },
      h('div', { style: { flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', minHeight: 0 } },
        chat, pending, composer),
      sideOpen ? side : null));
}

// ---------------- Loop ----------------

/**
 * Harness 生效方式（与 WinForms 的 Harness 页同一套语义）：
 * 用户直配 = 改动下一个会话直接生效；A/B 评测 = Prompt / AGENTS.md 先暂存为候选包，
 * 过考核题并严格更好才激活。workspace 变了才重新拉一次，避免每次输入都请求。
 */
function HarnessModeRow({ workspace, onMode }) {
  const ws = (workspace || '').trim();
  const [mode, setMode] = useState('direct');
  const [pending, setPending] = useState([]);

  const load = async () => {
    if (!ws) { setMode('direct'); setPending([]); return; }
    try {
      const r = await api('/api/loop/mode?workspace=' + encodeURIComponent(ws));
      setMode(r?.mode === 'eval' ? 'eval' : 'direct');
      setPending(r?.pending || []);
    } catch { /* 读不到就保持默认，不打扰用户 */ }
  };
  useEffect(() => { load(); }, [ws]);
  useEffect(() => { onMode && onMode(mode); }, [mode]);

  const toggle = async () => {
    if (!ws) return message.warning(t('先填工作目录'));
    const next = mode === 'eval' ? 'direct' : 'eval';
    try {
      const r = await post('/api/loop/mode', { workspace: ws, mode: next });
      setMode(r?.mode === 'eval' ? 'eval' : 'direct');
      message.success(r?.mode === 'eval'
        ? t('已切到 A/B 评测模式：Prompt 与 AGENTS.md 的改动先暂存为候选包')
        : t('已切到用户直配模式：Prompt 与 AGENTS.md 的改动下一个会话直接生效'));
      load();
    } catch (e) { message.error(t('切换失败：') + e.message); }
  };

  return h('div', { style: { display: 'grid', gap: 6 } },
    h(Button, { size: 'small', block: true, onClick: toggle },
      mode === 'eval' ? t('模式：A/B 评测') : t('模式：用户直配')),
    mode === 'eval' ? h(Muted, { style: { wordBreak: 'break-all' } },
      pending.length
        ? t('候选包待评估：') + pending.map(k => k === 'agents' ? 'AGENTS.md' : t('系统提示词')).join(' / ')
        : t('没有待评估的候选包')) : null);
}

function LoopPage({ defaultWorkspace, shot, setShot }) {
  const [workspace, setWorkspace] = useState(localStorage.getItem('openloop.workspace') || defaultWorkspace || '');
  const [ledger, setLedger] = useState('');
  const [skills, setSkills] = useState([]);
  const [evolve, setEvolve] = useState(null);
  const [iteration, setIteration] = useState(null);
  const [plan, setPlan] = useState('');
  const [model, setModel] = useState('');
  const [ruleName, setRuleName] = useState('');
  const [ruleText, setRuleText] = useState('');
  // 直配模式下候选包 / 考核题 / 评估报告 / 演进 / 回滚都不展开（与 WinForms Harness 页同一套）
  const [evalMode, setEvalMode] = useState(false);
  const [shellIncluded, setShellIncluded] = useState(true);
  const [publishMode, setPublishMode] = useState('framework');
  const wsRef = useRef(workspace);
  useEffect(() => { wsRef.current = workspace; }, [workspace]);
  useEffect(() => { if (!workspace && defaultWorkspace) setWorkspace(defaultWorkspace); }, [defaultWorkspace]);
  useEffect(() => { localStorage.setItem('openloop.workspace', workspace); }, [workspace]);

  const refresh = async () => {
    const ws = wsRef.current.trim();
    if (!ws) { setLedger(t('先填工作目录')); setSkills([]); return; }
    try {
      const payload = await api('/api/loop/activation?workspace=' + encodeURIComponent(ws));
      if (payload?.activation) {
        const a = payload.activation;
        const rules = (a.features || []).filter(f => f.id && f.id.startsWith('rule.')).length;
        setLedger(tf('v{version} · {features} 特征 · {rules} 规则', { version: a.version, features: (a.features || []).length, rules }));
      } else setLedger(t('账本未初始化（先跑一次或新建会话）'));
      setSkills(payload?.skills || []);
    } catch (e) { setLedger(t('读账本失败：') + e.message); setSkills([]); }
  };

  const pollEvolve = async () => {
    const ws = wsRef.current.trim();
    if (!ws) return;
    try {
      const s = await api('/api/loop/evolve?workspace=' + encodeURIComponent(ws));
      setEvolve(s);
      if (s?.state === 'running') setTimeout(pollEvolve, 2000);
      else refresh();
    } catch { /* 保持上次状态 */ }
  };

  const pollIteration = async () => {
    const ws = wsRef.current.trim();
    if (!ws) return;
    try {
      const s = await api('/api/loop/iteration?workspace=' + encodeURIComponent(ws));
      setIteration(s);
      if (s?.steps?.length) setPlan(s.steps.slice(-40).join('\n'));
      if (s?.state === 'running') setTimeout(pollIteration, 2000);
    } catch { /* 保持上次状态 */ }
  };

  useEffect(() => {
    if (!workspace.trim()) return;
    const timer = setTimeout(() => { refresh(); pollEvolve(); pollIteration(); }, 250);
    return () => clearTimeout(timer);
  }, [workspace]);

  const evolveText = useMemo(() => {
    if (!evolve) return '—';
    const parts = [evolve.state, evolve.phase];
    if (evolve.taskCount) parts.push(tf('题目 {n}', { n: evolve.taskCount }));
    if (evolve.state === 'done') parts.push(tf('候选 {cp}/{n}，基线 {bp}/{n}', { cp: evolve.candidatePassed, n: evolve.taskCount, bp: evolve.baselinePassed }));
    if (evolve.adopted) parts.push(t('已采纳 v') + evolve.candidateVersion);
    if (evolve.detail) parts.push(evolve.detail);
    if (evolve.error) parts.push(evolve.error);
    return parts.filter(Boolean).join(' · ');
  }, [evolve]);

  const iterationText = useMemo(() => {
    if (!iteration) return '—';
    const parts = [iteration.state];
    if (iteration.version) parts.push('v' + iteration.version);
    if (iteration.ok) parts.push(t('成功'));
    if (iteration.rolledBack) parts.push(t('已回滚'));
    if (iteration.error) parts.push(iteration.error);
    return parts.filter(Boolean).join(' · ');
  }, [iteration]);

  return h('div', { style: { display: 'flex', height: '100%', minHeight: 0 } },
    h('div', { className: 'ol-scroll', style: { flex: 1, minWidth: 0, padding: '14px 20px' } },
      h('div', { className: 'ol-section-title' }, t('工作区')),
      h(Input, {
        value: workspace, onChange: e => setWorkspace(e.target.value),
        placeholder: t('绝对路径'),
        onPressEnter: () => { refresh(); pollEvolve(); pollIteration(); },
        onBlur: () => { refresh(); pollEvolve(); pollIteration(); }
      }),
      h(Muted, { style: { display: 'block', margin: '6px 0 10px' } }, ledger || '—'),
      h(HarnessModeRow, { workspace, onMode: setEvalMode }),

      evalMode ? h('div', { style: { display: 'contents' } },
        h('div', { className: 'ol-section-title', style: { marginTop: 16 } }, t('演进')),
        h(Muted, { style: { display: 'block', margin: '0 0 8px' } },
          t('Harness 管当前工作区的五类对象：Prompt（系统提示词模板 + AGENTS.md）、Skill、Rule、Limit、Guard（含连接重试、免费额度恢复等自愈参数）；')
          + t('改提示词与 AGENTS.md 在桌面壳里用 /sysprompt、/agentsmd 让模型改，用户不直接编辑文本。')),
        h(Muted, { style: { display: 'block', margin: '0 0 8px' } },
          t('演进是攒够证据后的一次集中分诊：harness 类出候选并过考核题，源码类写派单，未知留到下一批。')
          + t('观察用 /report <内容> 记入 findings，只落账、不立即处理。')),
        h(Space, { wrap: true, style: { marginBottom: 6 } },
          h(Input, { value: model, onChange: e => setModel(e.target.value), placeholder: t('模型（留空用默认）'), style: { width: 280 } }),
          h(Button, {
            type: 'primary', disabled: evolve?.state === 'running',
            onClick: async () => {
              const ws = workspace.trim();
              if (!ws) return message.warning(t('先填工作目录'));
              try {
                setEvolve(await post('/api/loop/evolve', { workspace: ws, model: model.trim(), effort: '' }));
                setTimeout(pollEvolve, 2000);
              } catch (e) { message.error(t('演进启动失败：') + e.message); }
            }
          }, t('演进')),
          h(Button, {
            disabled: evolve?.state !== 'running',
            onClick: () => post('/api/loop/evolve/stop', { workspace: workspace.trim() }).catch(e => message.error(e.message))
          }, t('停止')),
          h('span', { style: { display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: '#8c8c8c' } },
            h(Switch, {
              size: 'small', checked: !!evolve?.autoEnabled,
              onChange: async enabled => {
                try {
                  const r = await post('/api/loop/auto', { workspace: workspace.trim(), enabled });
                  message.success(r.enabled ? t('自动演进已开启') : t('自动演进已关闭'));
                  pollEvolve();
                } catch (e) { message.error(t('设置失败：') + e.message); }
              }
            }), t('自动演进'))),
        h(Muted, { style: { display: 'block', marginBottom: 16 } }, evolveText),

        h('div', { className: 'ol-section-title' }, t('规则与回滚')),
        h(Space, { wrap: true, style: { marginBottom: 16 } },
          h(Input, { value: ruleName, onChange: e => setRuleName(e.target.value), placeholder: t('规则 id，如 rule.style'), style: { width: 220 } }),
          h(Input, { value: ruleText, onChange: e => setRuleText(e.target.value), placeholder: t('规则内容'), style: { width: 320 } }),
          h(Button, {
            onClick: async () => {
              const ws = workspace.trim();
              if (!ws || !ruleName.trim()) return message.warning(t('工作目录和规则 id 都要填'));
              try {
                const r = await post('/api/loop/rule', { workspace: ws, name: ruleName.trim(), text: ruleText.trim() });
                setRuleName(''); setRuleText('');
                message.success(t('已登记 ') + r.id);
                refresh();
              } catch (e) { message.error(t('登记失败：') + e.message); }
            }
          }, t('登记规则')),
          h(Popconfirm, {
            title: t('回滚到上一版？'), description: t('会写成新版本，旧版本文件不动。'),
            okText: t('回滚'), cancelText: t('取消'),
            onConfirm: async () => {
              try {
                const r = await post('/api/loop/rollback', { workspace: workspace.trim() });
                message.success(t('已回滚，当前 v') + r.version);
                refresh();
              } catch (e) { message.error(t('回滚失败：') + e.message); }
            }
          }, h(Button, null, t('回滚上一版')))),

        h('div', { className: 'ol-section-title' }, t('迭代')),
        h(Space, { wrap: true, style: { marginBottom: 6 } },
          h(Button, {
            type: 'primary', disabled: iteration?.state === 'running',
            onClick: async () => {
              const ws = workspace.trim();
              if (!ws) return message.warning(t('先填工作目录'));
              try {
                setIteration(await post('/api/loop/iteration', { workspace: ws, shell: shellIncluded, publishMode }));
                setTimeout(pollIteration, 2000);
              } catch (e) { message.error(t('迭代启动失败：') + e.message); }
            }
          }, t('开始迭代')),
          h(Button, {
            disabled: iteration?.state === 'running',
            onClick: async () => {
              const ws = workspace.trim();
              if (!ws) return message.warning(t('先填工作目录'));
              try {
                const r = await api('/api/loop/iteration/plan?workspace=' + encodeURIComponent(ws));
                const lines = [];
                if (r.summary) lines.push(r.summary);
                for (const p of r.plan || []) lines.push(`${p.role} → ${p.target}${p.update === false ? t('（跳过）') : ''}`);
                setPlan(lines.join('\n'));
              } catch (e) { message.error(t('生成计划失败：') + e.message); }
            }
          }, t('生成计划')),
          h(Button, {
            disabled: iteration?.state !== 'running',
            onClick: () => post('/api/loop/iteration/stop', {}).catch(e => message.error(e.message))
          }, t('停止')),
          h('span', { style: { display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: '#8c8c8c' } },
            h(Switch, { size: 'small', checked: shellIncluded, onChange: setShellIncluded }), t('连壳')),
          h('span', { style: { display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: '#8c8c8c' } },
            t('形态'),
            h(Select, {
              size: 'small', style: { width: 200 }, value: publishMode, onChange: setPublishMode,
              disabled: iteration?.state === 'running',
              options: [
                { value: 'framework', label: t('框架依赖（需装 .NET）') },
                { value: 'selfcontained', label: t('自带框架') }
              ]
            }))),
        h(Muted, { style: { display: 'block', marginBottom: 8 } }, iterationText),
      ) : null,
      plan ? h('pre', { className: 'ol-tools' }, plan) : null),

    h('div', {
      style: {
        width: 320, flex: 'none', borderLeft: '1px solid #f0f0f0', background: '#fff',
        padding: 12, display: 'flex', flexDirection: 'column', gap: 6, minHeight: 0
      }
    },
      h(Muted, null, t('工作区')), h('div', { style: { wordBreak: 'break-all' } }, workspace || '—'),
      h(Muted, { style: { marginTop: 8 } }, t('账本')), h('div', null, ledger || '—'),
      h(Muted, { style: { marginTop: 8 } }, t('技能')), h('div', null, skills.length ? skills.length + t(' 个启用') : t('（无）')),
      skills.length ? h('pre', { className: 'ol-tools', style: { maxHeight: 240 } }, skills.join('\n')) : null));
}

// ---------------- Puppet 页面钩子 ----------------

async function capturePage(region) {
  const root = document.documentElement;
  const width = root.scrollWidth;
  const height = root.scrollHeight;
  const clone = root.cloneNode(true);
  const source = [root, ...root.querySelectorAll('*')];
  const target = [clone, ...clone.querySelectorAll('*')];
  for (let i = 0; i < source.length; i++) {
    if (target[i].tagName === 'SCRIPT') { target[i].remove(); continue; }
    const computed = getComputedStyle(source[i]);
    let css = '';
    for (const name of computed) css += name + ':' + computed.getPropertyValue(name) + ';';
    target[i].style.cssText = css;
    const node = target[i];
    const live = source[i];
    if (node instanceof HTMLInputElement) {
      if (node.type !== 'password') node.setAttribute('value', live.value);
      if (node.type === 'checkbox' || node.type === 'radio') node.toggleAttribute('checked', live.checked);
    } else if (node instanceof HTMLSelectElement) {
      node.querySelectorAll('option').forEach((option, index) =>
        option.toggleAttribute('selected', !!live.options[index] && live.options[index].selected));
    } else if (node instanceof HTMLTextAreaElement) {
      node.textContent = live.value;
    }
  }
  const hideBars = document.createElement('style');
  hideBars.textContent = '*{scrollbar-width:none!important} *::-webkit-scrollbar{display:none!important;width:0!important;height:0!important}';
  (clone.querySelector('head') || clone).appendChild(hideBars);
  const svg = '<svg xmlns="http://www.w3.org/2000/svg" width="' + width + '" height="' + height +
    '"><foreignObject width="' + width + '" height="' + height + '">' +
    new XMLSerializer().serializeToString(clone) + '</foreignObject></svg>';
  const bitmap = await new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error(t('svg 渲染失败')));
    image.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg);
  });
  const x = Math.max(0, region.x | 0);
  const y = Math.max(0, region.y | 0);
  const w = region.width > 0 ? Math.min(region.width, width - x) : width - x;
  const hh = region.height > 0 ? Math.min(region.height, height - y) : height - y;
  if (w < 1 || hh < 1) throw new Error(t('截图区域为空'));
  const canvas = document.createElement('canvas');
  canvas.width = w;
  canvas.height = hh;
  canvas.getContext('2d').drawImage(bitmap, x, y, w, hh, 0, 0, w, hh);
  return canvas.toDataURL('image/png');
}

// ---------------- App ----------------

// ---------------- 设置（对齐 WinForms 设置页） ----------------

const SettingRow = ({ label, children }) => h('div', {
  style: { display: 'grid', gridTemplateColumns: '88px 1fr', alignItems: 'center', gap: 10, marginBottom: 10 }
},
  h('div', { style: { fontSize: 13, color: '#595959' } }, label),
  h('div', { style: { minWidth: 0 } }, children));

const SettingHint = ({ children, warn }) => h('div', {
  style: { fontSize: 12, color: warn ? '#ff4d4f' : '#8c8c8c', margin: '-4px 0 12px 98px' }
}, children);

const SwitchRow = ({ label, checked, onChange, hint, warn }) => h('div', null,
  h(SettingRow, { label },
    h('div', { style: { display: 'flex', alignItems: 'center', gap: 8 } },
      h(Switch, { checked: !!checked, onChange }),
      hint ? h('span', { style: { fontSize: 12, color: warn ? '#ff4d4f' : '#8c8c8c' } }, hint) : null)));

const blankProvider = () => ({
  name: 'OpenRouter', kind: 'openrouter', baseUrl: 'https://openrouter.ai/api/v1',
  hasKey: false, key: null, models: []
});

function SettingsPanel({ caps, onClose, onLanguage }) {
  const [loaded, setLoaded] = useState(null);
  const [draft, setDraft] = useState(null);
  const [tab, setTab] = useState('general');
  const [busy, setBusy] = useState(false);
  const [providerIndex, setProviderIndex] = useState(0);
  const [modelInput, setModelInput] = useState('');
  const [propsModel, setPropsModel] = useState(null);
  const [propsDraft, setPropsDraft] = useState({ name: '', contextLimit: 0 });
  const [providerStatus, setProviderStatus] = useState('');
  const [skills, setSkills] = useState({ root: '', rows: [], error: '' });
  const [skillScope, setSkillScope] = useState('project');
  const [installPath, setInstallPath] = useState('');
  const [plugins, setPlugins] = useState({ workspaceRoot: '', globalRoot: '', rows: [], error: '' });
  const [pluginDraft, setPluginDraft] = useState(null);
  const workspace = caps?.workspace || localStorage.getItem('openloop.workspace') || '';

  const reload = async () => {
    try {
      const s = await api('/api/settings');
      setLoaded(s);
      setDraft(JSON.parse(JSON.stringify(s)));
      setProviderIndex(0);
      onLanguage?.(s.language);
    } catch (e) { message.error(t('读取设置失败：') + e.message); }
  };
  useEffect(() => { reload(); }, []);

  const loadSkills = async scope => {
    try {
      setSkills(await api(`/api/settings/skills?scope=${scope}&workspace=${encodeURIComponent(workspace)}`) || {});
    } catch (e) { message.error(t('读取技能失败：') + e.message); }
  };
  useEffect(() => { if (tab === 'skills') loadSkills(skillScope); }, [tab, skillScope]);

  const loadPlugins = async () => {
    try {
      setPlugins(await api(`/api/settings/plugins?workspace=${encodeURIComponent(workspace)}`) || { rows: [] });
    } catch (e) { message.error(t('读取插件失败：') + e.message); }
  };
  useEffect(() => { if (tab === 'plugins') loadPlugins(); }, [tab]);

  const patch = p => setDraft(d => ({ ...d, ...p }));
  const providers = draft?.providers || [];
  const provider = providers[providerIndex] || null;
  const setProvider = (i, p) => setDraft(d => {
    const list = [...(d.providers || [])];
    list[i] = { ...list[i], ...p };
    return { ...d, providers: list };
  });

  const save = async () => {
    if (draft.fullAccess && !loaded.fullAccess
      && !window.confirm(t('完全许可会关闭路径边界、命令审批与环境变量隔离，子进程可能拿到密钥，也可以直接访问网络与任意路径。确定启用？'))) return;
    setBusy(true);
    try {
      const modelProps = {};
      for (const [id, p] of Object.entries(draft.modelProps || {})) {
        if (!id) continue;
        if ((p?.name || '') === '' && !p?.contextLimit) continue;
        modelProps[id] = { name: p?.name || '', contextLimit: p?.contextLimit || 0 };
      }
      await post('/api/settings', { ...draft, modelProps });
      message.success(t('已保存'));
      await reload();
    } catch (e) { message.error(t('保存失败：') + e.message); }
    setBusy(false);
  };

  const addProvider = () => {
    const list = [...providers, blankProvider()];
    setDraft(d => ({ ...d, providers: list }));
    setProviderIndex(list.length - 1);
    setProviderStatus('');
  };

  const removeProvider = () => {
    if (!provider) return;
    if (!window.confirm(tf('删除提供商「{name}」？', { name: provider.name }))) return;
    const list = providers.filter((_, i) => i !== providerIndex);
    setDraft(d => ({ ...d, providers: list }));
    setProviderIndex(Math.max(0, Math.min(providerIndex, list.length - 1)));
    setProviderStatus('');
  };

  const providerBody = extra => ({
    name: provider?.name || '', kind: provider?.kind || '', baseUrl: provider?.baseUrl || '',
    key: provider?.key ?? null, ...extra
  });

  const fetchModels = async () => {
    if (!provider) return;
    setProviderStatus(t('正在拉取模型…'));
    try {
      const r = await post('/api/settings/provider/fetch', providerBody({}));
      const merged = [...new Set([...(provider.models || []), ...(r.models || [])])];
      setProvider(providerIndex, { models: merged });
      setProviderStatus(tf('拉取到 {n} 个模型', { n: (r.models || []).length }));
    } catch (e) {
      setProviderStatus(t('拉取失败'));
      message.error(t('拉取失败：') + e.message);
    }
  };

  const testProvider = async () => {
    if (!provider) return;
    const model = (provider.models || [])[0];
    if (!model) return message.warning(t('先拉取或添加一个模型'));
    setProviderStatus(t('正在测试连接…'));
    try {
      const r = await post('/api/settings/provider/test', providerBody({ model }));
      setProviderStatus(t('连接正常：') + (r.reply || ''));
    } catch (e) {
      setProviderStatus(t('连接失败'));
      message.error(t('连接失败：') + e.message);
    }
  };

  const addModel = () => {
    const id = modelInput.trim();
    if (!id || !provider) return;
    if ((provider.models || []).some(m => m.toLowerCase() === id.toLowerCase())) return;
    setProvider(providerIndex, { models: [...(provider.models || []), id] });
    setModelInput('');
  };

  const removeModel = id => {
    if (!provider) return;
    if (provider.plugin && !(provider.declared || []).some(m => m.toLowerCase() === id.toLowerCase())) {
      setDisabled(id, true);
      return;
    }
    setProvider(providerIndex, { models: (provider.models || []).filter(m => m !== id) });
  };

  const openProps = id => {
    const p = draft?.modelProps?.[id];
    setPropsDraft({ name: p?.name || '', contextLimit: p?.contextLimit || 0 });
    setPropsModel(id);
  };

  const applyProps = () => {
    setDraft(d => ({
      ...d,
      modelProps: { ...(d.modelProps || {}), [propsModel]: { name: propsDraft.name.trim(), contextLimit: propsDraft.contextLimit || 0 } }
    }));
    setPropsModel(null);
  };

  const toggleSkill = async (row, enabled) => {
    try {
      await post('/api/settings/skills', { workspace, scope: skillScope, name: row.name, enabled });
      await loadSkills(skillScope);
    } catch (e) { message.error(t('切换失败：') + e.message); }
  };

  const uninstallSkill = async row => {
    if (!window.confirm(tf('确定卸载技能「{name}」？\n\n会删除整个目录，不可恢复：\n{dir}', { name: row.name, dir: row.directory }))) return;
    try {
      await post('/api/settings/skills/uninstall', { workspace, scope: skillScope, name: row.name });
      message.success(t('已卸载 ') + row.name);
      await loadSkills(skillScope);
    } catch (e) { message.error(t('卸载失败：') + e.message); }
  };

  const installSkill = async () => {
    if (!installPath.trim()) return message.warning(t('先填技能目录的绝对路径'));
    try {
      const r = await post('/api/settings/skills/install', {
        workspace, scope: skillScope, path: installPath.trim()
      });
      setInstallPath('');
      message.success(t('已安装到 ') + r.directory);
      await loadSkills(skillScope);
    } catch (e) { message.error(t('安装失败：') + e.message); }
  };

  const blankPlugin = () => ({
    directory: '', id: '', name: '', baseUrl: '', modelsUrl: '', modelPrefix: '', key: '', models: 0
  });

  const savePlugin = async () => {
    if (!pluginDraft) return;
    if (!(pluginDraft.id || '').trim()) return message.warning(t('插件 id 不能为空'));
    try {
      await post('/api/settings/plugins', { ...pluginDraft, workspace });
      message.success(t('已保存，会话里的提供商下拉重开一次就能看到新模型'));
      setPluginDraft(null);
      await loadPlugins();
    } catch (e) { message.error(t('保存失败：') + e.message); }
  };

  const removePlugin = async row => {
    const where = row.from === '全局' ? t('全局') : t('当前工作区');
    if (!window.confirm(tf('卸载{where}插件 {id}？\n\n整个目录会移进 OpenLoop 的插件回收目录，需要时能捞回来。\n{dir}', { where, id: row.id, dir: row.directory }))) return;
    try {
      await post('/api/settings/plugins/remove', { workspace, directory: row.directory });
      message.success(t('已卸载 ') + row.id);
      await loadPlugins();
    } catch (e) { message.error(t('卸载失败：') + e.message); }
  };

  if (!draft) return h('div', { style: { padding: 30, textAlign: 'center' } }, h(Muted, null, t('正在读取设置…')));

  const general = [
    h(SettingRow, { key: 'lang', label: t('语言') },
      h(Select, {
        value: draft.language || '', style: { maxWidth: 240 },
        onChange: v => patch({ language: v }),
        options: [{ value: '', label: t('跟随系统') }, { value: 'zh', label: t('中文') }, { value: 'en', label: 'English' }]
      })),
    h(SettingRow, { key: 'theme', label: t('主题色') },
      h(Select, {
        value: draft.theme || 'light', style: { maxWidth: 240 },
        onChange: v => patch({ theme: v }),
        options: [{ value: 'light', label: t('浅色') }, { value: 'dark', label: t('深色') }, { value: 'grayblue', label: t('灰蓝') }]
      })),
    h(SwitchRow, {
      key: 'full', label: t('完全许可'), checked: draft.fullAccess,
      onChange: v => patch({ fullAccess: v }),
      hint: t('关闭路径边界、命令审批与环境隔离，可直连网络'), warn: true
    }),
    h(SwitchRow, {
      key: 'auto', label: t('自动审批'), checked: draft.autoApprove,
      onChange: v => patch({ autoApprove: v }),
      hint: t('一切工具调用都无需审批（含工作区外访问与风险操作）')
    }),
    h(SwitchRow, {
      key: 'agents', label: 'AGENTS.md', checked: draft.useAgentsMd,
      onChange: v => patch({ useAgentsMd: v }),
      hint: t('把全局与项目 AGENTS.md 注入系统提示词')
    }),
    h(SwitchRow, {
      key: 'think', label: t('思考过程'), checked: draft.thinkingExpandedByDefault,
      onChange: v => patch({ thinkingExpandedByDefault: v }),
      hint: t('新回话默认展开「思考与工具过程」')
    }),
    h(SwitchRow, {
      key: 'evolve', label: t('自动演进'), checked: draft.autoEvolve,
      onChange: v => patch({ autoEvolve: v }),
      hint: t('改行为版本：护栏、阈值、规则与技能顺序')
    }),
    h(SettingHint, { key: 'evolve-hint' },
      t('演进的对象是 Agent 的行为版本：只改当前工作区的护栏、阈值、规则与技能顺序（.openloop 激活集），')
      + t('不编译、不换二进制；攒够新证据就自动跑一轮 A/B 考核，候选更好才采纳。')),
    h(SwitchRow, {
      key: 'iterate', label: t('自动迭代'), checked: draft.autoIterate,
      onChange: v => patch({ autoIterate: v }),
      hint: t('改程序版本：工作树源码构建升版，换掉 A/I')
    }),
    h(SettingHint, { key: 'iterate-hint' },
      t('迭代的对象是 OpenLoop 程序本身：把工作树里的源码改动构建、升版并替换 A/I（不连壳，壳仍由 Loop 页「迭代」换）。')
      + t('演进采纳后自动跑一次，会重启 A、中断正在跑的会话；工作树有未提交改动时跳过。')),
    h(SettingHint, { key: 'note' }, t('主题与语言在重启 OpenLoop 后生效；完全许可、自动审批、自动演进与自动迭代立即生效。'))
  ];

  const collab = [
    h(SwitchRow, {
      key: 'collab-on', label: t('启用协作'), checked: draft.enableCollab,
      onChange: v => patch({ enableCollab: v }),
      hint: t('给 Worker 与 Loop 会话挂 CollabStart / CollabMonitor / ReadSession')
    }),
    h(SettingRow, { key: 'collab-poll', label: t('监测间隔') },
      h(Select, {
        value: draft.collabPollSeconds || 30, style: { maxWidth: 240 },
        onChange: v => patch({ collabPollSeconds: v }),
        options: [
          { value: 10, label: t('10 秒') }, { value: 30, label: t('30 秒') },
          { value: 60, label: t('1 分钟') }, { value: 120, label: t('2 分钟') },
          { value: 300, label: t('5 分钟') }
        ]
      })),
    h(SwitchRow, {
      key: 'collab-reuse', label: t('沿用上下文'), checked: draft.collabReuseContext,
      onChange: v => patch({ collabReuseContext: v }),
      hint: t('协作会话带上发起方已完成的对话，复用前缀缓存（同模型才生效）')
    }),
    h(SettingHint, { key: 'collab-note' },
      t('协作会话是平级的独立会话，不是子 agent：它们不共享发起方的工具调用栈，只在同一工作区各干一段。')
      + t('发起方启动后不再自己动手，只做监测与协调；监测汇报以仿用户消息进入它的队列（队列里用特殊底色标出），')
      + t('全部会话结束或不可恢复地中止后自动停止。暂不支持跨工作区协作。'))
  ];

  const openCode = [
    h(SwitchRow, {
      key: 'ask', label: t('权限审批'), checked: draft.openCodeAskPermissions,
      onChange: v => patch({ openCodeAskPermissions: v }),
      hint: t('委派给 OpenCode 时，每个动作都经本应用审批')
    }),
    h(SettingHint, { key: 'note' }, t('服务通过 ~/.local/state/opencode/service.json 自动发现；未就绪时不会出现在模型列表里。'))
  ];

  const tools = [
    h(SettingRow, { key: 'webfetch', label: 'WebFetch' },
      h(Select, {
        value: draft.webFetchMode || 'allow', style: { maxWidth: 240 },
        onChange: v => patch({ webFetchMode: v }),
        options: [
          { value: 'allow', label: t('免审批（默认）') },
          { value: 'ask', label: t('每次审批') },
          { value: 'off', label: t('关闭') }
        ]
      })),
    h(SettingHint, { key: 'hint' }, t('只读抓取 http/https 网页正文并转纯文本；不执行 JavaScript，内容按不可信数据处理。')),
    h(SettingHint, { key: 'note' }, t('内网与环回地址一律拒绝；只抓文本/HTML/JSON/XML，单页最多 2MB，结果默认 6000 字符，一轮最多 8 次。Worker 与 Loop 同时生效。'))
  ];

  const providerOptions = providers.map((p, i) => ({
    value: i, label: p.plugin ? (p.label || (p.name + t('（插件）'))) : (p.name || (t('提供商 ') + (i + 1)))
  }));
  const modelProps = draft.modelProps || {};
  const disabledModels = draft.disabledModels || [];
  const isOff = id => disabledModels.some(m => m.toLowerCase() === id.toLowerCase());
  const setDisabled = (id, off) => setDraft(d => {
    const list = (d.disabledModels || []).filter(m => m.toLowerCase() !== id.toLowerCase());
    return { ...d, disabledModels: off ? [...list, id] : list };
  });

  const models = [
    h(SettingRow, { key: 'provider', label: t('提供商') },
      h('div', { style: { display: 'flex', gap: 8, flexWrap: 'wrap' } },
        h(Select, {
          value: providers.length ? providerIndex : undefined, placeholder: t('还没有提供商'),
          style: { minWidth: 220 }, options: providerOptions,
          onChange: v => { setProviderIndex(v); setProviderStatus(''); }
        }),
        h(Button, { onClick: addProvider }, t('添加')),
        h(Button, { danger: true, disabled: !provider || provider.plugin, onClick: removeProvider }, t('删除')))),
    h(SettingRow, { key: 'name', label: t('名称') },
      h(Input, {
        value: provider?.name || '', disabled: !provider || provider.plugin, style: { maxWidth: 320 },
        onChange: e => setProvider(providerIndex, { name: e.target.value })
      })),
    h(SettingRow, { key: 'url', label: t('接口地址') },
      h(Input, {
        value: provider?.baseUrl || '', disabled: !provider || provider.plugin, style: { maxWidth: 420 },
        onChange: e => setProvider(providerIndex, { baseUrl: e.target.value })
      })),
    h(SettingRow, { key: 'key', label: t('密钥') },
      h(Input.Password, {
        value: provider?.key ?? '', disabled: !provider || provider.plugin, style: { maxWidth: 420 },
        placeholder: provider?.plugin ? t('由插件清单管理')
          : provider?.hasKey && provider?.key == null ? t('已保存（留空表示不改）') : t('粘贴 API Key'),
        onChange: e => setProvider(providerIndex, { key: e.target.value })
      })),
    h(SettingRow, { key: 'actions', label: '' },
      h('div', { style: { display: 'flex', gap: 8, flexWrap: 'wrap' } },
        provider?.plugin ? null : h(Button, { disabled: !provider, onClick: fetchModels }, t('拉取模型')),
        provider?.plugin ? null : h(Button, { disabled: !provider, onClick: testProvider }, t('测试')),
        h(Muted, null, provider?.plugin
          ? (provider.ready ? t('插件提供商：模型的增删写回它的 plugin.json')
            : t('插件提供商当前不可用（服务未就绪），可以先停用'))
          : providerStatus))),
    h(SettingRow, { key: 'models', label: t('模型') },
      h('div', null,
        h('div', { style: { display: 'flex', gap: 8, marginBottom: 8 } },
          h(Input, {
            value: modelInput, placeholder: t('模型 id，回车加入'), style: { maxWidth: 320 },
            onChange: e => setModelInput(e.target.value),
            onPressEnter: addModel
          }),
          h(Button, { disabled: !provider, onClick: addModel }, t('加入'))),
        h('div', {
          className: 'ol-scroll',
          style: { height: 220, border: '1px solid #f0f0f0', borderRadius: 8, background: '#fafafa', padding: 6 }
        },
          (provider?.models || []).length === 0
            ? h(Muted, { style: { display: 'block', padding: 8 } }, t('这个提供商还没有模型'))
            : (provider.models || []).map(id => h('div', {
              key: id,
              style: { display: 'flex', alignItems: 'center', gap: 8, padding: '3px 6px' }
            },
              h('input', {
                type: 'checkbox', checked: !isOff(id), title: t('取消勾选=停用（不进出模型下拉）'),
                onChange: e => setDisabled(id, !e.target.checked)
              }),
              h('span', { style: { flex: 1, fontSize: 13, overflow: 'hidden', textOverflow: 'ellipsis' } }, id),
              modelProps[id]?.name || modelProps[id]?.contextLimit
                ? h(Muted, null, `${modelProps[id]?.name || ''}${modelProps[id]?.contextLimit ? ' · ' + modelProps[id].contextLimit : ''}`)
                : null,
              h(Button, { size: 'small', type: 'link', onClick: () => openProps(id) }, t('属性')),
              h(Button, { size: 'small', type: 'link', danger: true, onClick: () => removeModel(id) }, t('删除'))))))),
    h(SettingHint, { key: 'note' },
      t('「拉取模型」会请求提供商的 /models；「测试」会真的发一次最小补全。属性可设显示名称与上下文上限。')
      + t('插件贡献的提供商标着「（插件）」，模型增删写回插件清单，停用状态存在 settings.json。'))
  ];

  const skillRows = skills.rows || [];
  const skillsPage = [
    h(SettingRow, { key: 'scope', label: t('范围') },
      h('div', { style: { display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' } },
        h(Segmented, {
          value: skillScope, onChange: setSkillScope,
          options: [{ value: 'global', label: t('全局') }, { value: 'project', label: t('项目') }]
        }),
        h(Muted, null, skills.root || (workspace ? '' : t('先在项目里打开一个会话，或切到「全局」管理全局技能'))))),
    h(SettingRow, { key: 'install', label: t('安装') },
      h('div', { style: { display: 'flex', gap: 8, flexWrap: 'wrap' } },
        h(Input, {
          value: installPath, placeholder: t('含 SKILL.md 的目录（绝对路径）'), style: { maxWidth: 420 },
          onChange: e => setInstallPath(e.target.value)
        }),
        h(Button, { onClick: installSkill }, t('安装')))),
    h('div', {
      className: 'ol-scroll',
      style: { height: 320, border: '1px solid #f0f0f0', borderRadius: 8, padding: 6 }
    },
      skills.error ? h(Muted, { style: { display: 'block', padding: 8 } }, skills.error)
        : skillRows.length === 0 ? h(Muted, { style: { display: 'block', padding: 8 } }, t('没有可管理的技能'))
          : skillRows.map(row => h('div', {
            key: row.name,
            style: { display: 'flex', alignItems: 'center', gap: 10, padding: '6px 8px', borderBottom: '1px solid #f5f5f5' }
          },
            h(Switch, { size: 'small', checked: row.enabled, onChange: v => toggleSkill(row, v) }),
            h('div', { style: { minWidth: 0, flex: 1 } },
              h('div', { style: { fontSize: 13 } }, row.name,
                h(Muted, { style: { marginLeft: 8 } }, row.global ? t('全局') : row.opencode ? t('项目 .opencode') : t('项目'))),
              h(Muted, { style: { display: 'block', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } },
                row.description || row.directory || '')),
            h(Button, {
              size: 'small', danger: true,
              disabled: skillScope === 'project' && (row.global || row.opencode),
              onClick: () => uninstallSkill(row)
            }, t('卸载'))))),
    h(SettingHint, { key: 'note' }, t('项目技能停用/启用走账本（由 I 收敛）；全局技能只改本机开关。.opencode/skills 里的技能由 opencode 管理，这里只能看。'))
  ];

  const pluginRows = plugins.rows || [];
  const pluginsPage = [
    h(SettingRow, { key: 'roots', label: t('存放位置') },
      h('div', { style: { minWidth: 0 } },
        h(Muted, { style: { display: 'block', wordBreak: 'break-all' } },
          t('工作区 ') + (plugins.workspaceRoot || t('（还没打开工作区，只看全局）'))),
        h(Muted, { style: { display: 'block', wordBreak: 'break-all' } },
          t('全局 ') + (plugins.globalRoot || '')))),
    h(SettingRow, { key: 'actions', label: '' },
      h('div', { style: { display: 'flex', gap: 8, flexWrap: 'wrap' } },
        h(Button, { onClick: () => setPluginDraft(blankPlugin()) }, t('添加')),
        h(Button, { onClick: loadPlugins }, t('刷新')))),
    h('div', {
      className: 'ol-scroll',
      style: { height: 320, border: '1px solid #f0f0f0', borderRadius: 8, padding: 6 }
    },
      plugins.error ? h(Muted, { style: { display: 'block', padding: 8 } }, plugins.error)
        : pluginRows.length === 0 ? h(Muted, { style: { display: 'block', padding: 8 } }, t('还没有插件'))
          : pluginRows.map(row => h('div', {
            key: row.directory,
            style: { display: 'flex', alignItems: 'center', gap: 10, padding: '6px 8px', borderBottom: '1px solid #f5f5f5' }
          },
            h('div', { style: { minWidth: 0, flex: 1 } },
              h('div', { style: { fontSize: 13 } }, row.name,
                h(Muted, { style: { marginLeft: 8 } }, row.id),
                h(Muted, { style: { marginLeft: 8 } }, row.from || '')),
              h(Muted, { style: { display: 'block', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } },
                [row.kind, row.version, row.baseUrl].filter(Boolean).join(' · ') || row.directory)),
            h(Button, {
              size: 'small',
              disabled: !row.providers,
              onClick: () => setPluginDraft({
                directory: row.directory, id: row.id, name: row.name,
                baseUrl: row.baseUrl || '', modelsUrl: row.modelsUrl || '',
                modelPrefix: row.modelPrefix || '', key: '', models: row.models || 0
              })
            }, t('编辑')),
            h(Button, { size: 'small', danger: true, onClick: () => removePlugin(row) }, t('卸载'))))),
    h(SettingHint, { key: 'note' },
      t('存盘即生效，会话里的提供商下拉几秒内自己补上。「编辑」只改 provider 型插件的地址与密钥，')
      + t('工具/MCP 型插件的清单自己去目录里改；「卸载」把整个目录移进回收目录，可捞回。')
      + t('同名插件工作区优先，全局新插件点「添加」时写进当前工作区。'))
  ];

  return h('div', null,
    h(Tabs, {
      size: 'small', activeKey: tab, onChange: setTab,
      items: [
        { key: 'general', label: t('普通'), children: h('div', { style: { paddingTop: 12 } }, general) },
        { key: 'opencode', label: 'OpenCode', children: h('div', { style: { paddingTop: 12 } }, openCode) },
        { key: 'tools', label: t('工具'), children: h('div', { style: { paddingTop: 12 } }, tools) },
        { key: 'models', label: t('模型'), children: h('div', { style: { paddingTop: 12 } }, models) },
        { key: 'skills', label: t('技能'), children: h('div', { style: { paddingTop: 12 } }, skillsPage) },
        { key: 'plugins', label: t('插件'), children: h('div', { style: { paddingTop: 12 } }, pluginsPage) },
        { key: 'collab', label: t('协作'), children: h('div', { style: { paddingTop: 12 } }, collab) }
      ]
    }),
    h('div', { style: { display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 12 } },
      h(Button, { onClick: onClose }, t('取消')),
      h(Button, { type: 'primary', loading: busy, onClick: save }, t('保存'))),
    h(Modal, {
      open: pluginDraft != null, title: pluginDraft?.directory ? t('编辑插件') : t('添加插件'),
      okText: t('保存'), cancelText: t('取消'), width: 560,
      onOk: savePlugin, onCancel: () => setPluginDraft(null)
    },
      pluginDraft ? h('div', { style: { display: 'grid', gap: 10, paddingTop: 8 } },
        h(Muted, null, pluginDraft.directory || t('会写进当前工作区的插件目录（没有工作区就写全局）')),
        h(Input, {
          value: pluginDraft.id, placeholder: t('插件 id（也是目录名）'),
          onChange: e => setPluginDraft(d => ({ ...d, id: e.target.value }))
        }),
        h(Input, {
          value: pluginDraft.name, placeholder: t('下拉里显示的名字，留空用 id'),
          onChange: e => setPluginDraft(d => ({ ...d, name: e.target.value }))
        }),
        h(Input, {
          value: pluginDraft.baseUrl, placeholder: t('OpenAI 兼容根地址，例如 http://127.0.0.1:3065/v1'),
          onChange: e => setPluginDraft(d => ({ ...d, baseUrl: e.target.value }))
        }),
        h(Input, {
          value: pluginDraft.modelsUrl, placeholder: t('可选：GET 这个地址取 data[].id 当模型清单'),
          onChange: e => setPluginDraft(d => ({ ...d, modelsUrl: e.target.value }))
        }),
        h(Input, {
          value: pluginDraft.modelPrefix, placeholder: t('可选：界面上的模型前缀，发上游前会去掉，例如 a2a/'),
          onChange: e => setPluginDraft(d => ({ ...d, modelPrefix: e.target.value }))
        }),
        h(Input.Password, {
          value: pluginDraft.key,
          placeholder: pluginDraft.directory ? t('写入同目录 key.txt；留空表示不改') : t('写入同目录 key.txt，可留空'),
          onChange: e => setPluginDraft(d => ({ ...d, key: e.target.value }))
        }),
        pluginDraft.directory
          ? h(Muted, null, tf('清单声明了 {n} 个模型，这里不改 models 与工具/MCP 字段。', { n: pluginDraft.models || 0 }))
          : h(Muted, null, t('只建 provider 贡献，模型清单靠 ModelsUrl 自动发现。'))) : null),
    h(Modal, {
      open: propsModel != null, title: t('模型属性'), okText: t('确定'), cancelText: t('取消'),
      onOk: applyProps, onCancel: () => setPropsModel(null), width: 420
    },
      h('div', { style: { display: 'grid', gap: 10, paddingTop: 8 } },
        h(Muted, null, propsModel),
        h(Input, {
          value: propsDraft.name, placeholder: t('显示名称（留空沿用模型 id）'),
          onChange: e => setPropsDraft(p => ({ ...p, name: e.target.value }))
        }),
        h(Input, {
          value: String(propsDraft.contextLimit || ''), placeholder: t('上下文上限（token，0 表示自动）'),
          onChange: e => setPropsDraft(p => ({ ...p, contextLimit: parseInt(e.target.value, 10) || 0 }))
        }))));
}

function App() {
  const [caps, setCaps] = useState(null);
  const [models, setModels] = useState([]);
  const [images, setImages] = useState([]);
  const [recent, setRecent] = useState([]);
  const [sessions, setSessions] = useState([]);
  const [tabs, setTabs] = useState(() => {
    try { return JSON.parse(localStorage.getItem('openloop.tabs') || '[]'); } catch { return []; }
  });
  const [activeKey, setActiveKey] = useState('overview');
  const [snapshot, setSnapshot] = useState(null);
  const [project, setProject] = useState(null);
  const [shot, setShot] = useState(localStorage.getItem('openloop.shot') === '1');
  // 语言：setLang 只为了让整棵树用新的 LANG 重渲染
  const [, setLang] = useState('zh');
  const pickLanguage = value => setLang(applyLanguage(value));
  const [newOpen, setNewOpen] = useState(false);
  const [aboutOpen, setAboutOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [newWorkspace, setNewWorkspace] = useState('');
  const [newModel, setNewModel] = useState('');
  const [status, setStatus] = useState('');

  const stateRef = useRef({ activeKey, snapshot, sessions, shot, tabs });
  useEffect(() => { stateRef.current = { activeKey, snapshot, sessions, shot, tabs }; });
  useEffect(() => { localStorage.setItem('openloop.tabs', JSON.stringify(tabs)); }, [tabs]);

  const activeId = activeKey !== 'overview' && activeKey !== 'loop' ? activeKey : null;

  useEffect(() => {
    api('/api/settings').then(s => pickLanguage(s?.language)).catch(() => { /* 设置还没就绪就按系统的来 */ });
  }, []);

  const reloadSessions = async () => {
    try { setSessions(await api('/api/sessions') || []); } catch { /* A 未就绪 */ }
  };

  useEffect(() => {
    (async () => {
      try {
        const c = await api('/api/capabilities');
        setCaps(c);
        if (!c.worker) setActiveKey('loop');
        else {
          try {
            const m = await api('/api/models');
            setModels(m?.models || []);
            setImages(m?.images || []);
            setRecent(m?.recent || []);
          } catch { /* 模型目录可能未就绪 */ }
        }
      } catch (e) { setStatus(t('拿不到能力位：') + e.message); }
    })();
  }, []);

  const reloadModels = async () => {
    try {
      const m = await api('/api/models');
      setModels(m?.models || []);
      setImages(m?.images || []);
      setRecent(m?.recent || []);
    } catch { /* 目录重拉失败不挡操作 */ }
  };

  useEffect(() => {
    if (!caps?.worker) return;
    let alive = true;
    const tick = async () => {
      try {
        const rows = await api('/api/sessions');
        if (alive) setSessions(rows || []);
      } catch { /* 忽略瞬时失败 */ }
      if (alive) setTimeout(tick, 3000);
    };
    tick();
    return () => { alive = false; };
  }, [caps?.worker]);

  const openSession = async (id) => {
    if (!id) return;
    setTabs(prev => prev.includes(id) ? prev : [...prev, id]);
    setActiveKey(id);
    let data = null;
    const row = stateRef.current.sessions.find(s => s.id === id);
    if (!row || row.live !== false) {
      try { data = await api('/api/sessions/' + id); } catch { /* 只有索引里的历史会话 */ }
    }
    if (!data) {
      data = await post('/api/sessions', { id, workspace: row?.workspace || '', model: row?.model || '' });
    }
    setSnapshot(data);
    localStorage.setItem('openloop.session', id);
    if (data?.workspace) localStorage.setItem('openloop.workspace', data.workspace);
  };

  useEffect(() => {
    if (!activeId || !caps?.worker) return;
    let alive = true;
    const tick = async () => {
      try {
        const next = await api('/api/sessions/' + activeId);
        if (alive && next) setSnapshot(next);
      } catch {
        // 只有「索引里有、宿主里没有」才从磁盘恢复；会话被删掉就别再 POST 把他救回来
        const row = stateRef.current.sessions.find(s => s.id === activeId);
        if (!row) {
          if (alive) {
            setSnapshot(null);
            setTabs(prev => prev.filter(t => t !== activeId));
            setActiveKey('overview');
            setStatus(t('会话已删除：') + activeId);
          }
          return;
        }
        if (row.live !== false) { if (alive) setTimeout(tick, 800); return; }
        try {
          const again = await post('/api/sessions', { id: activeId, workspace: row.workspace || '', model: row.model || '' });
          if (alive && again) { setSnapshot(again); setStatus(t('已重连 ') + activeId); }
        } catch (e) { if (alive) setStatus(t('会话读取失败：') + e.message); }
      }
      if (alive) setTimeout(tick, 800);
    };
    tick();
    return () => { alive = false; };
  }, [activeId, caps?.worker]);

  useEffect(() => {
    let alive = true;
    const tick = async () => {
      const s = stateRef.current;
      try {
        await post('/api/puppet/state', {
          url: location.href,
          tab: s.activeKey,
          sessionId: s.activeKey === 'overview' || s.activeKey === 'loop' ? '' : s.activeKey,
          sessionCount: s.sessions.length,
          sessionState: s.snapshot?.state || '',
          prompts: (s.snapshot?.prompts || []).length,
          pending: (s.snapshot?.pending || []).length,
          width: window.innerWidth,
          height: window.innerHeight,
          dpr: window.devicePixelRatio || 1,
          captureEnabled: s.shot
        });
      } catch { /* 旧宿主没有钩子 */ }
      if (s.shot) {
        try {
          const pending = await api('/api/puppet/pending');
          if (pending) await post('/api/puppet/shot', { id: pending.id, png: await capturePage(pending) });
        } catch (e) { setStatus(t('Puppet 截图失败：') + e.message); }
      }
      if (alive) setTimeout(tick, 1000);
    };
    tick();
    return () => { alive = false; };
  }, []);

  const deleteSession = async (id) => {
    try {
      await api('/api/sessions/' + id, { method: 'DELETE' });
      setTabs(prev => prev.filter(t => t !== id));
      if (activeKey === id) { setActiveKey('overview'); setSnapshot(null); }
      await reloadSessions();
      message.success(t('已删除会话 ') + id);
    } catch (e) { message.error(t('删除失败：') + e.message); }
  };

  const createSession = async () => {
    if (!newWorkspace.trim()) return message.warning(t('先填工作目录'));
    try {
      const created = await post('/api/sessions', { workspace: newWorkspace.trim(), model: newModel.trim() });
      setNewOpen(false);
      setNewModel('');
      await reloadSessions();
      await openSession(created.id);
    } catch (e) { message.error(t('新建失败：') + e.message); }
  };

  const items = [
    { key: 'new', label: t('新建会话') },
    { key: 'list', label: t('会话列表') },
    { key: 'loop', label: 'Harness' },
    { type: 'divider' },
    { key: 'settings', label: t('设置') },
    { key: 'about', label: t('关于') }
  ];

  const menu = {
    items: caps?.worker ? items : items.filter(i => i.type === 'divider' || i.key === 'loop' || i.key === 'about'),
    onClick: ({ key }) => {
      if (key === 'new') {
        setNewWorkspace(project || caps?.workspace || '');
        setNewOpen(true);
      } else if (key === 'list') setActiveKey('overview');
      else if (key === 'loop') setActiveKey('loop');
      else if (key === 'settings') setSettingsOpen(true);
      else if (key === 'about') setAboutOpen(true);
    }
  };

  // 标签栏右键（与 WinForms 壳同一组动作，同一套分组）：活动 = 正在跑的会话
  // 分组：打开/关闭非活动 → 批量关闭 → 隔线，可关闭的 Tab 菜单没有针对自己的项（Web Tab 本身只承担切换）
  const isRunningTab = id => {
    const row = sessions.find(s => s.id === id);
    return (snapshot && snapshot.id === id ? snapshot.state : row?.state) === 'running';
  };
  const onTabMenu = key => {
    if (key === 'openactive') {
      const running = sessions.filter(s => s.state === 'running').map(s => s.id);
      if (running.length === 0) return message.info(t('没有运行中的会话'));
      setTabs(prev => {
        const next = [...prev];
        running.forEach(id => { if (!next.includes(id)) next.push(id); });
        return next;
      });
    } else if (key === 'closenactive') {
      if (sessions.length === 0) return message.warning(t('会话列表还没加载完'));
      const keep = tabs.filter(isRunningTab);
      setTabs(keep);
      if (!keep.includes(activeKey) && activeKey !== 'overview' && activeKey !== 'loop') setActiveKey('overview');
    } else if (key === 'closeworker') {
      setTabs([]);
      if (activeKey !== 'overview' && activeKey !== 'loop') setActiveKey('overview');
    }
  };
  const tabMenu = id => ({
    items: [
      ...(id ? [{ key: 'close', label: t('关闭') }, { type: 'divider' }] : []),
      { key: 'openactive', label: t('打开所有活动会话') },
      { type: 'divider' },
      { key: 'closenactive', label: t('关闭所有非活动会话') },
      { key: 'closeworker', label: t('关闭所有 Worker 会话') }
    ],
    onClick: ({ key }) => {
      if (key === 'close' && id) {
        setTabs(prev => prev.filter(x => x !== id));
        if (activeKey === id) { setActiveKey('overview'); setSnapshot(null); }
        return;
      }
      onTabMenu(key);
    }
  });
  const tabLabel = (label, id) => h(Dropdown, { trigger: ['contextMenu'], menu: tabMenu(id) }, label);

  const sessionTab = id => {
    const row = sessions.find(s => s.id === id);
    const state = (snapshot && snapshot.id === id ? snapshot.state : row?.state) || 'idle';
    return {
      key: id,
      label: tabLabel(h('span', { style: { display: 'inline-flex', alignItems: 'center', maxWidth: 200 } },
        h(Dot, { state }),
        h('span', { style: { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } },
          snapshot && snapshot.id === id ? (snapshot.title || id) : (row?.title || row?.model || id))), id)
    };
  };

  const tabItems = caps?.worker
    ? [{ key: 'overview', label: tabLabel(t('会话')), closable: false }, ...tabs.map(sessionTab)]
    : [];
  if (caps?.loop) tabItems.push({ key: 'loop', label: 'Harness', closable: false });

  let page;
  if (caps == null) page = h('div', { style: { padding: 40, textAlign: 'center' } }, h(Muted, null, status || t('正在连接…')));
  else if (activeKey === 'overview') {
    page = h(OverviewPage, {
      sessions, project, setProject, onOpen: openSession,
      onNew: () => { setNewWorkspace(project || caps?.workspace || ''); setNewOpen(true); },
      onDelete: deleteSession,
      onLoop: () => setActiveKey('loop'),
      caps
    });
  } else if (activeKey === 'loop') {
    page = h(LoopPage, { defaultWorkspace: caps?.workspace || '', shot, setShot });
  } else {
    page = h(SessionPage, {
      snapshot, models, recent, images, activeId, shot, setShot, reloadModels,
      onModel: (m, e) => post(`/api/sessions/${activeId}/model`, { model: m, effort: e }),
      onDelete: deleteSession
    });
  }

  return h(ConfigProvider, {
    theme: { token: { colorPrimary: '#1677ff', borderRadius: 8 } },
    button: { autoInsertSpace: false }
  },
    h('div', { style: { height: '100%', display: 'flex', flexDirection: 'column', background: '#f5f6f7' } },
      h(TopBar, { menu, onSettings: () => setSettingsOpen(true) }),
      h('div', { style: { flex: 'none', background: '#fff', padding: '0 12px' } },
        h(Tabs, {
          type: 'editable-card', size: 'small', hideAdd: true,
          activeKey, items: tabItems,
          onChange: key => { setActiveKey(key); if (key !== 'overview' && key !== 'loop') openSession(key); },
          onEdit: (key, action) => {
            if (action !== 'remove') return;
            setTabs(prev => prev.filter(t => t !== key));
            if (activeKey === key) setActiveKey('overview');
          },
          tabBarStyle: { margin: 0 }
        })),
      h('div', { style: { flex: 1, minHeight: 0, background: '#fff' } }, page),
      status ? h('div', { style: { position: 'fixed', right: 16, bottom: 8, fontSize: 12, color: '#8c8c8c' } }, status) : null,
      h(Modal, {
        open: newOpen, title: t('新建会话'), okText: t('创建'), cancelText: t('取消'),
        onOk: createSession, onCancel: () => setNewOpen(false)
      },
        h('div', { style: { display: 'grid', gap: 8 } },
          h(Input, { value: newWorkspace, onChange: e => setNewWorkspace(e.target.value), placeholder: t('工作目录（绝对路径）') }),
          h(Input, { value: newModel, onChange: e => setNewModel(e.target.value), placeholder: t('模型，如 opencode/xxx（留空用默认）') }))),
      h(Modal, {
        open: settingsOpen, title: t('设置'), width: 960, footer: null,
        onCancel: () => setSettingsOpen(false), destroyOnClose: true
      },
        h(SettingsPanel, { caps, onClose: () => setSettingsOpen(false), onLanguage: pickLanguage })),
      h(Modal, {
        open: aboutOpen, title: t('关于'), footer: null, onCancel: () => setAboutOpen(false)
      },
        h('div', { style: { display: 'grid', gap: 6 } },
          h('div', null, t('OpenLoop 浏览器壳')),
          h(Muted, null, t('角色：') + (caps?.worker ? 'Worker + Loop' : caps?.loop ? 'Loop' : '—')),
          h(Muted, null, t('默认工作区：') + (caps?.workspace || '—')),
          h(Muted, null, t('模型目录：') + (models.length ? models.length + t(' 个') : t('未就绪'))),
          h(Muted, null, t('会话：') + sessions.length + t(' 个')),
          h(PuppetRow, { shot, setShot })))));
}

try {
  ReactDOM.createRoot(document.getElementById('root')).render(h(App));
} catch (e) {
  document.title = 'ERR:' + (e && e.message ? e.message : e);
}
