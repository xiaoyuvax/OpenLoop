# 常见陷阱与自检

## 陷阱

### 1. 页面看不到工作区文件

LoopApp 的根目录是 `.openloop/tmp/LoopApps/<slug>/`，**只能读自己目录下的东西**。`fetch('../data.json')` 会失败。

需要数据：agent 写进 `<slug>/data.json`，页面 `fetch('./data.json')`。

### 2. 没有网络

`https://openui.local` 是本地目录映射。不要引 CDN、Google Fonts、外部图片、在线图标库——它们会一直转圈。

一切内联：字体用系统字体（CSS 已设好），图标用 `ui.icon()`，装饰用内联 SVG。

### 3. `ui.submit` 会唤醒 agent

它把数据作为一条用户消息放进会话队列。所以：

- 不要在 `mousemove`、`scroll`、`input` 里调；
- 提交后立刻 `disabled = true`，否则用户连点会起多轮；
- 只想记录操作轨迹用 `ui.emit()`。

### 4. `<form>` 会导航

`<form>` 的默认提交会让页面跳走。用 `button` + `onclick`，或 `onsubmit` 里 `e.preventDefault()`。

### 5. `location.href = ...` / `document.write` 毁上下文

agent 依赖「页面还在原文档」才能 `eval`。改内容用 `ui.mount` / `ui.patch`。

### 6. 重画整页会丢状态

`ui.mount('#app', ...)` 是清空重建——滚动位置、输入框内容、展开项全丢。频繁更新的区域（列表、计数）用 `ui.patch(sel, ...)` 只换那一块。

### 7. 硬编码颜色在浅色主题下崩

`style: { color: '#333' }` 在深色背景上等于看不见。只用 `var(--ui-*)`。

### 8. `ui.h` 的 props 顺序

```js
ui.h('div.card', { onclick }, '文本')        // 对
ui.h('div.card', '文本', { onclick })        // 错：props 必须在 children 前
```

只有一个 children 参数时可以直接给字符串/数组/元素。

### 9. 事件里的 `this`

`ui.h` 绑的是普通函数，`this` 是元素本身。用箭头函数捕获外层变量更稳。

### 10. 大列表卡顿

500 行一次性渲染会卡。用分页（`rows.slice(page * 50, page * 50 + 50)`）或只渲染可见窗口。

## 自检：agent 看不见画面怎么办

你只有 DOM 断言。在 `app.js` 末尾写：

```js
ui.ready(() => {
  render();
  // 结构断言：跑在渲染之后
  ui.assertVisible('#app');
  ui.assertNoOverflow('.table-wrap');
  ui.assertText('.card-title', '明细');
});
```

然后：

```
LoopApp(action="eval", js="JSON.stringify(ui.report())")
```

返回：

```json
{"pass":3,"total":3,"items":[
  {"label":"可见 #app","ok":true,"detail":"x=0 y=0 w=980 h=640"},
  {"label":"无溢出 .table-wrap","ok":true,"detail":"overflowY=0px"},
  {"label":"文本含 明细","ok":true,"detail":"text=明细"}
]}
```

`ok:false` 的条目带 `detail`，照它改。

### 常用断言

| 断言 | 查什么 | 典型失败原因 |
|---|---|---|
| `assertVisible(sel)` | 元素存在且有尺寸 | 忘了 `ui.mount`、选择器写错、父元素 `display:none` |
| `assertNoOverflow(sel)` | 内容没被裁 | 固定高度太小、表格行太多 |
| `assertText(sel, 'x')` | 文本已渲染 | 渲染函数没跑、数据为空 |

### 其它只读探针

```js
LoopApp(action="eval", js="document.body.scrollHeight")
LoopApp(action="eval", js="getComputedStyle(document.querySelector('.card')).padding")
LoopApp(action="eval", js="document.querySelectorAll('tbody tr').length")
```

`eval` 返回 WebView2 的 JSON 结果串（字符串带引号）。取对象用 `JSON.stringify(...)` 包一层。

## 迭代建议

- 视觉改动（间距、配色、布局）：一轮全改，再一次看效果。
- 交互改动：一轮只改一处，否则出错时定位不到。
- 用户说「太挤 / 太空 / 不好看」时，先改 `--ui-gap` 和 `padding`，别重构结构。
- 结构改到第三次还没满意，往往是用错了组件（该用列表却用了表格），换组件比调样式快。
