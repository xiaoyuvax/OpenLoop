# Quickstart：从零到第一个 LoopApp

## 1. 创建

```
LoopApp(action="create", name="订单核对")
```

返回：

```
目录：<workspace>/.openloop/tmp/LoopApps/订单核对/
入口：https://openui.local/订单核对/index.html
```

目录里已经有 `runtime/`（`openui.css`、`openui.js`、`icons.svg`），**不要重写它们**。

## 2. 写两个文件

`index.html`（照抄，只改 `<title>`）：

```html
<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>标题</title>
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
  ui.mount('#app', [ /* 页面结构 */ ]);
});
```

## 3. 打开

```
LoopApp(action="open", name="订单核对")
```

壳右侧「网页」页自动选中并显示。用户可点右上「独立窗口」搬成浮窗。

## 4. 迭代

```
WriteFile <目录>/app.js          ← 改逻辑
LoopApp(action="eval", js="location.reload()")
```

想只改一块、不丢状态：

```
LoopApp(action="eval", js="ui.patch('.list', ui.h('div.empty', {text:'没有数据'}))")
```

## 5. 接回会话

```js
ui.h('button.btn.primary', {
  text: '提交',
  onclick: async (e) => {
    e.target.disabled = true;
    try {
      await ui.submit({ picked: selected });
      ui.toast('已提交，稍等 agent 处理', 'ok');
    } catch (err) {
      ui.toast('提交失败：' + err.message, 'err');
      e.target.disabled = false;
    }
  }
})
```

提交后数据以 `{"picked":...}` 的形式出现在你会话的下一条用户消息里。

## 6. 查看 / 收尾

```
LoopApp(action="list")          列出本工作区所有 LoopApp
LoopApp(action="shot")          截一张给用户看
LoopApp(action="state")         当前开的哪个、dock 还是 float
```

不需要删——它们留在 `.openloop/tmp/LoopApps/` 下，下次还能复用。
