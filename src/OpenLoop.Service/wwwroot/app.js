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
  if (!res.ok) throw new Error(text || ('HTTP ' + res.status));
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

function TopBar({ menu, onAbout }) {
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
    h(Tooltip, { title: '关于' },
      h(Button, { type: 'text', style: { width: 38, height: 34, borderRadius: 8 }, onClick: onAbout }, '⚙')));
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
  const pct = snapshot?.contextLimit > 0 && snapshot?.contextTokens != null
    ? Math.min(100, Math.round(snapshot.contextTokens / snapshot.contextLimit * 100)) : 0;

  const send = async () => {
    if (!activeId) return;
    if (!value) {
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
    }, running ? (value ? '排队发送' : '停止') : '发送'));

  const composer = h('div', { style: { flex: 'none', padding: '4px 16px 8px', borderTop: pending ? '0' : '1px solid #f0f0f0' } },
    h(Input.TextArea, {
      value: text, onChange: e => setText(e.target.value), autoSize: { minRows: 3, maxRows: 10 },
      placeholder: '描述任务，或声明判据 / 纠正（Ctrl+Enter 发送）',
      onKeyDown: e => {
        if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); send(); }
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
              setIteration(await post('/api/loop/iteration', { workspace: ws, shell: shellIncluded }));
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
          h(Switch, { size: 'small', checked: shellIncluded, onChange: setShellIncluded }), '连壳')),
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
      h(TopBar, { menu, onAbout: () => setAboutOpen(true) }),
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
