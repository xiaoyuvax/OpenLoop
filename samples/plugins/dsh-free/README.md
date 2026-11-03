# dsh 免费模型接入

把本机 dsh（DeepSeek Harness）里装的 `dsh-our-free-model` 插件暴露的 OpenAI 兼容网关，
当成 OpenLoop 的一个 provider 贡献。A（Worker）和 I（Loop）都能直接选里面的模型。

## 前置

1. 本机装好 dsh，且 `web`（或任意带网关的 profile）装有 `dsh-our-free-model` 插件；
2. dsh 正在运行 —— 网关是 dsh 进程内的一个 channel，dsh 不开就没有端口：

   ```
   dsh web --no-open
   ```

3. 网关默认监听 `127.0.0.1:8326`，密钥由 dsh 自己生成在 `~/.dsh/openai-gateway/api-key`。
   若用 `DSH_OPENAI_GATEWAY_PORT` 改过端口，同步改本目录 `plugin.json` 的两个 URL。

## 安装

把本目录复制到下面任一处（全局装一次，A 与 I 都看得到）：

- `%APPDATA%\OpenLoop\plugins\dsh-free/`（Linux：`~/.config/OpenLoop/plugins/dsh-free/`）
- `<工作区>/.openloop/plugins/dsh-free/`（只对该工作区生效）

不用重启：每次新建会话/演进轮都会重读清单。模型目录（`/v1/models`）有 5 分钟缓存。

## 用法

模型 id 就是网关返回的 id，带 `our-free-model/` 前缀，例如：

- `our-free-model/deepseek-v4.1-flash-free`
- `our-free-model/glm-5.3-flash-free`
- `our-free-model/combo:recommended-free`

WinForms 的模型下拉里会多出这些条目（标为免费）；Web 界面在模型框里直接填。

## 为什么用 `modelsUrl` 而不是把 id 写死

dsh 的模型清单随上游（OpenCode Zen / Kilo）刷新，写死在清单里过几天就对不上。
本插件让 OpenLoop 每次启动/刷新时问一次网关的 `/v1/models`，拿到的 id 直接可用，
dsh 换模型不用改配置。

## 边界

- 只读本地文件与 loopback，不联网转发；
- 网关需要 `Authorization: Bearer <api-key>`，密钥从 `keyFile` 读，不写进清单、不落日志；
- dsh 没开时这些模型会报连接失败——这是预期的，OpenLoop 不做自动拉起（拉起 dsh 是另一件事）。
