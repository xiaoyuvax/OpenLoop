# OpenUI

agent 即时生成的 Web 界面框架。产物叫 **LoopApp**：一个自包含的单页应用，跑在 OpenLoop 壳的内嵌浏览器里，可以即时改、即时看、把用户操作送回会话。

设计文档：`docs/DESIGN-OPENUI.md`（仓库根）。

## 目录

```
skills/openui/SKILL.md                 技能正文（agent 读它）
skills/openui/references/runtime/      经验性资产：css / js / icons（create 时整目录复制）
skills/openui/references/*.md          按需读的手册：quickstart / components / pitfalls
```

运行时放在技能目录里是刻意设计：**装技能 = 装框架**。`LoopApp(create)` 从 `SkillInfo.Directory` 复制 `references/runtime/`，不依赖任何安装布局的路径解析。

## LoopApp 的形状

```
<workspace>/.openloop/tmp/LoopApps/<slug>/
  app.json      {name,title,window:"dock"|"float",size:[w,h]}
  index.html    结构，引 ./runtime/openui.css 与 ./app.js
  app.js        逻辑（agent 迭代时唯一要改的文件）
  data.json     可选数据
  runtime/      create 时复制进来，应用自包含
```

自包含而非跨域共享：多占 ~34KB 磁盘，换掉一整类 origin 问题，应用还能整目录搬走、用系统浏览器直接打开。

## 页面能力

```js
ui.call("SessionSubmit", { text })   // → 会话队列，agent 下一轮看到
ui.emit("picked", { id })            // → 攒着，agent 用 LoopApp(drain) 取
```

`SessionSubmit` 是唯一会让 agent 动起来的动作。它没有会话参数——桥绑定的那个浏览器属于哪个会话，消息就进哪个会话。

## 验收（P0）

1. agent 从零生成一个带表格与表单的页面，`LoopApp(open)` 后壳右侧显示。
2. 用户填表点「提交」，数据作为一条用户消息进入该会话，触发下一轮。
3. 两个会话各开一个 LoopApp，A 的页面无论调什么 action，B 的 `busy/queueLen` 前后不变。
