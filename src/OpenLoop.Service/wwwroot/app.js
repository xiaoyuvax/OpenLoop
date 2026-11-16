// OpenLoop 浏览器壳：React + Ant Design 5（vendor 本地引入）。
// 布局对齐 WinForms：顶栏（菜单/标题/设置）→ 卡片标签页（会话 + 每个会话）→
// 会话页 = 头部（标题 + 选择/操作/上下文/侧栏）+ 聊天|侧栏 + 输入区（输入框 + 工具条）。
const { createElement: h, useEffect, useMemo, useRef, useState } = React;
const {
  ConfigProvider, Dropdown, Tabs, Button, Input, Select, Space, Typography, Tag,
  Progress, Modal, Switch, Segmented, Tooltip, Empty, Popconfirm, theme, message
} = antd;
const { Text } = Typography;

async function api(path, options = {}) {
  const res = await fetch(path, { headers: { 'Content-Type': 'application/json' }, ...options });
  const text = await res.text();
  if (!res.ok) {
    let msg = text;
    try { const j = JSON.parse(text); msg = j.error || j.message || text; } catch { /* 非 JSON 错误体 */ }
    throw new Error(msg || ('HTTP ' + res.status));
  }
  return text ? JSON.parse(text) : null;
}
const post = (path, body) => api(path, { method: 'POST', body: JSON.stringify(body ?? {}) });

function baseName(path) {
  const s = (path || '').replace(/[\\/]+$/, '');
  return s.split(/[\\/]/).pop() || s || '—';
}

const stateDot = state => ({
  running: '#1677ff', done: '#52c41a', interrupted: '#faad14', idle: '#bfbfbf'
}[state] || '#bfbfbf');

const Dot = ({ state }) => h('span', {
  style: {
    display: 'inline-block', width: 8, height: 8, borderRadius: 4,
    background: stateDot(state), marginRight: 6, flex: 'none'
  }
});

const Muted = ({ children, style }) => h(Text, { type: 'secondary', style: { fontSize: 12, ...style } }, children);

// Puppet 页面截图开关：只有人能勾（Puppet 自己关不掉），放「关于」里随时可达
const PuppetRow = ({ shot, setShot }) => h('div', { style: { display: 'flex', alignItems: 'center', gap: 8, marginTop: 12 } },
  h(Switch, {
    size: 'small', checked: shot,
    onChange: v => {
      setShot(v);
      localStorage.setItem('openloop.shot', v ? '1' : '0');
      message.success(v ? '已允许 Puppet 截图' : '已关闭 Puppet 截图');
    }
  }),
  h(Muted, null, '允许 Puppet 截图'));

// ---------------- 顶栏 ----------------

function TopBar({ menu, onSettings }) {
  return h('div', {
    style: {
      height: 54, flex: 'none', display: 'flex', alignItems: 'center', gap: 10,
      padding: '0 10px', background: '#fff', borderBottom: '1px solid #f0f0f0'
    }
  },
    h(Dropdown, { menu, trigger: ['click'] },
      h(Button, { type: 'text', style: { width: 38, height: 34, borderRadius: 8 } }, '☰')),
    h('div', { style: { display: 'flex', alignItems: 'baseline', gap: 10, minWidth: 0 } },
      h('span', { style: { fontSize: 17, fontWeight: 700 } }, 'OpenLoop'),
      h('span', { style: { fontSize: 12, color: '#8c8c8c' } }, '多会话 · 关闭标签不停止')),
    h('div', { style: { flex: 1 } }),
    h(Tooltip, { title: '设置' },
      h(Button, { type: 'text', style: { width: 38, height: 34, borderRadius: 8 }, onClick: onSettings }, '⚙')));
}

// ---------------- 会话列表（Tab0） ----------------

function OverviewPage({ sessions, project, setProject, onOpen, onNew, onDelete, onLoop, caps }) {
  const [q, setQ] = useState('');
  const [picked, setPicked] = useState([]);

  const projects = useMemo(() => {
    const map = new Map();
    for (const s of sessions) {
      const ws = s.workspace || '';
      const p = map.get(ws) || { ws, name: baseName(ws), count: 0 };
      p.count++;
      map.set(ws, p);
    }
    return [...map.values()].sort((a, b) => a.name.localeCompare(b.name));
  }, [sessions]);

  const rows = useMemo(() => {
    const key = q.trim().toLowerCase();
    return sessions.filter(s =>
      (project == null || (s.workspace || '') === project) &&
      (!key || (s.title || '').toLowerCase().includes(key) || (s.id || '').toLowerCase().includes(key)));
  }, [sessions, project, q]);

  const allPicked = rows.length > 0 && rows.every(r => picked.includes(r.id));
  const toggle = id => setPicked(p => p.includes(id) ? p.filter(x => x !== id) : [...p, id]);

  const removePicked = async () => {
    for (const id of picked) await onDelete(id);
    setPicked([]);
  };

  return h('div', { style: { display: 'flex', height: '100%', minHeight: 0 } },
    h('div', {
      style: {
        width: 272, flex: 'none', borderRight: '1px solid #f0f0f0', background: '#fff',
        display: 'flex', flexDirection: 'column', minHeight: 0
      }
    },
      h('div', { style: { padding: '10px 12px 6px', fontSize: 12, color: '#8c8c8c' } }, '项目'),
      h('div', { className: 'ol-scroll', style: { flex: 1, minHeight: 0, padding: '0 8px' } },
        projects.map(p => h('div', {
          key: p.ws || '∅',
          className: 'ol-item' + (project === p.ws ? ' active' : ''),
          onClick: () => setProject(p.ws),
          title: p.ws || '（空工作区）'
        },
          h('span', { className: 'ol-avatar' }, (p.name || '?').slice(0, 1).toUpperCase()),
          h('span', { style: { flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } }, p.name),
          h(Muted, null, p.count)))),
      h('div', { style: { padding: 10, borderTop: '1px solid #f0f0f0', display: 'grid', gap: 8 } },
        h(Button, { block: true, onClick: onLoop }, 'Loop'),
        h(Button, { block: true, onClick: onNew }, '新建会话'))),

    h('div', { style: { flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', minHeight: 0 } },
      h('div', { style: { display: 'flex', gap: 8, padding: '10px 16px' } },
        h(Input, {
          value: q, onChange: e => setQ(e.target.value), allowClear: true,
          placeholder: '搜索会话', style: { maxWidth: 480 }
        }),
        h('div', { style: { flex: 1 } }),
        picked.length
          ? h(Popconfirm, {
              title: '删除选中的 ' + picked.length + ' 个会话？',
              description: '对话历史会一起删除，不可恢复。',
              okText: '删除', cancelText: '取消', okButtonProps: { danger: true },
              onConfirm: removePicked
            }, h(Button, { danger: true }, '删除选中'))
          : h(Button, { disabled: true }, '删除选中'),
        h(Button, { type: 'primary', onClick: onNew }, '新建会话')),
      h('div', { style: { padding: '0 16px 8px' } },
        h(Muted, null, project || '全部项目')),
      h('div', { className: 'ol-scroll', style: { flex: 1, minHeight: 0, padding: '0 16px 12px' } },
        rows.length === 0
          ? h(Empty, { image: Empty.PRESENTED_IMAGE_SIMPLE, description: '暂无会话' })
          : h('div', { className: 'ol-table' },
            h('div', { className: 'ol-thead' },
              h('input', {
                type: 'checkbox', checked: allPicked,
                onChange: () => setPicked(allPicked ? [] : rows.map(r => r.id))
              }),
              h('span', { style: { flex: 1 } }, '会话'),
              h('span', { style: { width: 90 } }, '状态'),
              h('span', { style: { width: 220 } }, '工作区')),
            rows.map(s => h('div', {
              key: s.id, className: 'ol-trow', onClick: () => onOpen(s.id)
            },
              h('input', {
                type: 'checkbox', checked: picked.includes(s.id),
                onClick: e => e.stopPropagation(), onChange: () => toggle(s.id)
              }),
              h('span', { style: { flex: 1, minWidth: 0, display: 'flex', alignItems: 'center' } },
                h(Dot, { state: s.state }),
                h('span', { style: { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } },
                  s.title || baseName(s.workspace) || s.id),
                s.live === false ? h(Tag, { style: { marginLeft: 8 } }, '历史') : null),
              h('span', { style: { width: 90 } }, h(Muted, null, s.state || 'idle')),
              h('span', { style: { width: 220, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } },
                h(Muted, null, s.workspace || '—'))))))));
}

// ---------------- 会话页 ----------------

function Turn({ turn }) {
  return h('div', { className: 'ol-turn' },
    turn.user ? h('div', null,
      h(Muted, null, '你'),
      h('div', { className: 'ol-body' }, turn.user)) : null,
    h('div', { style: turn.user ? { marginTop: 10 } : null },
      h(Muted, null, 'OpenLoop' + (turn.streaming ? ' · 输出中…' : '')),
      h('div', { className: 'ol-body' }, turn.assistant || ''),
      turn.reasoning ? h('details', { className: 'ol-details' },
        h('summary', null, '思考'),
        h('pre', null, turn.reasoning)) : null,
      (turn.timeline || turn.toolLines) ? h('pre', { className: 'ol-tools' }, turn.timeline || turn.toolLines) : null));
}

function SessionPage({ snapshot, models, activeId, shot, setShot, onModel, onDelete }) {
  const [text, setText] = useState('');
  const [mode, setMode] = useState('task');
  const [model, setModel] = useState('');
  const [effort, setEffort] = useState('');
  const [freeOnly, setFreeOnly] = useState(true);
  const [provider, setProvider] = useState('');
  const [sideOpen, setSideOpen] = useState(true);
  const [sideTab, setSideTab] = useState('detail');
  const [selectMode, setSelectMode] = useState(false);
  const chatRef = useRef(null);

  useEffect(() => {
    setModel(snapshot?.model || '');
    setEffort(snapshot?.effort || '');
  }, [snapshot?.id, snapshot?.model, snapshot?.effort]);

  useEffect(() => {
    const el = chatRef.current;
    if (!el) return;
    if (el.scrollTop + el.clientHeight >= el.scrollHeight - 160) el.scrollTop = el.scrollHeight;
  }, [snapshot]);

  const providers = useMemo(() => {
    const set = new Set();
    for (const m of models) {
      const slash = m.id.indexOf('/');
      if (slash > 0) set.add(m.id.slice(0, slash));
    }
    return [...set].sort();
  }, [models]);

  const choices = useMemo(() => models
    .filter(m => (!freeOnly || m.free) && (!provider || m.id.startsWith(provider + '/')))
    .map(m => ({ value: m.id, label: m.name })), [models, freeOnly, provider]);

  const running = snapshot?.state === 'running';
  const value = text.trim();
  const lastTurn = (snapshot?.turns || [])[(snapshot?.turns || []).length - 1];
  const retry = !running && !value && !!lastTurn && lastTurn.failed
    && /provider\.free-limit|\b429\b|\b402\b|rate limit|too many requests|quota|usage limit|insufficient|credits|payment required|额度|余额/i.test(lastTurn.assistant || '');
  const pct = snapshot?.contextLimit > 0 && snapshot?.contextTokens != null
    ? Math.min(100, Math.round(snapshot.contextTokens / snapshot.contextLimit * 100)) : 0;

  const send = async () => {
    if (!activeId) return;
    if (!value) {
      if (retry) {
        try { await post(`/api/sessions/${activeId}/messages`, { text: '继续', tag: 'task' }); }
        catch (e) { message.error('发送失败：' + e.message); }
        return;
      }
      if (!running) return message.warning('输入框为空');
      try { await post(`/api/sessions/${activeId}/stop`); } catch (e) { message.error('停止失败：' + e.message); }
      return;
    }
    setText('');
    try {
      await post(`/api/sessions/${activeId}/messages`, { text: value, tag: mode });
      if (running) message.success(mode === 'task' ? '已入队，本轮结束后发送' : '已入队');
    } catch (e) { setText(value); message.error('发送失败：' + e.message); }
  };

  const act = async (action, body, ok) => {
    if (!activeId) return;
    try { await post(`/api/sessions/${activeId}/${action}`, body); if (ok) message.success(ok); }
    catch (e) { message.error('失败：' + e.message); }
  };

  const pickModel = async (id, nextEffort) => {
    setModel(id);
    if (nextEffort !== undefined) setEffort(nextEffort);
    if (!activeId) return;
    try {
      await post(`/api/sessions/${activeId}/model`, { model: id, effort: nextEffort !== undefined ? nextEffort : effort });
      message.success('已切换模型');
    } catch (e) { message.error('切换失败：' + e.message); }
  };

  const header = h('div', {
    style: {
      height: 44, flex: 'none', display: 'flex', alignItems: 'center', gap: 10,
      padding: '0 16px', borderBottom: '1px solid #f0f0f0', background: '#fff'
    }
  },
    h(Dot, { state: snapshot?.state }),
    h('span', { style: { fontWeight: 600, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } },
      snapshot?.title || snapshot?.id || '会话'),
    h('div', { style: { flex: 1 } }),
    h('label', { style: { display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: '#8c8c8c' } },
      h('input', { type: 'checkbox', checked: selectMode, onChange: e => setSelectMode(e.target.checked) }), '选择'),
    selectMode
      ? h(Popconfirm, {
          title: '删除这个会话？', description: '对话历史会一起删除，不可恢复。',
          okText: '删除', cancelText: '取消', okButtonProps: { danger: true },
          onConfirm: () => onDelete(snapshot?.id)
        }, h(Button, { size: 'small', danger: true }, '删除'))
      : h(Dropdown, {
          menu: {
            items: [
              { key: 'stop', label: '停止当前轮', disabled: !running },
              { key: 'interrupt', label: '打断（下一 step 前收尾）', disabled: !running },
              { key: 'resume', label: '继续', disabled: running },
              { type: 'divider' },
              { key: 'copy', label: '复制工作区' }
            ],
            onClick: ({ key }) => {
              if (key === 'copy') {
                navigator.clipboard?.writeText(snapshot?.workspace || '');
                return message.success('已复制工作区');
              }
              act(key, null, '已执行');
            }
          }
        }, h(Button, { size: 'small', type: 'text' }, '操作 ▾')),
    h(Tooltip, { title: '上下文占用：' + (snapshot?.contextTokens ?? '—') + ' / ' + (snapshot?.contextLimit || '—') },
      h(Progress, { type: 'circle', size: 26, percent: pct, showInfo: false, strokeWidth: 8 })),
    h(Muted, { style: { width: 34, textAlign: 'right' } }, pct + '%'),
    h(Button, { size: 'small', type: 'text', onClick: () => setSideOpen(v => !v) }, sideOpen ? '»' : '«'));

  const chat = h('div', { ref: chatRef, className: 'ol-scroll', style: { flex: 1, minHeight: 0, padding: '12px 20px' } },
    !snapshot
      ? h(Empty, { image: Empty.PRESENTED_IMAGE_SIMPLE, description: '暂无对话' })
      : (snapshot.turns || []).length === 0
        ? h(Empty, { image: Empty.PRESENTED_IMAGE_SIMPLE, description: '暂无对话' })
        : (snapshot.turns || []).map((t, i) => h(Turn, { key: i, turn: t })),
    (snapshot?.prompts || []).map(p => h('div', { key: p.id, className: 'ol-prompt' },
      h('div', { style: { flex: 1, minWidth: 0 } },
        h('div', { style: { fontWeight: 600, whiteSpace: 'pre-wrap' } },
          (p.category ? '[' + p.category + '] ' : '') + (p.tool ? p.tool + '：' : '') + (p.text || '')),
        p.reason ? h(Muted, null, p.reason) : null),
      h(Space, { size: 4, style: { flex: 'none' } },
        h(Button, { size: 'small', type: 'primary', onClick: () => act('approval', { id: p.id, decision: 1 }, '已允许') }, '允许一次'),
        h(Button, { size: 'small', onClick: () => act('approval', { id: p.id, decision: 2 }, '已允许同类') }, '同类都允许'),
        h(Button, { size: 'small', danger: true, onClick: () => act('approval', { id: p.id, decision: 0 }, '已拒绝') }, '拒绝')))));

  const pending = (snapshot?.pending || []).length
    ? h('div', { className: 'ol-pending' },
        h(Muted, { style: { flex: 'none' } }, '排队'),
        (snapshot.pending || []).map((item, i) => h('div', { key: i, className: 'ol-pending-row' },
          h('span', { style: { flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } }, item),
          h(Button, { size: 'small', type: 'link', onClick: () => act('pending', { index: i, action: 'now' }) }, '立即'),
          h(Button, { size: 'small', type: 'link', danger: true, onClick: () => act('pending', { index: i, action: 'remove' }) }, '撤回'))))
    : null;

  const toolbar = h('div', { style: { display: 'flex', alignItems: 'center', gap: 8, marginTop: 6, flexWrap: 'nowrap', minWidth: 0 } },
    h(Segmented, {
      size: 'small', value: mode, onChange: setMode,
      options: [{ label: '任务', value: 'task' }, { label: '声明判据', value: 'criterion' }, { label: '这是纠正', value: 'correction' }]
    }),
    h(Muted, { style: { flex: '0 1 auto', minWidth: 0, maxWidth: 220, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } },
      '📁 ' + (snapshot?.workspace || '—')),
    h('div', { style: { flex: 1, minWidth: 8 } }),
    h('span', { style: { display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: '#8c8c8c' } },
      '只看免费',
      h(Switch, { size: 'small', checked: freeOnly, onChange: setFreeOnly })),
    providers.length
      ? h(Select, {
          value: provider || '', onChange: setProvider, style: { width: 140 },
          options: [{ value: '', label: '全部提供商' }, ...providers.map(p => ({ value: p, label: p }))]
        }) : null,
    models.length
      ? h(Select, {
          value: model || undefined, onChange: id => pickModel(id), style: { width: 280 },
          showSearch: true, optionFilterProp: 'label', placeholder: '模型',
          options: choices.length ? choices : [{ value: model, label: model }].filter(o => o.value)
        })
      : h(Input, { value: model, onChange: e => setModel(e.target.value), onBlur: () => pickModel(model), placeholder: '模型', style: { width: 280 } }),
    h(Select, {
      value: effort || '', onChange: v => pickModel(model, v), style: { width: 110 },
      options: [{ value: '', label: '努力 Auto' }, { value: 'low', label: 'Low' },
        { value: 'medium', label: 'Medium' }, { value: 'high', label: 'High' }]
    }),
    h(Button, {
      type: 'primary', style: { width: 96 },
      danger: running && !value, onClick: send
    }, running ? (value ? '排队发送' : '停止') : retry ? '继续' : '发送'));

  const composer = h('div', { style: { flex: 'none', padding: '4px 16px 8px', borderTop: pending ? '0' : '1px solid #f0f0f0' } },
    h(Input.TextArea, {
      value: text, onChange: e => setText(e.target.value), autoSize: { minRows: 3, maxRows: 10 },
      placeholder: '描述任务，或声明判据 / 纠正（Enter 发送，Shift+Enter 换行）',
      onKeyDown: e => {
        // 与 WinForms 一致：Enter 即时发送，Shift+Enter 软换行；输入法组字中的 Enter 不算
        if (e.key !== 'Enter' || e.shiftKey || e.nativeEvent?.isComposing) return;
        e.preventDefault();
        send();
      }
    }),
    toolbar);

  const loopSide = h('div', { style: { padding: 12, display: 'grid', gap: 8 } },
    h(Button, { size: 'small' }, '待答 ' + ((snapshot?.prompts || []).length)),
    h(Muted, null, 'def: —'),
    h(Muted, null, '考核点: —'),
    h(Muted, { style: { wordBreak: 'break-all' } }, 'trace: ' + (snapshot?.traceDirectory || '—')),
    h(Muted, null, '版本: ' + (snapshot?.activationVersion
      ? 'v' + snapshot.activationVersion + ' · ' + snapshot.featureCount + ' 特征 · ' + snapshot.ruleCount + ' 规则' : '—')));

  const detailSide = h('div', { style: { padding: 12, display: 'grid', gap: 6 } },
    h(Muted, null, '模型'), h('div', { style: { wordBreak: 'break-all' } }, snapshot?.model || '—'),
    h(Muted, null, '努力'), h('div', null, snapshot?.effort || 'Auto'),
    h(Muted, null, '上下文'),
    h('div', null, (snapshot?.contextTokens != null ? snapshot.contextTokens.toLocaleString() : '—') + ' / '
      + (snapshot?.contextLimit > 0 ? snapshot.contextLimit.toLocaleString() : '—')),
    h(Muted, null, '成本'), h('div', null, snapshot?.cost != null ? snapshot.cost.toFixed(4) : '—'),
    h(Muted, null, '状态'), h('div', null, [snapshot?.state, snapshot?.outcome].filter(Boolean).join(' · ') || '—'),
    h(Muted, null, '工作区'), h('div', { style: { wordBreak: 'break-all' } }, snapshot?.workspace || '—'));

  const side = h('div', {
    style: {
      width: 372, flex: 'none', borderLeft: '1px solid #f0f0f0', background: '#fff',
      display: 'flex', flexDirection: 'column', minHeight: 0
    }
  },
    h('div', { style: { padding: 8, borderBottom: '1px solid #f0f0f0' } },
      h(Segmented, {
        block: true, size: 'small', value: sideTab, onChange: setSideTab,
        options: [{ label: 'Loop', value: 'loop' }, { label: '详情', value: 'detail' }, { label: '旁路', value: 'btw' }]
      })),
    h('div', { className: 'ol-scroll', style: { flex: 1, minHeight: 0 } },
      sideTab === 'loop' ? loopSide
        : sideTab === 'detail' ? detailSide
          : h('div', { style: { padding: 24 } }, h(Empty, { image: Empty.PRESENTED_IMAGE_SIMPLE, description: '旁路未接入浏览器' }))));

  return h('div', { style: { display: 'flex', flexDirection: 'column', height: '100%', minHeight: 0 } },
    header,
    h('div', { style: { flex: 1, minHeight: 0, display: 'flex' } },
      h('div', { style: { flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', minHeight: 0 } },
        chat, pending, composer),
      sideOpen ? side : null));
}

// ---------------- Loop ----------------

function LoopPage({ defaultWorkspace, shot, setShot }) {
  const [workspace, setWorkspace] = useState(localStorage.getItem('openloop.workspace') || defaultWorkspace || '');
  const [ledger, setLedger] = useState('');
  const [skills, setSkills] = useState([]);
  const [evolve, setEvolve] = useState(null);
  const [iteration, setIteration] = useState(null);
  const [plan, setPlan] = useState('');
  const [model, setModel] = useState('');
  const [ruleName, setRuleName] = useState('');
  const [ruleText, setRuleText] = useState('');
  const [shellIncluded, setShellIncluded] = useState(true);
  const [publishMode, setPublishMode] = useState('framework');
  const wsRef = useRef(workspace);
  useEffect(() => { wsRef.current = workspace; }, [workspace]);
  useEffect(() => { if (!workspace && defaultWorkspace) setWorkspace(defaultWorkspace); }, [defaultWorkspace]);
  useEffect(() => { localStorage.setItem('openloop.workspace', workspace); }, [workspace]);

  const refresh = async () => {
    const ws = wsRef.current.trim();
    if (!ws) { setLedger('先填工作目录'); setSkills([]); return; }
    try {
      const payload = await api('/api/loop/activation?workspace=' + encodeURIComponent(ws));
      if (payload?.activation) {
        const a = payload.activation;
        const rules = (a.features || []).filter(f => f.id && f.id.startsWith('rule.')).length;
        setLedger(`v${a.version} · ${(a.features || []).length} 特征 · ${rules} 规则`);
      } else setLedger('账本未初始化（先跑一次或新建会话）');
      setSkills(payload?.skills || []);
    } catch (e) { setLedger('读账本失败：' + e.message); setSkills([]); }
  };

  const pollEvolve = async () => {
    const ws = wsRef.current.trim();
    if (!ws) return;
    try {
      const s = await api('/api/loop/evolve?workspace=' + encodeURIComponent(ws));
      setEvolve(s);
      if (s?.state === 'running') setTimeout(pollEvolve, 2000);
      else refresh();
    } catch { /* 保持上次状态 */ }
  };

  const pollIteration = async () => {
    const ws = wsRef.current.trim();
    if (!ws) return;
    try {
      const s = await api('/api/loop/iteration?workspace=' + encodeURIComponent(ws));
      setIteration(s);
      if (s?.steps?.length) setPlan(s.steps.slice(-40).join('\n'));
      if (s?.state === 'running') setTimeout(pollIteration, 2000);
    } catch { /* 保持上次状态 */ }
  };

  useEffect(() => {
    if (!workspace.trim()) return;
    const timer = setTimeout(() => { refresh(); pollEvolve(); pollIteration(); }, 250);
    return () => clearTimeout(timer);
  }, [workspace]);

  const evolveText = useMemo(() => {
    if (!evolve) return '—';
    const parts = [evolve.state, evolve.phase];
    if (evolve.taskCount) parts.push(`题目 ${evolve.taskCount}`);
    if (evolve.state === 'done') parts.push(`候选 ${evolve.candidatePassed}/${evolve.taskCount}，基线 ${evolve.baselinePassed}/${evolve.taskCount}`);
    if (evolve.adopted) parts.push('已采纳 v' + evolve.candidateVersion);
    if (evolve.detail) parts.push(evolve.detail);
    if (evolve.error) parts.push(evolve.error);
    return parts.filter(Boolean).join(' · ');
  }, [evolve]);

  const iterationText = useMemo(() => {
    if (!iteration) return '—';
    const parts = [iteration.state];
    if (iteration.version) parts.push('v' + iteration.version);
    if (iteration.ok) parts.push('成功');
    if (iteration.rolledBack) parts.push('已回滚');
    if (iteration.error) parts.push(iteration.error);
    return parts.filter(Boolean).join(' · ');
  }, [iteration]);

  return h('div', { style: { display: 'flex', height: '100%', minHeight: 0 } },
    h('div', { className: 'ol-scroll', style: { flex: 1, minWidth: 0, padding: '14px 20px' } },
      h('div', { className: 'ol-section-title' }, '工作区'),
      h(Input, {
        value: workspace, onChange: e => setWorkspace(e.target.value),
        placeholder: '绝对路径',
        onPressEnter: () => { refresh(); pollEvolve(); pollIteration(); },
        onBlur: () => { refresh(); pollEvolve(); pollIteration(); }
      }),
      h(Muted, { style: { display: 'block', margin: '6px 0 16px' } }, ledger || '—'),

      h('div', { className: 'ol-section-title' }, '演进'),
      h(Space, { wrap: true, style: { marginBottom: 6 } },
        h(Input, { value: model, onChange: e => setModel(e.target.value), placeholder: '模型（留空用默认）', style: { width: 280 } }),
        h(Button, {
          type: 'primary', disabled: evolve?.state === 'running',
          onClick: async () => {
            const ws = workspace.trim();
            if (!ws) return message.warning('先填工作目录');
            try {
              setEvolve(await post('/api/loop/evolve', { workspace: ws, model: model.trim(), effort: '' }));
              setTimeout(pollEvolve, 2000);
            } catch (e) { message.error('演进启动失败：' + e.message); }
          }
        }, '演进'),
        h(Button, {
          disabled: evolve?.state !== 'running',
          onClick: () => post('/api/loop/evolve/stop', { workspace: workspace.trim() }).catch(e => message.error(e.message))
        }, '停止'),
        h('span', { style: { display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: '#8c8c8c' } },
          h(Switch, {
            size: 'small', checked: !!evolve?.autoEnabled,
            onChange: async enabled => {
              try {
                const r = await post('/api/loop/auto', { workspace: workspace.trim(), enabled });
                message.success(r.enabled ? '自动演进已开启' : '自动演进已关闭');
                pollEvolve();
              } catch (e) { message.error('设置失败：' + e.message); }
            }
          }), '自动演进')),
      h(Muted, { style: { display: 'block', marginBottom: 16 } }, evolveText),

      h('div', { className: 'ol-section-title' }, '规则与回滚'),
      h(Space, { wrap: true, style: { marginBottom: 16 } },
        h(Input, { value: ruleName, onChange: e => setRuleName(e.target.value), placeholder: '规则 id，如 rule.style', style: { width: 220 } }),
        h(Input, { value: ruleText, onChange: e => setRuleText(e.target.value), placeholder: '规则内容', style: { width: 320 } }),
        h(Button, {
          onClick: async () => {
            const ws = workspace.trim();
            if (!ws || !ruleName.trim()) return message.warning('工作目录和规则 id 都要填');
            try {
              const r = await post('/api/loop/rule', { workspace: ws, name: ruleName.trim(), text: ruleText.trim() });
              setRuleName(''); setRuleText('');
              message.success('已登记 ' + r.id);
              refresh();
            } catch (e) { message.error('登记失败：' + e.message); }
          }
        }, '登记规则'),
        h(Popconfirm, {
          title: '回滚到上一版？', description: '会写成新版本，旧版本文件不动。',
          okText: '回滚', cancelText: '取消',
          onConfirm: async () => {
            try {
              const r = await post('/api/loop/rollback', { workspace: workspace.trim() });
              message.success('已回滚，当前 v' + r.version);
              refresh();
            } catch (e) { message.error('回滚失败：' + e.message); }
          }
        }, h(Button, null, '回滚上一版'))),

      h('div', { className: 'ol-section-title' }, '迭代'),
      h(Space, { wrap: true, style: { marginBottom: 6 } },
        h(Button, {
          type: 'primary', disabled: iteration?.state === 'running',
          onClick: async () => {
            const ws = workspace.trim();
            if (!ws) return message.warning('先填工作目录');
            try {
              setIteration(await post('/api/loop/iteration', { workspace: ws, shell: shellIncluded, publishMode }));
              setTimeout(pollIteration, 2000);
            } catch (e) { message.error('迭代启动失败：' + e.message); }
          }
        }, '开始迭代'),
        h(Button, {
          disabled: iteration?.state === 'running',
          onClick: async () => {
            const ws = workspace.trim();
            if (!ws) return message.warning('先填工作目录');
            try {
              const r = await api('/api/loop/iteration/plan?workspace=' + encodeURIComponent(ws));
              const lines = [];
              if (r.summary) lines.push(r.summary);
              for (const p of r.plan || []) lines.push(`${p.role} → ${p.target}${p.update === false ? '（跳过）' : ''}`);
              setPlan(lines.join('\n'));
            } catch (e) { message.error('生成计划失败：' + e.message); }
          }
        }, '生成计划'),
        h(Button, {
          disabled: iteration?.state !== 'running',
          onClick: () => post('/api/loop/iteration/stop', {}).catch(e => message.error(e.message))
        }, '停止'),
        h('span', { style: { display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: '#8c8c8c' } },
          h(Switch, { size: 'small', checked: shellIncluded, onChange: setShellIncluded }), '连壳'),
        h('span', { style: { display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: '#8c8c8c' } },
          '形态',
          h(Select, {
            size: 'small', style: { width: 200 }, value: publishMode, onChange: setPublishMode,
            disabled: iteration?.state === 'running',
            options: [
              { value: 'framework', label: '框架依赖（需装 .NET）' },
              { value: 'selfcontained', label: '自带框架' }
            ]
          }))),
      h(Muted, { style: { display: 'block', marginBottom: 8 } }, iterationText),
      plan ? h('pre', { className: 'ol-tools' }, plan) : null),

    h('div', {
      style: {
        width: 320, flex: 'none', borderLeft: '1px solid #f0f0f0', background: '#fff',
        padding: 12, display: 'flex', flexDirection: 'column', gap: 6, minHeight: 0
      }
    },
      h(Muted, null, '工作区'), h('div', { style: { wordBreak: 'break-all' } }, workspace || '—'),
      h(Muted, { style: { marginTop: 8 } }, '账本'), h('div', null, ledger || '—'),
      h(Muted, { style: { marginTop: 8 } }, '技能'), h('div', null, skills.length ? skills.length + ' 个启用' : '（无）'),
      skills.length ? h('pre', { className: 'ol-tools', style: { maxHeight: 240 } }, skills.join('\n')) : null));
}

// ---------------- Puppet 页面钩子 ----------------

async function capturePage(region) {
  const root = document.documentElement;
  const width = root.scrollWidth;
  const height = root.scrollHeight;
  const clone = root.cloneNode(true);
  const source = [root, ...root.querySelectorAll('*')];
  const target = [clone, ...clone.querySelectorAll('*')];
  for (let i = 0; i < source.length; i++) {
    if (target[i].tagName === 'SCRIPT') { target[i].remove(); continue; }
    const computed = getComputedStyle(source[i]);
    let css = '';
    for (const name of computed) css += name + ':' + computed.getPropertyValue(name) + ';';
    target[i].style.cssText = css;
    const node = target[i];
    const live = source[i];
    if (node instanceof HTMLInputElement) {
      if (node.type !== 'password') node.setAttribute('value', live.value);
      if (node.type === 'checkbox' || node.type === 'radio') node.toggleAttribute('checked', live.checked);
    } else if (node instanceof HTMLSelectElement) {
      node.querySelectorAll('option').forEach((option, index) =>
        option.toggleAttribute('selected', !!live.options[index] && live.options[index].selected));
    } else if (node instanceof HTMLTextAreaElement) {
      node.textContent = live.value;
    }
  }
  const hideBars = document.createElement('style');
  hideBars.textContent = '*{scrollbar-width:none!important} *::-webkit-scrollbar{display:none!important;width:0!important;height:0!important}';
  (clone.querySelector('head') || clone).appendChild(hideBars);
  const svg = '<svg xmlns="http://www.w3.org/2000/svg" width="' + width + '" height="' + height +
    '"><foreignObject width="' + width + '" height="' + height + '">' +
    new XMLSerializer().serializeToString(clone) + '</foreignObject></svg>';
  const bitmap = await new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error('svg 渲染失败'));
    image.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg);
  });
  const x = Math.max(0, region.x | 0);
  const y = Math.max(0, region.y | 0);
  const w = region.width > 0 ? Math.min(region.width, width - x) : width - x;
  const hh = region.height > 0 ? Math.min(region.height, height - y) : height - y;
  if (w < 1 || hh < 1) throw new Error('截图区域为空');
  const canvas = document.createElement('canvas');
  canvas.width = w;
  canvas.height = hh;
  canvas.getContext('2d').drawImage(bitmap, x, y, w, hh, 0, 0, w, hh);
  return canvas.toDataURL('image/png');
}

// ---------------- App ----------------

// ---------------- 设置（对齐 WinForms 设置页） ----------------

const SettingRow = ({ label, children }) => h('div', {
  style: { display: 'grid', gridTemplateColumns: '88px 1fr', alignItems: 'center', gap: 10, marginBottom: 10 }
},
  h('div', { style: { fontSize: 13, color: '#595959' } }, label),
  h('div', { style: { minWidth: 0 } }, children));

const SettingHint = ({ children, warn }) => h('div', {
  style: { fontSize: 12, color: warn ? '#ff4d4f' : '#8c8c8c', margin: '-4px 0 12px 98px' }
}, children);

const SwitchRow = ({ label, checked, onChange, hint, warn }) => h('div', null,
  h(SettingRow, { label },
    h('div', { style: { display: 'flex', alignItems: 'center', gap: 8 } },
      h(Switch, { checked: !!checked, onChange }),
      hint ? h('span', { style: { fontSize: 12, color: warn ? '#ff4d4f' : '#8c8c8c' } }, hint) : null)));

const blankProvider = () => ({
  name: 'OpenRouter', kind: 'openrouter', baseUrl: 'https://openrouter.ai/api/v1',
  hasKey: false, key: null, models: []
});

function SettingsPanel({ caps, onClose }) {
  const [loaded, setLoaded] = useState(null);
  const [draft, setDraft] = useState(null);
  const [tab, setTab] = useState('general');
  const [busy, setBusy] = useState(false);
  const [providerIndex, setProviderIndex] = useState(0);
  const [modelInput, setModelInput] = useState('');
  const [propsModel, setPropsModel] = useState(null);
  const [propsDraft, setPropsDraft] = useState({ name: '', contextLimit: 0 });
  const [providerStatus, setProviderStatus] = useState('');
  const [skills, setSkills] = useState({ root: '', rows: [], error: '' });
  const [skillScope, setSkillScope] = useState('project');
  const [installPath, setInstallPath] = useState('');
  const workspace = caps?.workspace || localStorage.getItem('openloop.workspace') || '';

  const reload = async () => {
    try {
      const s = await api('/api/settings');
      setLoaded(s);
      setDraft(JSON.parse(JSON.stringify(s)));
      setProviderIndex(0);
    } catch (e) { message.error('读取设置失败：' + e.message); }
  };
  useEffect(() => { reload(); }, []);

  const loadSkills = async scope => {
    try {
      setSkills(await api(`/api/settings/skills?scope=${scope}&workspace=${encodeURIComponent(workspace)}`) || {});
    } catch (e) { message.error('读取技能失败：' + e.message); }
  };
  useEffect(() => { if (tab === 'skills') loadSkills(skillScope); }, [tab, skillScope]);

  const patch = p => setDraft(d => ({ ...d, ...p }));
  const providers = draft?.providers || [];
  const provider = providers[providerIndex] || null;
  const setProvider = (i, p) => setDraft(d => {
    const list = [...(d.providers || [])];
    list[i] = { ...list[i], ...p };
    return { ...d, providers: list };
  });

  const save = async () => {
    if (draft.fullAccess && !loaded.fullAccess
      && !window.confirm('完全许可会关闭路径边界、命令审批与环境变量隔离，子进程可能拿到密钥，也可以直接访问网络与任意路径。确定启用？')) return;
    setBusy(true);
    try {
      const modelProps = {};
      for (const [id, p] of Object.entries(draft.modelProps || {})) {
        if (!id) continue;
        if ((p?.name || '') === '' && !p?.contextLimit) continue;
        modelProps[id] = { name: p?.name || '', contextLimit: p?.contextLimit || 0 };
      }
      await post('/api/settings', { ...draft, modelProps });
      message.success('已保存');
      await reload();
    } catch (e) { message.error('保存失败：' + e.message); }
    setBusy(false);
  };

  const addProvider = () => {
    const list = [...providers, blankProvider()];
    setDraft(d => ({ ...d, providers: list }));
    setProviderIndex(list.length - 1);
    setProviderStatus('');
  };

  const removeProvider = () => {
    if (!provider) return;
    if (!window.confirm(`删除提供商「${provider.name}」？`)) return;
    const list = providers.filter((_, i) => i !== providerIndex);
    setDraft(d => ({ ...d, providers: list }));
    setProviderIndex(Math.max(0, Math.min(providerIndex, list.length - 1)));
    setProviderStatus('');
  };

  const providerBody = extra => ({
    name: provider?.name || '', kind: provider?.kind || '', baseUrl: provider?.baseUrl || '',
    key: provider?.key ?? null, ...extra
  });

  const fetchModels = async () => {
    if (!provider) return;
    setProviderStatus('正在拉取模型…');
    try {
      const r = await post('/api/settings/provider/fetch', providerBody({}));
      const merged = [...new Set([...(provider.models || []), ...(r.models || [])])];
      setProvider(providerIndex, { models: merged });
      setProviderStatus(`拉取到 ${(r.models || []).length} 个模型`);
    } catch (e) {
      setProviderStatus('拉取失败');
      message.error('拉取失败：' + e.message);
    }
  };

  const testProvider = async () => {
    if (!provider) return;
    const model = (provider.models || [])[0];
    if (!model) return message.warning('先拉取或添加一个模型');
    setProviderStatus('正在测试连接…');
    try {
      const r = await post('/api/settings/provider/test', providerBody({ model }));
      setProviderStatus('连接正常：' + (r.reply || ''));
    } catch (e) {
      setProviderStatus('连接失败');
      message.error('连接失败：' + e.message);
    }
  };

  const addModel = () => {
    const id = modelInput.trim();
    if (!id || !provider) return;
    if ((provider.models || []).some(m => m.toLowerCase() === id.toLowerCase())) return;
    setProvider(providerIndex, { models: [...(provider.models || []), id] });
    setModelInput('');
  };

  const removeModel = id => {
    if (!provider) return;
    setProvider(providerIndex, { models: (provider.models || []).filter(m => m !== id) });
  };

  const openProps = id => {
    const p = draft?.modelProps?.[id];
    setPropsDraft({ name: p?.name || '', contextLimit: p?.contextLimit || 0 });
    setPropsModel(id);
  };

  const applyProps = () => {
    setDraft(d => ({
      ...d,
      modelProps: { ...(d.modelProps || {}), [propsModel]: { name: propsDraft.name.trim(), contextLimit: propsDraft.contextLimit || 0 } }
    }));
    setPropsModel(null);
  };

  const toggleSkill = async (row, enabled) => {
    try {
      await post('/api/settings/skills', { workspace, scope: skillScope, name: row.name, enabled });
      await loadSkills(skillScope);
    } catch (e) { message.error('切换失败：' + e.message); }
  };

  const uninstallSkill = async row => {
    if (!window.confirm(`确定卸载技能「${row.name}」？\n\n会删除整个目录，不可恢复：\n${row.directory}`)) return;
    try {
      await post('/api/settings/skills/uninstall', { workspace, scope: skillScope, name: row.name });
      message.success('已卸载 ' + row.name);
      await loadSkills(skillScope);
    } catch (e) { message.error('卸载失败：' + e.message); }
  };

  const installSkill = async () => {
    if (!installPath.trim()) return message.warning('先填技能目录的绝对路径');
    try {
      const r = await post('/api/settings/skills/install', {
        workspace, scope: skillScope, path: installPath.trim()
      });
      setInstallPath('');
      message.success('已安装到 ' + r.directory);
      await loadSkills(skillScope);
    } catch (e) { message.error('安装失败：' + e.message); }
  };

  if (!draft) return h('div', { style: { padding: 30, textAlign: 'center' } }, h(Muted, null, '正在读取设置…'));

  const general = [
    h(SettingRow, { key: 'lang', label: '语言' },
      h(Select, {
        value: draft.language || '', style: { maxWidth: 240 },
        onChange: v => patch({ language: v }),
        options: [{ value: '', label: '跟随系统' }, { value: 'zh', label: '中文' }, { value: 'en', label: 'English' }]
      })),
    h(SettingRow, { key: 'theme', label: '主题色' },
      h(Select, {
        value: draft.theme || 'light', style: { maxWidth: 240 },
        onChange: v => patch({ theme: v }),
        options: [{ value: 'light', label: '浅色' }, { value: 'dark', label: '深色' }, { value: 'grayblue', label: '灰蓝' }]
      })),
    h(SwitchRow, {
      key: 'full', label: '完全许可', checked: draft.fullAccess,
      onChange: v => patch({ fullAccess: v }),
      hint: '关闭路径边界、命令审批与环境隔离，可直连网络', warn: true
    }),
    h(SwitchRow, {
      key: 'auto', label: '自动审批', checked: draft.autoApprove,
      onChange: v => patch({ autoApprove: v }),
      hint: '一切工具调用都无需审批（含工作区外访问与风险操作）'
    }),
    h(SwitchRow, {
      key: 'agents', label: 'AGENTS.md', checked: draft.useAgentsMd,
      onChange: v => patch({ useAgentsMd: v }),
      hint: '把全局与项目 AGENTS.md 注入系统提示词'
    }),
    h(SwitchRow, {
      key: 'think', label: '思考过程', checked: draft.thinkingExpandedByDefault,
      onChange: v => patch({ thinkingExpandedByDefault: v }),
      hint: '新回话默认展开「思考与工具过程」'
    }),
    h(SwitchRow, {
      key: 'evolve', label: '自动演进', checked: draft.autoEvolve,
      onChange: v => patch({ autoEvolve: v }),
      hint: '攒够新证据就自己跑一轮演进（阈值与冷却见 settings.json）'
    }),
    h(SettingHint, { key: 'note' }, '主题与语言在重启 OpenLoop 后生效；完全许可、自动审批与自动演进立即生效。')
  ];

  const openCode = [
    h(SwitchRow, {
      key: 'ask', label: '权限审批', checked: draft.openCodeAskPermissions,
      onChange: v => patch({ openCodeAskPermissions: v }),
      hint: '委派给 OpenCode 时，每个动作都经本应用审批'
    }),
    h(SettingHint, { key: 'note' }, '服务通过 ~/.local/state/opencode/service.json 自动发现；未就绪时不会出现在模型列表里。')
  ];

  const tools = [
    h(SettingRow, { key: 'webfetch', label: 'WebFetch' },
      h(Select, {
        value: draft.webFetchMode || 'allow', style: { maxWidth: 240 },
        onChange: v => patch({ webFetchMode: v }),
        options: [
          { value: 'allow', label: '免审批（默认）' },
          { value: 'ask', label: '每次审批' },
          { value: 'off', label: '关闭' }
        ]
      })),
    h(SettingHint, { key: 'hint' }, '只读抓取 http/https 网页正文并转纯文本；不执行 JavaScript，内容按不可信数据处理。'),
    h(SettingHint, { key: 'note' }, '内网与环回地址一律拒绝；只抓文本/HTML/JSON/XML，单页最多 2MB，结果默认 6000 字符，一轮最多 8 次。Worker 与 Loop 同时生效。')
  ];

  const providerOptions = providers.map((p, i) => ({ value: i, label: p.name || ('提供商 ' + (i + 1)) }));
  const modelProps = draft.modelProps || {};

  const models = [
    h(SettingRow, { key: 'provider', label: '提供商' },
      h('div', { style: { display: 'flex', gap: 8, flexWrap: 'wrap' } },
        h(Select, {
          value: providers.length ? providerIndex : undefined, placeholder: '还没有提供商',
          style: { minWidth: 220 }, options: providerOptions,
          onChange: v => { setProviderIndex(v); setProviderStatus(''); }
        }),
        h(Button, { onClick: addProvider }, '添加'),
        h(Button, { danger: true, disabled: !provider, onClick: removeProvider }, '删除'))),
    h(SettingRow, { key: 'name', label: '名称' },
      h(Input, {
        value: provider?.name || '', disabled: !provider, style: { maxWidth: 320 },
        onChange: e => setProvider(providerIndex, { name: e.target.value })
      })),
    h(SettingRow, { key: 'url', label: '接口地址' },
      h(Input, {
        value: provider?.baseUrl || '', disabled: !provider, style: { maxWidth: 420 },
        onChange: e => setProvider(providerIndex, { baseUrl: e.target.value })
      })),
    h(SettingRow, { key: 'key', label: '密钥' },
      h(Input.Password, {
        value: provider?.key ?? '', disabled: !provider, style: { maxWidth: 420 },
        placeholder: provider?.hasKey && provider?.key == null ? '已保存（留空表示不改）' : '粘贴 API Key',
        onChange: e => setProvider(providerIndex, { key: e.target.value })
      })),
    h(SettingRow, { key: 'actions', label: '' },
      h('div', { style: { display: 'flex', gap: 8, flexWrap: 'wrap' } },
        h(Button, { disabled: !provider, onClick: fetchModels }, '拉取模型'),
        h(Button, { disabled: !provider, onClick: testProvider }, '测试'),
        h(Muted, null, providerStatus))),
    h(SettingRow, { key: 'models', label: '模型' },
      h('div', null,
        h('div', { style: { display: 'flex', gap: 8, marginBottom: 8 } },
          h(Input, {
            value: modelInput, placeholder: '模型 id，回车加入', style: { maxWidth: 320 },
            onChange: e => setModelInput(e.target.value),
            onPressEnter: addModel
          }),
          h(Button, { disabled: !provider, onClick: addModel }, '加入')),
        h('div', {
          className: 'ol-scroll',
          style: { height: 220, border: '1px solid #f0f0f0', borderRadius: 8, background: '#fafafa', padding: 6 }
        },
          (provider?.models || []).length === 0
            ? h(Muted, { style: { display: 'block', padding: 8 } }, '这个提供商还没有模型')
            : (provider.models || []).map(id => h('div', {
              key: id,
              style: { display: 'flex', alignItems: 'center', gap: 8, padding: '3px 6px' }
            },
              h('span', { style: { flex: 1, fontSize: 13, overflow: 'hidden', textOverflow: 'ellipsis' } }, id),
              modelProps[id]?.name || modelProps[id]?.contextLimit
                ? h(Muted, null, `${modelProps[id]?.name || ''}${modelProps[id]?.contextLimit ? ' · ' + modelProps[id].contextLimit : ''}`)
                : null,
              h(Button, { size: 'small', type: 'link', onClick: () => openProps(id) }, '属性'),
              h(Button, { size: 'small', type: 'link', danger: true, onClick: () => removeModel(id) }, '删除')))))),
    h(SettingHint, { key: 'note' }, '「拉取模型」会请求提供商的 /models；「测试」会真的发一次最小补全。属性可设显示名称与上下文上限。')
  ];

  const skillRows = skills.rows || [];
  const skillsPage = [
    h(SettingRow, { key: 'scope', label: '范围' },
      h('div', { style: { display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' } },
        h(Segmented, {
          value: skillScope, onChange: setSkillScope,
          options: [{ value: 'global', label: '全局' }, { value: 'project', label: '项目' }]
        }),
        h(Muted, null, skills.root || (workspace ? '' : '先在项目里打开一个会话，或切到「全局」管理全局技能')))),
    h(SettingRow, { key: 'install', label: '安装' },
      h('div', { style: { display: 'flex', gap: 8, flexWrap: 'wrap' } },
        h(Input, {
          value: installPath, placeholder: '含 SKILL.md 的目录（绝对路径）', style: { maxWidth: 420 },
          onChange: e => setInstallPath(e.target.value)
        }),
        h(Button, { onClick: installSkill }, '安装'))),
    h('div', {
      className: 'ol-scroll',
      style: { height: 320, border: '1px solid #f0f0f0', borderRadius: 8, padding: 6 }
    },
      skills.error ? h(Muted, { style: { display: 'block', padding: 8 } }, skills.error)
        : skillRows.length === 0 ? h(Muted, { style: { display: 'block', padding: 8 } }, '没有可管理的技能')
          : skillRows.map(row => h('div', {
            key: row.name,
            style: { display: 'flex', alignItems: 'center', gap: 10, padding: '6px 8px', borderBottom: '1px solid #f5f5f5' }
          },
            h(Switch, { size: 'small', checked: row.enabled, onChange: v => toggleSkill(row, v) }),
            h('div', { style: { minWidth: 0, flex: 1 } },
              h('div', { style: { fontSize: 13 } }, row.name,
                h(Muted, { style: { marginLeft: 8 } }, row.global ? '全局' : row.opencode ? '项目 .opencode' : '项目')),
              h(Muted, { style: { display: 'block', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } },
                row.description || row.directory || '')),
            h(Button, {
              size: 'small', danger: true,
              disabled: skillScope === 'project' && (row.global || row.opencode),
              onClick: () => uninstallSkill(row)
            }, '卸载')))),
    h(SettingHint, { key: 'note' }, '项目技能停用/启用走账本（由 I 收敛）；全局技能只改本机开关。.opencode/skills 里的技能由 opencode 管理，这里只能看。')
  ];

  return h('div', null,
    h(Tabs, {
      size: 'small', activeKey: tab, onChange: setTab,
      items: [
        { key: 'general', label: '普通', children: h('div', { style: { paddingTop: 12 } }, general) },
        { key: 'opencode', label: 'OpenCode', children: h('div', { style: { paddingTop: 12 } }, openCode) },
        { key: 'tools', label: '工具', children: h('div', { style: { paddingTop: 12 } }, tools) },
        { key: 'models', label: '模型', children: h('div', { style: { paddingTop: 12 } }, models) },
        { key: 'skills', label: '技能', children: h('div', { style: { paddingTop: 12 } }, skillsPage) }
      ]
    }),
    h('div', { style: { display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 12 } },
      h(Button, { onClick: onClose }, '取消'),
      h(Button, { type: 'primary', loading: busy, onClick: save }, '保存')),
    h(Modal, {
      open: propsModel != null, title: '模型属性', okText: '确定', cancelText: '取消',
      onOk: applyProps, onCancel: () => setPropsModel(null), width: 420
    },
      h('div', { style: { display: 'grid', gap: 10, paddingTop: 8 } },
        h(Muted, null, propsModel),
        h(Input, {
          value: propsDraft.name, placeholder: '显示名称（留空沿用模型 id）',
          onChange: e => setPropsDraft(p => ({ ...p, name: e.target.value }))
        }),
        h(Input, {
          value: String(propsDraft.contextLimit || ''), placeholder: '上下文上限（token，0 表示自动）',
          onChange: e => setPropsDraft(p => ({ ...p, contextLimit: parseInt(e.target.value, 10) || 0 }))
        }))));
}

function App() {
  const [caps, setCaps] = useState(null);
  const [models, setModels] = useState([]);
  const [sessions, setSessions] = useState([]);
  const [tabs, setTabs] = useState(() => {
    try { return JSON.parse(localStorage.getItem('openloop.tabs') || '[]'); } catch { return []; }
  });
  const [activeKey, setActiveKey] = useState('overview');
  const [snapshot, setSnapshot] = useState(null);
  const [project, setProject] = useState(null);
  const [shot, setShot] = useState(localStorage.getItem('openloop.shot') === '1');
  const [newOpen, setNewOpen] = useState(false);
  const [aboutOpen, setAboutOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [newWorkspace, setNewWorkspace] = useState('');
  const [newModel, setNewModel] = useState('');
  const [status, setStatus] = useState('');

  const stateRef = useRef({ activeKey, snapshot, sessions, shot, tabs });
  useEffect(() => { stateRef.current = { activeKey, snapshot, sessions, shot, tabs }; });
  useEffect(() => { localStorage.setItem('openloop.tabs', JSON.stringify(tabs)); }, [tabs]);

  const activeId = activeKey !== 'overview' && activeKey !== 'loop' ? activeKey : null;

  const reloadSessions = async () => {
    try { setSessions(await api('/api/sessions') || []); } catch { /* A 未就绪 */ }
  };

  useEffect(() => {
    (async () => {
      try {
        const c = await api('/api/capabilities');
        setCaps(c);
        if (!c.worker) setActiveKey('loop');
        else {
          try {
            const m = await api('/api/models');
            setModels(m?.models || []);
          } catch { /* 模型目录可能未就绪 */ }
        }
      } catch (e) { setStatus('拿不到能力位：' + e.message); }
    })();
  }, []);

  useEffect(() => {
    if (!caps?.worker) return;
    let alive = true;
    const tick = async () => {
      try {
        const rows = await api('/api/sessions');
        if (alive) setSessions(rows || []);
      } catch { /* 忽略瞬时失败 */ }
      if (alive) setTimeout(tick, 3000);
    };
    tick();
    return () => { alive = false; };
  }, [caps?.worker]);

  const openSession = async (id) => {
    if (!id) return;
    setTabs(prev => prev.includes(id) ? prev : [...prev, id]);
    setActiveKey(id);
    let data = null;
    const row = stateRef.current.sessions.find(s => s.id === id);
    if (!row || row.live !== false) {
      try { data = await api('/api/sessions/' + id); } catch { /* 只有索引里的历史会话 */ }
    }
    if (!data) {
      data = await post('/api/sessions', { id, workspace: row?.workspace || '', model: row?.model || '' });
    }
    setSnapshot(data);
    localStorage.setItem('openloop.session', id);
    if (data?.workspace) localStorage.setItem('openloop.workspace', data.workspace);
  };

  useEffect(() => {
    if (!activeId || !caps?.worker) return;
    let alive = true;
    const tick = async () => {
      try {
        const next = await api('/api/sessions/' + activeId);
        if (alive && next) setSnapshot(next);
      } catch {
        // 只有「索引里有、宿主里没有」才从磁盘恢复；会话被删掉就别再 POST 把他救回来
        const row = stateRef.current.sessions.find(s => s.id === activeId);
        if (!row) {
          if (alive) {
            setSnapshot(null);
            setTabs(prev => prev.filter(t => t !== activeId));
            setActiveKey('overview');
            setStatus('会话已删除：' + activeId);
          }
          return;
        }
        if (row.live !== false) { if (alive) setTimeout(tick, 800); return; }
        try {
          const again = await post('/api/sessions', { id: activeId, workspace: row.workspace || '', model: row.model || '' });
          if (alive && again) { setSnapshot(again); setStatus('已重连 ' + activeId); }
        } catch (e) { if (alive) setStatus('会话读取失败：' + e.message); }
      }
      if (alive) setTimeout(tick, 800);
    };
    tick();
    return () => { alive = false; };
  }, [activeId, caps?.worker]);

  useEffect(() => {
    let alive = true;
    const tick = async () => {
      const s = stateRef.current;
      try {
        await post('/api/puppet/state', {
          url: location.href,
          tab: s.activeKey,
          sessionId: s.activeKey === 'overview' || s.activeKey === 'loop' ? '' : s.activeKey,
          sessionCount: s.sessions.length,
          sessionState: s.snapshot?.state || '',
          prompts: (s.snapshot?.prompts || []).length,
          pending: (s.snapshot?.pending || []).length,
          width: window.innerWidth,
          height: window.innerHeight,
          dpr: window.devicePixelRatio || 1,
          captureEnabled: s.shot
        });
      } catch { /* 旧宿主没有钩子 */ }
      if (s.shot) {
        try {
          const pending = await api('/api/puppet/pending');
          if (pending) await post('/api/puppet/shot', { id: pending.id, png: await capturePage(pending) });
        } catch (e) { setStatus('Puppet 截图失败：' + e.message); }
      }
      if (alive) setTimeout(tick, 1000);
    };
    tick();
    return () => { alive = false; };
  }, []);

  const deleteSession = async (id) => {
    try {
      await api('/api/sessions/' + id, { method: 'DELETE' });
      setTabs(prev => prev.filter(t => t !== id));
      if (activeKey === id) { setActiveKey('overview'); setSnapshot(null); }
      await reloadSessions();
      message.success('已删除会话 ' + id);
    } catch (e) { message.error('删除失败：' + e.message); }
  };

  const createSession = async () => {
    if (!newWorkspace.trim()) return message.warning('先填工作目录');
    try {
      const created = await post('/api/sessions', { workspace: newWorkspace.trim(), model: newModel.trim() });
      setNewOpen(false);
      setNewModel('');
      await reloadSessions();
      await openSession(created.id);
    } catch (e) { message.error('新建失败：' + e.message); }
  };

  const items = [
    { key: 'new', label: '新建会话' },
    { key: 'list', label: '会话列表' },
    { key: 'loop', label: 'Loop' },
    { type: 'divider' },
    { key: 'settings', label: '设置' },
    { key: 'about', label: '关于' }
  ];

  const menu = {
    items: caps?.worker ? items : items.filter(i => i.type === 'divider' || i.key === 'loop' || i.key === 'about'),
    onClick: ({ key }) => {
      if (key === 'new') {
        setNewWorkspace(project || caps?.workspace || '');
        setNewOpen(true);
      } else if (key === 'list') setActiveKey('overview');
      else if (key === 'loop') setActiveKey('loop');
      else if (key === 'settings') setSettingsOpen(true);
      else if (key === 'about') setAboutOpen(true);
    }
  };

  const sessionTab = id => {
    const row = sessions.find(s => s.id === id);
    const state = (snapshot && snapshot.id === id ? snapshot.state : row?.state) || 'idle';
    return {
      key: id,
      label: h('span', { style: { display: 'inline-flex', alignItems: 'center', maxWidth: 200 } },
        h(Dot, { state }),
        h('span', { style: { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } },
          snapshot && snapshot.id === id ? (snapshot.title || id) : (row?.title || row?.model || id)))
    };
  };

  const tabItems = caps?.worker
    ? [{ key: 'overview', label: '会话', closable: false }, ...tabs.map(sessionTab)]
    : [];
  if (caps?.loop) tabItems.push({ key: 'loop', label: 'Loop', closable: false });

  let page;
  if (caps == null) page = h('div', { style: { padding: 40, textAlign: 'center' } }, h(Muted, null, status || '正在连接…'));
  else if (activeKey === 'overview') {
    page = h(OverviewPage, {
      sessions, project, setProject, onOpen: openSession,
      onNew: () => { setNewWorkspace(project || caps?.workspace || ''); setNewOpen(true); },
      onDelete: deleteSession,
      onLoop: () => setActiveKey('loop'),
      caps
    });
  } else if (activeKey === 'loop') {
    page = h(LoopPage, { defaultWorkspace: caps?.workspace || '', shot, setShot });
  } else {
    page = h(SessionPage, {
      snapshot, models, activeId, shot, setShot,
      onModel: (m, e) => post(`/api/sessions/${activeId}/model`, { model: m, effort: e }),
      onDelete: deleteSession
    });
  }

  return h(ConfigProvider, {
    theme: { token: { colorPrimary: '#1677ff', borderRadius: 8 } },
    button: { autoInsertSpace: false }
  },
    h('div', { style: { height: '100%', display: 'flex', flexDirection: 'column', background: '#f5f6f7' } },
      h(TopBar, { menu, onSettings: () => setSettingsOpen(true) }),
      h('div', { style: { flex: 'none', background: '#fff', padding: '0 12px' } },
        h(Tabs, {
          type: 'editable-card', size: 'small', hideAdd: true,
          activeKey, items: tabItems,
          onChange: key => { setActiveKey(key); if (key !== 'overview' && key !== 'loop') openSession(key); },
          onEdit: (key, action) => {
            if (action !== 'remove') return;
            setTabs(prev => prev.filter(t => t !== key));
            if (activeKey === key) setActiveKey('overview');
          },
          tabBarStyle: { margin: 0 }
        })),
      h('div', { style: { flex: 1, minHeight: 0, background: '#fff' } }, page),
      status ? h('div', { style: { position: 'fixed', right: 16, bottom: 8, fontSize: 12, color: '#8c8c8c' } }, status) : null,
      h(Modal, {
        open: newOpen, title: '新建会话', okText: '创建', cancelText: '取消',
        onOk: createSession, onCancel: () => setNewOpen(false)
      },
        h('div', { style: { display: 'grid', gap: 8 } },
          h(Input, { value: newWorkspace, onChange: e => setNewWorkspace(e.target.value), placeholder: '工作目录（绝对路径）' }),
          h(Input, { value: newModel, onChange: e => setNewModel(e.target.value), placeholder: '模型，如 opencode/xxx（留空用默认）' }))),
      h(Modal, {
        open: settingsOpen, title: '设置', width: 960, footer: null,
        onCancel: () => setSettingsOpen(false), destroyOnClose: true
      },
        h(SettingsPanel, { caps, onClose: () => setSettingsOpen(false) })),
      h(Modal, {
        open: aboutOpen, title: '关于', footer: null, onCancel: () => setAboutOpen(false)
      },
        h('div', { style: { display: 'grid', gap: 6 } },
          h('div', null, 'OpenLoop 浏览器壳'),
          h(Muted, null, '角色：' + (caps?.worker ? 'Worker + Loop' : caps?.loop ? 'Loop' : '—')),
          h(Muted, null, '默认工作区：' + (caps?.workspace || '—')),
          h(Muted, null, '模型目录：' + (models.length ? models.length + ' 个' : '未就绪')),
          h(Muted, null, '会话：' + sessions.length + ' 个'),
          h(PuppetRow, { shot, setShot })))));
}

try {
  ReactDOM.createRoot(document.getElementById('root')).render(h(App));
} catch (e) {
  document.title = 'ERR:' + (e && e.message ? e.message : e);
}
