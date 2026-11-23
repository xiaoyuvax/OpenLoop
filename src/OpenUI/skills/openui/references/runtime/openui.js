/* OpenUI 运行时：极小工具集。零构建、零网络、无依赖。
   用法：<script src="./runtime/openui.js"></script>，页面里用全局 ui。 */
(function (global) {
  'use strict';

  var OPENUI_ORIGIN = 'https://openui.local';   // 桌面壳的虚拟域；Web 壳下为 http://127.0.0.1:<port>

  // ---- DOM 简写 ----

  function $(sel, root) { return (root || document).querySelector(sel); }
  function $$(sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); }

  /** 建元素：h('div.card', {onclick}, '文本', h('b', null, '粗')) */
  function h(tag, props, children) {
    var parts = String(tag).split(/(?=[.#])/);
    var el = document.createElement(parts[0] || 'div');
    for (var i = 1; i < parts.length; i++) {
      var p = parts[i];
      if (p[0] === '.') el.classList.add(p.slice(1));
      else if (p[0] === '#') el.id = p.slice(1);
    }
    if (props && (typeof props !== 'object' || props.nodeType || Array.isArray(props))) {
      children = props; props = null;
    }
    if (props) {
      Object.keys(props).forEach(function (k) {
        var v = props[k];
        if (v === null || v === undefined || v === false) return;
        if (k === 'class' || k === 'className') el.className += (el.className ? ' ' : '') + v;
        else if (k === 'style' && typeof v === 'object') Object.assign(el.style, v);
        else if (k === 'html') el.innerHTML = v;
        else if (k === 'text') el.textContent = v;
        else if (k === 'dataset') Object.assign(el.dataset, v);
        else if (k.slice(0, 2) === 'on' && typeof v === 'function') el.addEventListener(k.slice(2).toLowerCase(), v);
        else if (k in el && k !== 'list' && typeof v !== 'object') { try { el[k] = v; } catch (e) { el.setAttribute(k, v); } }
        else el.setAttribute(k, v);
      });
    }
    append(el, children);
    return el;
  }

  function append(el, children) {
    if (children === null || children === undefined || children === false) return;
    if (Array.isArray(children)) { children.forEach(function (c) { append(el, c); }); return; }
    el.appendChild(children.nodeType ? children : document.createTextNode(String(children)));
  }

  /** 全量替换容器内容。 */
  function mount(el, children) {
    if (typeof el === 'string') el = $(el);
    if (!el) return null;
    el.textContent = '';
    append(el, children);
    return el;
  }

  /** 按选择器局部替换：不丢页面其他状态（滚动、输入、已展开项）。 */
  function patch(sel, children) {
    var el = typeof sel === 'string' ? $(sel) : sel;
    return el ? mount(el, children) : null;
  }

  // ---- 状态 ----

  function state(initial) {
    var value = initial, watchers = [];
    return {
      get: function () { return value; },
      set: function (next) {
        var prev = value;
        value = typeof next === 'function' ? next(prev) : next;
        watchers.forEach(function (w) { try { w(value, prev); } catch (e) { console.error(e); } });
        return value;
      },
      on: function (fn) { watchers.push(fn); return function () { watchers = watchers.filter(function (w) { return w !== fn; }); }; }
    };
  }

  /** localStorage 持久化（键自动加 openui: 前缀）。 */
  function persist(key, fallback) {
    var full = 'openui:' + key;
    var s = state(read());
    function read() {
      try { var raw = localStorage.getItem(full); return raw === null ? fallback : JSON.parse(raw); } catch (e) { return fallback; }
    }
    return {
      get: s.get,
      set: function (next) {
        var v = s.set(next);
        try { localStorage.setItem(full, JSON.stringify(v)); } catch (e) { /* 隐私模式忽略 */ }
        return v;
      },
      on: s.on
    };
  }

  // ---- 与 OpenLoop 通信 ----

  var seq = 0, pending = new Map();

  function bridged() {
    return !!(global.chrome && chrome.webview && typeof chrome.webview.postMessage === 'function');
  }

  /**
   * 调一个会话级 action（SessionState / SessionSubmit / SessionStop / AppClose ...）。
   * 桌面壳走 WebView2 消息桥；Web 壳走 /t/<token>/api/<Action>。页面无法指定会话。
   */
  function call(action, args) {
    if (bridged()) {
      return new Promise(function (resolve, reject) {
        var id = 'r' + (++seq);
        pending.set(id, { resolve: resolve, reject: reject });
        chrome.webview.postMessage({ id: id, action: action, args: args || {} });
        setTimeout(function () {
          if (pending.has(id)) { pending.delete(id); reject(new Error('调用超时：' + action)); }
        }, 20000);
      });
    }
    var token = tokenFromPath();
    if (!token) return Promise.reject(new Error('不在 LoopApp 里运行，无法调用 ' + action));
    return fetch('/t/' + token + '/api/' + action, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(args || {})
    }).then(function (r) {
      return r.text().then(function (text) {
        var data = null;
        try { data = text ? JSON.parse(text) : null; } catch (e) { data = text; }
        if (!r.ok) throw new Error('HTTP ' + r.status + '：' + text);
        return data;
      });
    });
  }

  function tokenFromPath() {
    var m = /^\/t\/([A-Za-z0-9_-]+)\//.exec(location.pathname);
    return m ? m[1] : null;
  }

  // 桥回执：壳用 PostWebMessageAsJson 回 {id, ok, data, error}
  if (bridged()) {
    chrome.webview.addEventListener('message', function (e) {
      var msg = e.data;
      if (!msg || !msg.id || !pending.has(msg.id)) return;
      var p = pending.get(msg.id);
      pending.delete(msg.id);
      if (msg.ok) p.resolve(msg.data);
      else p.reject(new Error(msg.error || '调用失败'));
    });
  }

  /** 把页面数据作为一条用户消息交给 agent（唯一会唤醒会话的动作）。 */
  function submit(payload, text) {
    return call('SessionSubmit', { text: text || stringify(payload), tag: 'task' });
  }

  /** 只入队本地事件，等 agent 下次 drain 取走（不唤醒会话）。 */
  function emit(type, payload) {
    var box = global.__openui || (global.__openui = { events: [] });
    box.events.push({ type: type, payload: payload, at: new Date().toISOString() });
    return payload;
  }

  function drain() {
    var box = global.__openui || (global.__openui = { events: [] });
    var out = box.events.slice();
    box.events.length = 0;
    return out;
  }

  function stringify(v) {
    if (v === null || v === undefined) return '';
    if (typeof v === 'string') return v;
    try { return JSON.stringify(v, null, 0); } catch (e) { return String(v); }
  }

  // ---- 提示 ----

  function toastHost() {
    var el = $('.toast-host');
    if (!el) { el = h('div.toast-host'); document.body.appendChild(el); }
    return el;
  }

  function toast(message, kind, ms) {
    var el = h('div.toast' + (kind ? '.' + kind : ''), { text: String(message) });
    toastHost().appendChild(el);
    setTimeout(function () {
      el.style.transition = 'opacity .2s ease';
      el.style.opacity = '0';
      setTimeout(function () { el.remove(); }, 220);
    }, ms || 2600);
    return el;
  }

  /** 模态框：返回 { close }；buttons 省略时只有「关闭」。 */
  function modal(opts) {
    opts = opts || {};
    var mask = h('div.modal-mask');
    var box = h('div.modal');
    var body = h('div.body');
    mount(body, opts.body);
    var footer = h('div');
    var buttons = opts.buttons || [{ text: '关闭', primary: true }];
    buttons.forEach(function (b) {
      footer.appendChild(h('button.btn' + (b.primary ? '.primary' : '') + (b.danger ? '.danger' : ''), {
        text: b.text,
        onclick: function () {
          if (b.onClick) b.onClick(close);
          if (b.keepOpen !== true && !b.onClick) close();
          if (b.keepOpen !== true && b.onClick && b.closeOnClick !== false) close();
        }
      }));
    });
    if (opts.title) box.appendChild(h('header', { text: opts.title }));
    box.appendChild(body);
    if (buttons.length) box.appendChild(footer);
    mask.appendChild(box);
    mask.addEventListener('click', function (e) { if (e.target === mask) close(); });
    document.addEventListener('keydown', onKey);
    document.body.appendChild(mask);

    function onKey(e) { if (e.key === 'Escape') close(); }
    function close() {
      document.removeEventListener('keydown', onKey);
      mask.remove();
      if (opts.onClose) opts.onClose();
    }
    return { close: close, body: body };
  }

  function confirm(message, opts) {
    opts = opts || {};
    return new Promise(function (resolve) {
      modal({
        title: opts.title || '确认',
        body: h('p', { text: message }),
        buttons: [
          { text: opts.cancelText || '取消', onClick: function () { resolve(false); } },
          { text: opts.okText || '确定', primary: !opts.danger, danger: !!opts.danger, onClick: function () { resolve(true); } }
        ]
      });
    });
  }

  function copy(text) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      return navigator.clipboard.writeText(text).then(function () { toast('已复制', 'ok'); });
    }
    var ta = h('textarea', { value: text, style: { position: 'fixed', opacity: '0' } });
    document.body.appendChild(ta); ta.select();
    try { document.execCommand('copy'); toast('已复制', 'ok'); } catch (e) { toast('复制失败', 'err'); }
    ta.remove();
  }

  // ---- 格式化 ----

  var fmt = {
    num: function (v, digits) {
      if (v === null || v === undefined || isNaN(v)) return '—';
      return Number(v).toLocaleString('zh-CN', digits === undefined
        ? {} : { minimumFractionDigits: digits, maximumFractionDigits: digits });
    },
    pct: function (v, digits) { return (v === null || v === undefined) ? '—' : (Number(v) * 100).toFixed(digits === undefined ? 1 : digits) + '%'; },
    bytes: function (v) {
      if (!v) return '0 B';
      var units = ['B', 'KB', 'MB', 'GB', 'TB'], i = 0, n = Number(v);
      while (n >= 1024 && i < units.length - 1) { n /= 1024; i++; }
      return (i === 0 ? n : n.toFixed(1)) + ' ' + units[i];
    },
    dur: function (ms) {
      var s = Math.round(Number(ms) / 1000);
      if (s < 60) return s + ' 秒';
      var m = Math.floor(s / 60);
      if (m < 60) return m + ' 分 ' + (s % 60) + ' 秒';
      return Math.floor(m / 60) + ' 时 ' + (m % 60) + ' 分';
    },
    date: function (v) {
      if (!v) return '—';
      var d = new Date(v);
      if (isNaN(d.getTime())) return String(v);
      var p = function (n) { return n < 10 ? '0' + n : String(n); };
      return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate());
    },
    time: function (v) {
      if (!v) return '—';
      var d = new Date(v);
      if (isNaN(d.getTime())) return String(v);
      var p = function (n) { return n < 10 ? '0' + n : String(n); };
      return p(d.getHours()) + ':' + p(d.getMinutes()) + ':' + p(d.getSeconds());
    },
    ago: function (v) {
      var d = new Date(v).getTime();
      if (isNaN(d)) return '—';
      var s = Math.round((Date.now() - d) / 1000);
      if (s < 60) return '刚刚';
      if (s < 3600) return Math.floor(s / 60) + ' 分钟前';
      if (s < 86400) return Math.floor(s / 3600) + ' 小时前';
      return Math.floor(s / 86400) + ' 天前';
    },
    esc: function (s) {
      return String(s === null || s === undefined ? '' : s)
        .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }
  };

  // ---- 内联 SVG 图表（替掉图表库） ----

  var chart = {
    /** 迷你折线 / 面积图。values: number[] */
    spark: function (values, opts) {
      opts = opts || {};
      var w = opts.width || 120, hgt = opts.height || 28, pad = 2;
      var vals = (values || []).filter(function (v) { return typeof v === 'number' && isFinite(v); });
      if (vals.length < 2) return svg(w, hgt, '');
      var min = Math.min.apply(null, vals), max = Math.max.apply(null, vals);
      var span = (max - min) || 1;
      var step = (w - pad * 2) / (vals.length - 1);
      var pts = vals.map(function (v, i) {
        return [pad + i * step, hgt - pad - ((v - min) / span) * (hgt - pad * 2)];
      });
      var line = pts.map(function (p, i) { return (i ? 'L' : 'M') + p[0].toFixed(1) + ' ' + p[1].toFixed(1); }).join(' ');
      var color = opts.color ? 'stroke="' + opts.color + '"' : '';
      var area = opts.area === false ? '' :
        '<path d="' + line + ' L' + pts[pts.length - 1][0].toFixed(1) + ' ' + hgt + ' L' + pts[0][0].toFixed(1) + ' ' + hgt + ' Z" fill="currentColor" opacity=".12"/>';
      return svg(w, hgt,
        area + '<path d="' + line + '" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round" stroke-linecap="round" ' + color + '/>');
    },

    /** 竖向柱状图。values: number[] */
    bars: function (values, opts) {
      opts = opts || {};
      var w = opts.width || 240, hgt = opts.height || 80, gap = opts.gap === undefined ? 3 : opts.gap;
      var vals = (values || []).map(function (v) { return typeof v === 'number' && isFinite(v) ? v : 0; });
      if (!vals.length) return svg(w, hgt, '');
      var max = Math.max.apply(null, vals) || 1;
      var bw = (w - gap * (vals.length - 1)) / vals.length;
      var body = vals.map(function (v, i) {
        var bh = Math.max(1, (v / max) * hgt);
        return '<rect x="' + (i * (bw + gap)).toFixed(1) + '" y="' + (hgt - bh).toFixed(1) +
          '" width="' + bw.toFixed(1) + '" height="' + bh.toFixed(1) + '" rx="2" fill="currentColor" opacity="' +
          (opts.dim === false ? 0.9 : (0.45 + 0.5 * (v / max)).toFixed(2)) + '"/>';
      }).join('');
      return svg(w, hgt, body);
    },

    /** 环形进度。ratio: 0..1 */
    ring: function (ratio, opts) {
      opts = opts || {};
      var size = opts.size || 72, sw = opts.stroke || 6, r = (size - sw) / 2;
      var c = 2 * Math.PI * r;
      var v = Math.max(0, Math.min(1, Number(ratio) || 0));
      return svg(size, size,
        '<circle cx="' + size / 2 + '" cy="' + size / 2 + '" r="' + r + '" fill="none" stroke="currentColor" stroke-width="' + sw + '" opacity=".15"/>' +
        '<circle cx="' + size / 2 + '" cy="' + size / 2 + '" r="' + r + '" fill="none" stroke="currentColor" stroke-width="' + sw +
        '" stroke-dasharray="' + (c * v).toFixed(1) + ' ' + c.toFixed(1) + '" stroke-linecap="round" transform="rotate(-90 ' + size / 2 + ' ' + size / 2 + ')"/>');
    }
  };

  function svg(w, hgt, inner) {
    return '<svg width="' + w + '" height="' + hgt + '" viewBox="0 0 ' + w + ' ' + hgt + '" preserveAspectRatio="none">' + inner + '</svg>';
  }

  // ---- 图标 ----

  var ICONS = {
    check: '<polyline points="4 12 9 17 20 6"/>',
    x: '<line x1="5" y1="5" x2="19" y2="19"/><line x1="19" y1="5" x2="5" y2="19"/>',
    plus: '<line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>',
    minus: '<line x1="5" y1="12" x2="19" y2="12"/>',
    search: '<circle cx="11" cy="11" r="6"/><line x1="16" y1="16" x2="21" y2="21"/>',
    trash: '<polyline points="4 7 20 7"/><path d="M9 7V5h6v2"/><path d="M6 7l1 13h10l1-13"/>',
    edit: '<path d="M4 20h4L20 8l-4-4L4 16z"/>',
    save: '<path d="M5 4h11l3 3v13H5z"/><path d="M8 4v6h8V4"/>',
    download: '<path d="M12 4v11"/><polyline points="7 11 12 16 17 11"/><path d="M5 20h14"/>',
    upload: '<path d="M12 20V9"/><polyline points="7 13 12 8 17 13"/><path d="M5 4h14"/>',
    refresh: '<path d="M20 12a8 8 0 1 1-2.3-5.6"/><polyline points="20 4 20 9 15 9"/>',
    play: '<polygon points="7 4 20 12 7 20"/>',
    pause: '<rect x="7" y="5" width="4" height="14"/><rect x="14" y="5" width="4" height="14"/>',
    stop: '<rect x="6" y="6" width="12" height="12" rx="1"/>',
    settings: '<circle cx="12" cy="12" r="3"/><path d="M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M18.4 5.6l-2.1 2.1M7.7 16.3l-2.1 2.1"/>',
    user: '<circle cx="12" cy="8" r="4"/><path d="M4 21c0-4 4-6 8-6s8 2 8 6"/>',
    file: '<path d="M6 3h8l4 4v14H6z"/><polyline points="14 3 14 8 19 8"/>',
    folder: '<path d="M3 6h7l2 2h9v11H3z"/>',
    chart: '<line x1="4" y1="20" x2="20" y2="20"/><rect x="6" y="11" width="3" height="9"/><rect x="11" y="7" width="3" height="13"/><rect x="16" y="13" width="3" height="7"/>',
    table: '<rect x="3" y="5" width="18" height="14" rx="1"/><line x1="3" y1="10" x2="21" y2="10"/><line x1="9" y1="10" x2="9" y2="19"/>',
    list: '<line x1="8" y1="6" x2="20" y2="6"/><line x1="8" y1="12" x2="20" y2="12"/><line x1="8" y1="18" x2="20" y2="18"/><circle cx="4.5" cy="6" r="1"/><circle cx="4.5" cy="12" r="1"/><circle cx="4.5" cy="18" r="1"/>',
    grid: '<rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/>',
    arrowRight: '<line x1="4" y1="12" x2="19" y2="12"/><polyline points="14 7 19 12 14 17"/>',
    arrowLeft: '<line x1="20" y1="12" x2="5" y2="12"/><polyline points="10 7 5 12 10 17"/>',
    chevronDown: '<polyline points="6 9 12 15 18 9"/>',
    chevronRight: '<polyline points="9 6 15 12 9 18"/>',
    info: '<circle cx="12" cy="12" r="9"/><line x1="12" y1="11" x2="12" y2="16"/><circle cx="12" cy="8" r=".8" fill="currentColor"/>',
    warn: '<path d="M12 4l9 16H3z"/><line x1="12" y1="10" x2="12" y2="14"/><circle cx="12" cy="17" r=".8" fill="currentColor"/>',
    error: '<circle cx="12" cy="12" r="9"/><line x1="8" y1="8" x2="16" y2="16"/><line x1="16" y1="8" x2="8" y2="16"/>',
    external: '<path d="M14 4h6v6"/><path d="M20 4l-9 9"/><path d="M18 14v5H5V6h5"/>',
    close: '<line x1="6" y1="6" x2="18" y2="18"/><line x1="18" y1="6" x2="6" y2="18"/>',
    filter: '<path d="M3 5h18l-7 8v6l-4 2v-8z"/>',
    sort: '<polyline points="8 5 8 19"/><polyline points="4 9 8 5 12 9"/><polyline points="16 19 16 5"/><polyline points="12 15 16 19 20 15"/>',
    send: '<path d="M4 12l16-8-6 16-3-6z"/>'
  };

  function icon(name, cls) {
    var body = ICONS[name] || ICONS.info;
    return '<svg class="icon' + (cls ? ' ' + cls : '') + '" viewBox="0 0 24 24">' + body + '</svg>';
  }

  // ---- 自检：agent 看不见画面，只能靠断言 ----

  var assertLog = [];

  function check(label, ok, detail) {
    assertLog.push({ label: label, ok: !!ok, detail: detail === undefined ? null : detail });
    return !!ok;
  }

  /** 断言元素存在且可见（有尺寸）。 */
  function assertVisible(sel, label) {
    var el = $(sel);
    var ok = !!el && el.getBoundingClientRect().width > 0 && el.getBoundingClientRect().height > 0;
    return check(label || ('可见 ' + sel), ok,
      el ? rectText(el) : '元素不存在');
  }

  /** 断言没有溢出：容器内容不超过其可视高度（常见「表格被裁」问题）。 */
  function assertNoOverflow(sel, label) {
    var el = $(sel);
    if (!el) return check(label || ('无溢出 ' + sel), false, '元素不存在');
    var over = el.scrollHeight - el.clientHeight;
    return check(label || ('无溢出 ' + sel), over <= 1, 'overflowY=' + over + 'px');
  }

  /** 断言文本包含。 */
  function assertText(sel, expect, label) {
    var el = $(sel);
    var text = el ? (el.textContent || '') : '';
    return check(label || ('文本含 ' + expect), text.indexOf(expect) >= 0, 'text=' + text.slice(0, 80));
  }

  function report() {
    return { pass: assertLog.filter(function (a) { return a.ok; }).length, total: assertLog.length, items: assertLog };
  }

  function rectText(el) {
    var r = el.getBoundingClientRect();
    return 'x=' + Math.round(r.x) + ' y=' + Math.round(r.y) + ' w=' + Math.round(r.width) + ' h=' + Math.round(r.height);
  }

  // ---- 杂项 ----

  function debounce(fn, ms) {
    var t;
    return function () {
      var args = arguments, self = this;
      clearTimeout(t);
      t = setTimeout(function () { fn.apply(self, args); }, ms || 200);
    };
  }

  function uid(prefix) {
    return (prefix || 'id') + '-' + Math.random().toString(36).slice(2, 9);
  }

  /** 页面就绪后回调（DOMContentLoaded 已过则立刻）。 */
  function ready(fn) {
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', fn);
    else fn();
  }

  global.ui = {
    // DOM
    $: $, $$: $$, h: h, mount: mount, patch: patch, ready: ready,
    // 状态
    state: state, persist: persist,
    // 通信
    call: call, submit: submit, emit: emit, drain: drain,
    // 反馈
    toast: toast, modal: modal, confirm: confirm, copy: copy,
    // 展示
    fmt: fmt, chart: chart, icon: icon,
    // 自检
    check: check, assertVisible: assertVisible, assertNoOverflow: assertNoOverflow,
    assertText: assertText, report: report,
    // 杂项
    debounce: debounce, uid: uid,
    // 环境
    bridged: bridged, origin: OPENUI_ORIGIN, token: tokenFromPath
  };
})(window);
