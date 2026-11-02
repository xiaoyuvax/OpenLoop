# Echo 插件（示例）

最小的 OpenLoop 插件：一个工具 + 一个提供商贡献，入口是 Node 子进程，协议是 stdio 一行 JSON。

## 安装

把整个目录拷到下面任一位置（同名插件工作区优先）：

- `<工作区>/.openloop/plugins/echo/`
- `%APPDATA%\OpenLoop\plugins\echo/`（Linux：`~/.config/OpenLoop/plugins/echo/`）

需要 Node ≥ 18 在 `PATH` 里。

## 工具

`Echo(text)` 原样返回文本。特殊值：

- `__crash__`：进程退出 1，用来验证「插件崩溃宿主不倒」；
- `__sleep__`：5 秒后才回，用来验证超时。

## 提供商

清单里的 `providers[0]` 是一个占位：把 `baseUrl` 改成本机的 OpenAI 兼容端点，
`models` 改成该端点 `GET /v1/models` 里的模型 id，`keyEnv` 指向存密钥的环境变量名即可，
OpenLoop 不需要改代码。dsh-our-free-model 的 standalone 网关默认监听
`http://127.0.0.1:18900/v1`，模型列表从 `/v1/models` 取。

## 协议

请求（宿主 → 插件）：

```json
{"jsonrpc":"2.0","id":1,"method":"tool.call","params":{"tool":"Echo","args":{"text":"hi"},"workspace":"/path"}}
```

成功应答：

```json
{"jsonrpc":"2.0","id":1,"result":{"ok":true,"data":"hi"}}
```

失败可以走 `result.ok=false` + `result.error`，也可以走 JSON-RPC 的 `error`。
输出里没有 `id` 的行会被宿主当日志，不会当成应答。
