---
name: openui
description: 用 OpenUI 在壳的内嵌浏览器里即时生成图形界面（LoopApp）：需要表单、表格、看板、图表、对比、向导等原生对话框做不出的交互时用它；改一次就能立刻看到。
---

# OpenUI：即时生成界面

需要用户**填东西、挑东西、比对东西**，而 `AskUser` 那种一问一答撑不住时，用一个 LoopApp：单页 HTML+JS，在壳的内嵌浏览器里跑，改完立刻刷新可见。

## 什么时候用

| 场景 | 做法 |
|---|---|
| 一个明确的选择、一个短问题 | `AskUser`，不要开 LoopApp |
| 结构化输入（多字段表单、批量编辑） | LoopApp |
| 需要看数据（表格、图表、时间线） | LoopApp |
| 需要反复迭代视觉（「再紧凑点」「换个布局」） | LoopApp |
| 操作流程（向导、多步确认） | LoopApp |
| 只是回答 / 解释 | 直接说 |

判断标准：**如果用户要盯着它点十下以上，就用 LoopApp；只点一下，别用。**

## 五步

```
1. LoopApp(create, "订单核对")     → 建目录 + 复制运行时，返回路径
2. WriteFile index.html / app.js   → 只写结构 + 逻辑
3. LoopApp(open, "订单核对")       → 壳右侧弹出
4. 改 app.js → LoopApp(eval, "location.reload()") → 看效果
5. 用户点「提交」→ 数据作为一条消息进本会话 → 你下一轮读到
```

`create` 只在第一次用；后续迭代就是第 4 步。任务做完调 `LoopApp(close)` 收起页面（见下）。

## 停靠还是独立窗口

壳会按页面内容自己决定：小表单、单列列表留在右侧栏（竖版），表格列多、有宽栅格、一屏读不完的整页数据搬进独立窗口。你**不用管**，内容变窄了下次打开会自动回到侧栏。

要覆盖就在 `app.json` 里写：

```json
{ "window": "float", "size": [1200, 820] }
```

`dock` 强制留在侧栏，`float` 强制独立窗口；`size` 是独立窗口的尺寸（缺省 1100×800）。

## 收尾：close

页面 `ui.submit` 过、这一轮跑完，壳会自动收起网页页（隐藏页面 → 关掉独立窗口 → 收起「网页」标签），实例留着，闲置 30 分钟（账本 `limit.browser_idle`，按工作区可配）才销毁。你也可以在任务确实做完时直接 `LoopApp(close)`。

别把「用户还在看」的页面收掉：只有在确认这一轮就是它的任务、且不需要用户继续点时才 close。

## 最小可用应用

`index.html`：

```html
<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>订单核对</title>
<link rel="stylesheet" href="./runtime/openui.css">
</head>
<body class="ui">
<div class="page" id="app"></div>
<script src="./runtime/openui.js"></script>
<script src="./app.js"></script>
</body>
</html>
```

`app.js`：

```js
ui.ready(() => {
  ui.mount('#app', [
    ui.h('div.row.between', [
      ui.h('h1', { text: '订单核对' }),
      ui.h('span.badge.accent', { text: '3 项待确认' })
    ]),
    ui.h('div.card', [
      ui.h('div.card-title', { text: '明细' }),
      ui.h('div.table-wrap', [
        ui.h('table.table', [
          ui.h('thead', ui.h('tr', [ui.h('th', { text: '商品' }), ui.h('th.num', { text: '数量' })])),
          ui.h('tbody', rows.map(r => ui.h('tr', [
            ui.h('td', { text: r.name }),
            ui.h('td.num', { text: ui.fmt.num(r.qty) })
          ])))
        ])
      ])
    ]),
    ui.h('div.row.end', [
      ui.h('button.btn.primary', { text: '提交', onclick: submit })
    ])
  ]);
});

async function submit() {
  await ui.submit({ action: 'confirm', orderId: 'A-1029' });
  ui.toast('已提交', 'ok');
}
```

这就是一个完整应用。**没有构建、没有依赖、没有网络。**

## 运行时提供什么

- `ui.h(tag, props, children)` — 建元素。`tag` 支持 `'div.card.flat'`、`'#id'`；`props` 里 `style` 收对象、`on*` 收函数、`text`/`html` 设内容。
- `ui.mount(sel, children)` — 全量替换容器内容。
- `ui.patch(sel, children)` — 局部替换，不丢滚动/输入状态（微调用）。
- `ui.$(sel)` / `ui.$$(sel)` — querySelector 简写。
- `ui.state(initial)` — `{get, set, on}`，`set` 后自动通知。
- `ui.persist(key, fallback)` — 同 `state`，落 localStorage。
- `ui.toast(msg, kind)` — `kind` = `ok` / `err` / `warn`。
- `ui.modal({title, body, buttons})` / `ui.confirm(msg)` — 弹层，`confirm` 返回 Promise&lt;boolean&gt;。
- `ui.copy(text)` — 复制并提示。
- `ui.fmt.num|pct|bytes|dur|date|time|ago|esc` — 格式化。
- `ui.chart.spark(values)` / `.bars(values)` / `.ring(ratio)` — 内联 SVG，返回字符串，塞进 `html`。
- `ui.icon(name)` — 内联 SVG 图标，返回字符串。
- `ui.emit(type, payload)` / `ui.drain()` — 本地事件，不唤醒会话。
- `ui.submit(payload, text?)` — **唯一会唤醒 agent 的动作**。

CSS 里有：`page / row / stack / grid.c2..c4 / card / stat / btn(.primary .ghost .danger .sm .lg .block) / input / textarea / select / check / table(.compact) / table-wrap / badge(.ok .warn .err .accent) / seg / tabs / list / list-item / bar / empty / skeleton`。

## 通信只有两个方向

**页面 → agent**：

- `ui.submit(payload)` — 把数据作为一条用户消息放进本会话队列，agent 下一轮读到。用于「用户填完了」。
- `ui.emit(type, payload)` — 只攒在页面里；agent 调 `LoopApp(drain)` 取走。用于记录操作轨迹这类不必立刻唤醒的事。

**agent → 页面**：改文件 + `LoopApp(eval, "location.reload()")`。

## 视觉规范（跟着做就好看）

- 深色优先，颜色**只用 CSS 变量**：`--ui-bg / --ui-surface / --ui-text / --ui-muted / --ui-accent / --ui-ok / --ui-warn / --ui-err`。不要写 `#333` 这类硬编码色，浅色主题会崩。
- 间距用 4 的倍数；一屏内区块间距 `12px`，区块内 `6px`。
- 一页一个 `<h1>`；数字用 `.num`（等宽对齐）。
- 主操作只放一个 `.btn.primary`；破坏性操作用 `.danger` 并配 `ui.confirm`。
- 空列表必须给 `.empty`，别留白板。
- 装饰图形用内联 SVG，不要引入图片文件。

## 性能与体积

- 单文件 `app.js` 控制在 **400 行内**；超过就说明该拆成多文件（`app.js` + `views/*.js`，用 `<script type="module">`）。
- 列表超过 200 行不要一次渲染全部，用 `slice` 分页或虚拟滚动。
- 不要在 `oninput` 里重画整页；用 `ui.patch` 只换那一块。

## 陷阱

1. **别把整页 HTML 塞进 `eval`。** `eval` 只用来 `location.reload()` 或读状态；渲页面靠改文件。
2. **`ui.submit` 会唤醒 agent。** 用户点一次提交就会起一轮；别在 `mousemove`、滚动这类高频事件里调。重复提交要自己禁用按钮。
3. **页面看不到工作区文件。** 它只能读自己目录下的东西（`data.json`、`assets/`）。需要数据就由 agent 写进 `data.json`。
4. **没有网络。** 不要引 CDN、Google Fonts、外部图片；一切内联或放 `assets/`。
5. **`document.write` / `location.href=` 会毁掉页面上下文。** 用 `ui.mount` 重建。
6. **看不清自己画的东西。** agent 只有 DOM 断言：用 `ui.assertVisible(sel)`、`ui.assertNoOverflow(sel)`、`ui.assertText(sel, 'x')` 写进 `app.js` 末尾，再 `LoopApp(eval, "JSON.stringify(ui.report())")` 读结果。

## 迭代节奏

```
改 app.js（WriteFile 或局部替换）
→ LoopApp(eval, "location.reload()")
→ 需要确认结构：LoopApp(eval, "JSON.stringify(ui.report())")
→ 需要给用户看：LoopApp(shot)
```

视觉类改动（间距、颜色、布局）一轮改到位再看；交互类改动一轮只改一处，否则出错时定位不到。
