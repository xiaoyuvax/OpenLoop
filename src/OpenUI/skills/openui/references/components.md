# 组件速查

所有类名都在 `runtime/openui.css` 里。用 `ui.h('div.card', ...)` 直接拼，不需要记 HTML。

## 布局

| 类 | 作用 |
|---|---|
| `.page` | 内容容器，居中、最大 1080px、纵向 12px 间距。`.page.wide` 不限宽 |
| `.row` | 横排。加 `.between` 两端对齐、`.end` 靠右、`.wrap` 换行、`.top` 顶部对齐 |
| `.stack` | 纵排。`.tight` 6px、`.loose` 20px |
| `.grid.c2` / `.c3` / `.c4` / `.auto` | 等宽网格；`.auto` 按 200px 自适应 |
| `.grow` | 占满剩余宽度（配合 flex） |
| `.spacer` | 撑开空隙 |
| `.scroll` | 滚动容器（要配固定高度） |

两栏布局（左列表 + 右详情）：

```js
ui.h('div', { style: { display: 'grid', gridTemplateColumns: '260px minmax(0,1fr)', gap: '12px', height: '100%' } }, [
  ui.h('div.card.scroll', list),
  ui.h('div.card', detail)
])
```

## 卡片与统计

```js
ui.h('div.card', [
  ui.h('div.card-title', [ui.h('span', { text: '概览' }), ui.h('span.spacer'), ui.h('button.btn.sm', { text: '刷新' })]),
  ui.h('div')
])

// 一排数字
ui.h('div.grid.c3', metrics.map(m =>
  ui.h('div.card', ui.h('div.stat', [
    ui.h('div.k', { text: m.label }),
    ui.h('div.v', { text: ui.fmt.num(m.value) })
  ]))))
```

## 按钮

```js
ui.h('button.btn', { text: '普通', onclick: fn })
ui.h('button.btn.primary', { text: '主操作', onclick: fn })
ui.h('button.btn.ghost', { text: '次要' })
ui.h('button.btn.danger', { text: '删除' })
ui.h('button.btn.sm', { text: '小' })       // 行内操作
ui.h('button.btn.block', { text: '整行宽' })
```

禁用：`{ disabled: true }`。

## 表单

```js
// 单字段
ui.h('div.field', [
  ui.h('label', { text: '名称' }),
  ui.h('input.input', { placeholder: '输入…', value: v, oninput: e => v = e.target.value })
])

// 下拉
ui.h('select.select', { onchange: e => pick(e.target.value) },
  options.map(o => ui.h('option', { value: o.v, text: o.k, selected: o.v === cur })))

// 多行
ui.h('textarea.textarea', { rows: 4, placeholder: '备注…' })

// 复选
ui.h('label.check', [ui.h('input', { type: 'checkbox', checked: on, onchange: e => on = e.target.checked }), '启用'])

// 滑块
ui.h('input.range', { type: 'range', min: 0, max: 100, value: 40 })
```

表单不要用 `<form>` + `submit` 事件（会触发导航）；用 `button` + `onclick` 收集值。

## 表格

```js
ui.h('div.table-wrap', [
  ui.h('table.table', [
    ui.h('thead', ui.h('tr', [
      ui.h('th', { text: '名称' }),
      ui.h('th.num', { text: '金额' }),
      ui.h('th', { text: '' })
    ])),
    ui.h('tbody', rows.map(r => ui.h('tr', [
      ui.h('td', { text: r.name }),
      ui.h('td.num', { text: ui.fmt.num(r.amount, 2) }),
      ui.h('td', ui.h('button.btn.sm.ghost', { text: '编辑', onclick: () => edit(r) }))
    ])))
  ])
])
```

`.table-wrap` 提供边框与滚动、`th` 自动吸顶。密集数据加 `.table.compact`。

## 徽章 / 状态

```js
ui.h('span.badge', { text: '普通' })
ui.h('span.badge.ok', { text: '完成' })
ui.h('span.badge.warn', { text: '待定' })
ui.h('span.badge.err', { text: '失败' })
ui.h('span.badge.accent', { text: '新' })
```

## 切换

```js
// 分段控件
ui.h('div.seg', ['全部', '未完成', '已完成'].map((t, i) =>
  ui.h('button' + (i === tab ? '.active' : ''), { text: t, onclick: () => { tab = i; render(); } })))

// 下方标签页
ui.h('div.tabs', [...])
```

## 列表

```js
ui.h('div.card.pad0', ui.h('div.list', items.map(it =>
  ui.h('div.list-item', [
    ui.h('span.grow.ellipsis', { text: it.title }),
    ui.h('span.badge', { text: it.tag }),
    ui.h('button.btn.sm.ghost', { text: '×', onclick: () => remove(it) })
  ]))))
```

## 进度

```js
ui.h('div.bar', ui.h('i', { style: { width: '64%' } }))
ui.h('div', { html: ui.chart.ring(0.64, { size: 64 }) })
```

## 空态与骨架

```js
ui.h('div.empty', [
  ui.h('div', { html: ui.icon('folder', 'xl') }),
  ui.h('div', { text: '还没有数据' }),
  ui.h('button.btn.sm', { text: '新建', onclick: create })
])

ui.h('div.skeleton', { style: { height: '18px', width: '60%' } })
```

## 图表（内联 SVG，无依赖）

```js
ui.h('div', { html: ui.chart.spark([3,7,4,9,6,11], { width: 160, height: 36 }) })
ui.h('div', { html: ui.chart.bars([12,5,8,20]) })
ui.h('div', { html: ui.chart.ring(0.72, { size: 80, stroke: 8 }) })
```

颜色用 `color`（`currentColor` 继承），所以在父元素上设 `style: { color: 'var(--ui-accent)' }` 就会跟着变。

## 图标

```js
ui.h('button.btn', [ui.h('span', { html: ui.icon('download') }), '导出'])
```

可用名：`check x plus minus search trash edit save download upload refresh play pause stop settings user file folder chart table list grid arrowRight arrowLeft chevronDown chevronRight info warn error external close filter sort send`。

## Toast / 弹层

```js
ui.toast('已保存', 'ok')
ui.toast('失败了', 'err')

if (await ui.confirm('确定删除这 3 项？', { danger: true })) remove();

const m = ui.modal({
  title: '重命名',
  body: ui.h('input.input', { id: 'nm', value: old }),
  buttons: [
    { text: '取消' },
    { text: '保存', primary: true, onClick: () => rename(ui.$('#nm').value) }
  ]
});
```

## 状态与持久化

ui.state(initial) 返回 { get, set, on }：

```js
const s = ui.state(0);
s.set(1);                 // 也可以 set(v => v + 1)
const off = s.on(v => console.log(v));
s.get();                  // 1
```

ui.persist(key, fallback) 形状**一样**，只是每次 set 顺带写 localStorage
（键自动加 openui: 前缀，隐私模式下静默跳过）：

```js
const store = ui.persist('draft', {});   // 注意：返回对象，不是函数
let data = store.get();                  // 读到 fallback 或上次的值
store.set({ a: 1 });                     // 写入并持久化
store.on(v => render(v));
```

**常见错法**：把 persist 当柯里化用——ui.persist('draft', {})() 会报
ui.persist(...) is not a function。它不返回函数，返回 {get,set,on}。

## 主题

`body.ui` 即可。默认浅色（elev-tuning 风格），深色自动跟随系统；要强制深色：

```html
<body class="ui" style="color-scheme: dark">
```
