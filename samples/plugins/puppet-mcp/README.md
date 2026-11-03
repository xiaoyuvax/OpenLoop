# Puppet MCP 接入

把别的项目（这里以 ChanTool 为例）通过 **Puppet 的 `/mcp`** 暴露的界面能力，变成 OpenLoop 的工具。
Agent 因此可以直接读界面状态、点按钮、调方法——不靠截图、不靠模拟键鼠。

## 前置

1. 目标应用内嵌了 Puppet（ChanTool 已内嵌）：启动后它会在 `127.0.0.1:19190` 监听 `/mcp`；
2. 该应用有全局密钥。Puppet 的密钥默认每个进程随机生成，只有进程自己知道；
   ChanTool 支持用环境变量注入：启动前设 `PUPPET_CHANTOOL_KEY=<key>`，它会接管这个值。

## 安装与配置

本目录复制到 `%APPDATA%\OpenLoop\plugins\puppet-mcp/`（或 `<工作区>/.openloop/plugins/`），
按实际情况改三处：

- `mcp.url`：目标应用的端口（ChanTool 默认 `19190`，被占时看它的启动日志）；
- `mcp.keyEnv`：存密钥的环境变量名（这里沿用 `PUPPET_CHANTOOL_KEY`）；
  也可以换成 `keyFile` 指向密钥文件，写法同 provider 贡献；
- `mcp.timeoutMs`：单次调用的超时。

**A（Worker 进程）必须能读到那个环境变量**：shell 启动时带上，A/I 会继承。

## 发生了什么

- 每次 Worker 开始跑之前，OpenLoop 会 `POST <url>` 发 `initialize` 与 `tools/list`，
  把服务端返回的 `inputSchema` 压成一行参数说明，注册成普通工具；
- 模型调工具时走 `tools/call`，返回的 `content[].text` 原样进工具结果；
- 服务端不可达时只是「没有这些工具」，不会让会话失败（日志里有一条 warn）。

Puppet 侧固定提供 `puppet_registry` / `puppet_describe` / `puppet_get` / `puppet_usage` / `puppet_logs`，
外加按控件与公开方法动态合成的调用工具。

## 边界

- 只支持 Streamable HTTP（POST JSON-RPC，无会话头、无 SSE 长连接）；
  stdio 型 MCP（如 filesystem）还没做，需要时补一层传输。
- MCP 工具的只读性未知，一律按可写处理（会影响「只读模式」下的可用性）。
- 目标应用没启动 → 工具列表为空，不是错误。
