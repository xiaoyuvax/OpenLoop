# ofm-standalone —— 把 OFM 当本地服务型 provider

`dsh-our-free-model`（简称 OFM）自带一个 standalone 服务端，**不需要 dsh**：
它自己打印 `/v1` 地址、自己管密钥、自己带管理页。这份清单就是让 OpenLoop 拉起它、
把它的模型当成普通 OpenAI 兼容 provider 用。

## 安装

1. 把本目录复制到 `<工作区>/.openloop/plugins/ofm/`（或 `%APPDATA%\OpenLoop\plugins\ofm\`）；
2. 把 OFM 仓库里的 `packages/standalone`（含它依赖的 `src/`）放成本目录下的 `cli.mjs` 与同级文件，
   或者把清单里的 `service.args` 改成你 clone 的绝对路径；
3. 启动后模型会以 `ofm/<模型 id>` 出现在提供商下拉里（`OFM` 分组）。

## 清单要点

| 字段 | 作用 |
|---|---|
| `service.command` / `args` | 拉起子进程；`{root}` 会替换成清单所在目录 |
| `service.urlPattern` | 从 stdout 每行里提取服务地址（只取协议+主机+端口） |
| `providers[].baseUrl: "auto"` | 用服务地址 + `/v1` |
| `providers[].modelsUrl: "auto/v1/models"` | 用服务地址拉模型清单（`auto/` 后面原样拼） |
| `providers[].modelPrefix: "ofm/"` | 界面上的前缀；发给上游时会去掉 |
| `providers[].keyFile` + `keyField` | 服务把 API Key 写在它自己的 `settings.json` 里，按字段取 |

## 已知边界

- 进程由**先用到它的那个 OpenLoop 进程**拉起（通常是壳），另一个进程（A）通过
  `runtime/plugin-ofm.json` 复用同一实例；插件自己用 `service.lock` 防止双开。
- 服务数据目录独立于 dsh（清单里用 `{root}/data`），所以 EAC 授权要在它的管理页里另做一次。
- OFM standalone 的模型集与 dsh 插件模式不同（例如没有 `kilo-auto/free`）；可用清单以
  `/v1/models` 为准。
