// OpenLoop 浏览器界面：一套代码，A（worker+loop）和 I（只有 loop）按 /api/capabilities 显示面板。
const $ = id => document.getElementById(id);

let caps = { worker: false, loop: false };
let sessions = [];
let activeId = null;
let snapshot = null;
let revision = -1;
let answered = new Set();
let sessionTimer = null;
let evolveTimer = null;

async function api(path, options = {}) {
  const res = await fetch(path, { headers: { 'Content-Type': 'application/json' }, ...options });
  const text = await res.text();
  if (!res.ok) throw new Error(text || ('HTTP ' + res.status));
  return text ? JSON.parse(text) : null;
}

const post = (path, body) => api(path, { method: 'POST', body: JSON.stringify(body ?? {}) });

function say(text) {
  $('state').textContent = text || '';
}

function workspaceOf() {
  return ($('loop-workspace').value || '').trim();
}

// ---------- worker 面板 ----------

function renderSessions() {
  const list = $('sessions');
  list.innerHTML = '';
  for (const s of sessions) {
    const li = document.createElement('li');
    li.className = s.id === activeId ? 'active' : '';
    li.innerHTML = '<span class="title"></span><span class="sub"></span>';
    li.querySelector('.title').textContent = s.id + ' · ' + s.state;
    li.querySelector('.sub').textContent = s.workspace || '';
    li.onclick = () => openSession(s.id);
    list.appendChild(li);
  }
}

function renderChat() {
  const box = $('messages');
  if (!snapshot) {
    box.innerHTML = '<div class="muted">没有选中会话。</div>';
    return;
  }

  const atBottom = box.scrollTop + box.clientHeight >= box.scrollHeight - 40;
  box.innerHTML = '';
  for (const turn of snapshot.turns || []) {
    if (!turn.user && !turn.assistant && !turn.toolLines) continue;
    box.appendChild(turnNode('你', turn.user, 'user'));
    box.appendChild(turnNode('OpenLoop', turn.assistant, 'assistant', turn.toolLines, turn.streaming));
  }
  if (atBottom) box.scrollTop = box.scrollHeight;

  $('run').textContent = [snapshot.state, snapshot.outcome,
    (snapshot.turns || []).reduce((n, t) => n + (t.steps || 0), 0) + ' 步',
    snapshot.activationVersion ? 'v' + snapshot.activationVersion : '']
    .filter(Boolean).join(' · ');
  $('send').disabled = false;
  $('stop').disabled = snapshot.state !== 'running';
  renderPrompts();
}

function turnNode(role, text, cls, tools, streaming) {
  const div = document.createElement('div');
  div.className = 'turn ' + cls;
  div.innerHTML = '<div class="role"></div><div class="body"></div>' +
    (tools ? '<div class="tools"></div>' : '');
  div.querySelector('.role').textContent = role + (streaming ? ' · 输出中…' : '');
  div.querySelector('.body').textContent = text || '';
  if (tools) div.querySelector('.tools').textContent = tools;
  return div;
}

function renderPrompts() {
  const box = $('prompts');
  box.innerHTML = '';
  for (const p of snapshot.prompts || []) {
    if (answered.has(p.id)) continue;
    const div = document.createElement('div');
    div.className = 'prompt';
    div.innerHTML = '<div class="text"></div><div class="row">' +
      '<button data-d="1">允许一次</button>' +
      '<button data-d="2">同类都允许</button>' +
      '<button data-d="0" class="ghost">拒绝</button></div>';
    div.querySelector('.text').textContent = (p.category ? '[' + p.category + '] ' : '') +
      (p.tool ? p.tool + '：' : '') + (p.text || '') + (p.reason ? '（' + p.reason + '）' : '');
    div.querySelectorAll('button').forEach(b => {
      b.onclick = async () => {
        answered.add(p.id);
        try { await post(`/api/sessions/${activeId}/approval`, { id: p.id, decision: Number(b.dataset.d) }); }
        catch (e) { say('审批回传失败：' + e.message); }
        renderPrompts();
      };
    });
    box.appendChild(div);
  }
}

async function openSession(id) {
  activeId = id;
  revision = -1;
  answered = new Set();
  snapshot = await api('/api/sessions/' + id);
  if (snapshot.workspace) {
    $('loop-workspace').value = snapshot.workspace;
    localStorage.setItem('openloop.workspace', snapshot.workspace);
    refreshLedger();
  }
  renderSessions();
  renderChat();
  clearTimeout(sessionTimer);
  sessionTimer = setTimeout(pollSession, 300);
}

async function pollSession() {
  if (!activeId || !caps.worker) return;
  try {
    const next = await api('/api/sessions/' + activeId);
    if (next) snapshot = next;
    if (next && (next.revision !== revision || (next.turns || []).some(t => t.streaming))) {
      revision = next.revision;
      mergeSession(next);
      renderChat();
      renderSessions();
    }
  } catch (e) {
    // A 重启后内存里的会话没了：带同一个 id 重建，A 会从 chats/ 读回历史
    try {
      const again = await post('/api/sessions',
        { id: activeId, workspace: snapshot?.workspace || workspaceOf(), model: snapshot?.model || '' });
      if (again) { snapshot = again; revision = -1; say('已重连 ' + activeId); }
    } catch { say('会话读取失败：' + e.message); }
  }
  clearTimeout(sessionTimer);
  sessionTimer = setTimeout(pollSession, 600);
}

async function loadSessions() {
  sessions = await api('/api/sessions') || [];
  renderSessions();
}

function mergeSession(next) {
  const row = { id: next.id, workspace: next.workspace, state: next.state, model: next.model };
  const i = sessions.findIndex(s => s.id === next.id);
  if (i >= 0) sessions[i] = { ...sessions[i], ...row };
  else sessions.unshift(row);
}

// ---------- loop 面板 ----------

function ledgerText(payload) {
  if (!payload || !payload.activation) return '账本未初始化（先跑一次或新建会话）';
  const a = payload.activation;
  const rules = (a.features || []).filter(f => f.id && f.id.startsWith('rule.')).length;
  return `v${a.version} · ${(a.features || []).length} 特征 · ${rules} 规则`;
}

async function refreshLedger() {
  const ws = workspaceOf();
  if (!ws) { $('ledger').textContent = '先填工作目录'; $('skills').textContent = ''; return; }
  localStorage.setItem('openloop.workspace', ws);
  try {
    const payload = await api('/api/loop/activation?workspace=' + encodeURIComponent(ws));
    $('ledger').textContent = ledgerText(payload);
    $('skills').textContent = (payload.skills || []).join('\n') || '（无）';
  } catch (e) {
    $('ledger').textContent = '读账本失败：' + e.message;
    $('skills').textContent = '';
  }
}

function renderEvolve(s) {
  if (!s) return;
  const parts = [s.state, s.phase];
  if (s.taskCount) parts.push(`题目 ${s.taskCount}`);
  if (s.state === 'done') parts.push(`候选 ${s.candidatePassed}/${s.taskCount}，基线 ${s.baselinePassed}/${s.taskCount}`);
  if (s.adopted) parts.push('已采纳 v' + s.candidateVersion);
  if (s.detail) parts.push(s.detail);
  if (s.error) parts.push(s.error);
  $('evolve-state').textContent = parts.filter(Boolean).join(' · ');
  $('evolve').disabled = s.state === 'running';
  $('evolve-stop').disabled = s.state !== 'running';
  $('auto-evolve').checked = !!s.autoEnabled;
  $('auto-state').textContent = s.autoHint || '';
}

async function pollEvolve() {
  const ws = workspaceOf();
  if (!ws) return;
  try {
    const s = await api('/api/loop/evolve?workspace=' + encodeURIComponent(ws));
    renderEvolve(s);
    if (s.state === 'running') {
      clearTimeout(evolveTimer);
      evolveTimer = setTimeout(pollEvolve, 2000);
    } else {
      refreshLedger();
    }
  } catch (e) {
    say('演进状态读取失败：' + e.message);
  }
}

// ---------- 事件 ----------

$('new-session').onsubmit = async e => {
  e.preventDefault();
  const workspace = $('new-workspace').value.trim();
  if (!workspace) return say('先填工作目录');
  try {
    const created = await post('/api/sessions', { workspace, model: $('new-model').value.trim() });
    $('new-workspace').value = '';
    await loadSessions();
    await openSession(created.id);
    say('已新建 ' + created.id);
  } catch (err) { say('新建失败：' + err.message); }
};

$('composer').onsubmit = async e => {
  e.preventDefault();
  const text = $('text').value.trim();
  if (!text) return;
  if (!activeId) return say('先选一个会话');
  try {
    $('text').value = '';
    await post(`/api/sessions/${activeId}/messages`, { text, tag: 'task' });
  } catch (err) { say('发送失败：' + err.message); }
};

$('stop').onclick = async () => {
  if (!activeId) return;
  try { await post(`/api/sessions/${activeId}/stop`); } catch (e) { say('停止失败：' + e.message); }
};

$('text').onkeydown = e => {
  if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); $('composer').requestSubmit(); }
};

$('loop-workspace').onchange = refreshLedger;
$('loop-workspace').onblur = refreshLedger;

$('evolve').onclick = async () => {
  const ws = workspaceOf();
  if (!ws) return say('先填工作目录');
  try {
    const s = await post('/api/loop/evolve', { workspace: ws, model: $('loop-model').value.trim(), effort: '' });
    renderEvolve(s);
    clearTimeout(evolveTimer);
    evolveTimer = setTimeout(pollEvolve, 2000);
  } catch (e) { say('演进启动失败：' + e.message); }
};

$('evolve-stop').onclick = async () => {
  try { await post('/api/loop/evolve/stop', { workspace: workspaceOf() }); } catch (e) { say('停止失败：' + e.message); }
};

$('auto-evolve').onchange = async e => {
  const box = e.target;
  try {
    const r = await post('/api/loop/auto', { workspace: workspaceOf(), enabled: box.checked });
    say(r.enabled ? '自动演进已开启' : '自动演进已关闭');
    pollEvolve();
  } catch (err) {
    box.checked = !box.checked;
    say('设置失败：' + err.message);
  }
};

$('rollback').onclick = async () => {
  const ws = workspaceOf();
  if (!ws || !confirm('回滚到上一版？会写成新版本，旧版本文件不动。')) return;
  try {
    const r = await post('/api/loop/rollback', { workspace: ws });
    say('已回滚，当前 v' + r.version);
    refreshLedger();
  } catch (e) { say('回滚失败：' + e.message); }
};

$('rule-form').onsubmit = async e => {
  e.preventDefault();
  const ws = workspaceOf();
  const name = $('rule-name').value.trim();
  if (!ws || !name) return say('工作目录和规则 id 都要填');
  try {
    const r = await post('/api/loop/rule', { workspace: ws, name, text: $('rule-text').value.trim() });
    $('rule-name').value = '';
    $('rule-text').value = '';
    say('已登记 ' + r.id + '（下一轮演进的候选里可被启用）');
    refreshLedger();
  } catch (err) { say('登记失败：' + err.message); }
};

// ---------- 启动 ----------

(async function boot() {
  const saved = localStorage.getItem('openloop.workspace') || '';
  $('loop-workspace').value = saved;

  try {
    caps = await api('/api/capabilities');
  } catch (e) {
    say('拿不到能力位：' + e.message);
    return;
  }

  $('role').textContent = caps.worker ? 'A · 阳（Worker + Loop）' : 'I · 阴（Loop）';
  $('sessions-pane').style.display = caps.worker ? '' : 'none';
  $('chat-pane').style.display = caps.worker ? '' : 'none';
  if (!caps.worker) document.querySelector('main').style.gridTemplateColumns = '1fr';
  if (!caps.worker) $('loop-pane').style.borderLeft = 'none';

  await refreshLedger();
  if (!caps.worker) return;

  await loadSessions();
  if (sessions.length) await openSession(sessions[0].id);
})();
