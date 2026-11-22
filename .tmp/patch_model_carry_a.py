# -*- coding: utf-8 -*-
# MainForm.cs：让 /complain、/fork、「转给 worker/Loop」、协作派生的会话沿用来源会话的模型与强度。
import io
import sys


def patch(path, pairs):
    with io.open(path, 'rb') as f:
        raw = f.read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig').replace('\r\n', '\n')
    for i, (old, new) in enumerate(pairs):
        n = text.count(old)
        if n != 1:
            print('FAIL %s pattern#%d count=%d' % (path, i, n))
            print(repr(old[:160]))
            sys.exit(1)
        text = text.replace(old, new)
    out = text.replace('\n', '\r\n')
    with io.open(path, 'wb') as f:
        if bom:
            f.write(b'\xef\xbb\xbf')
        f.write(out.encode('utf-8'))
    print('OK', path, '(%d patterns)' % len(pairs))


P = 'src/OpenLoop.App/MainForm.cs'
mf = []

# R1: /complain（Worker 页来源）带上来源会话的模型与强度
mf.append((
"""        private void OnComplaint(Session session, string text)
            => PostComplaint(session.Workspace, text);

        private void PostComplaint(string workspace, string text)
            => EnsureLoop(workspace, select: false, sessionId: "",
                title: "反馈 " + DateTime.Now.ToString("HH:mm"))?.Post(text);
""",
"""        private void OnComplaint(Session session, string text)
            => PostComplaint(session.Workspace, session.View?.Model, session.View?.Effort, text);

        private void PostComplaint(string workspace, string model, string effort, string text)
        {
            var view = EnsureLoop(workspace, select: false, sessionId: "",
                title: "反馈 " + DateTime.Now.ToString("HH:mm"));
            if (view == null) return;
            // 派生会话沿用来源会话的模型与强度，不悄悄落回全局设置（无来源模型时传 null 走默认）
            view.SetModel(model);
            view.SetEffort(effort);
            view.Post(text);
        }
"""))

# R5b: Puppet 板的 /complain 没有来源模型，传 null 走默认
mf.append((
"""            view.ComplaintRequested += text =>
                PostComplaint(_lastSession?.Workspace ?? SourceSync.InstallRoot, text);
""",
"""            view.ComplaintRequested += text =>
                PostComplaint(_lastSession?.Workspace ?? SourceSync.InstallRoot,
                    _lastSession?.View?.Model, _lastSession?.View?.Effort, text);
"""))

# R2: Loop 页「转给 worker」带上来源模型
mf.append((
"            view.TransferToWorker = turns => TransferToWorker(target, turns);\n",
"            view.TransferToWorker = turns => TransferToWorker(target, turns, view.Model, view.Effort);\n"))

# R3: Loop 页再 complain 时带上当前会话模型
mf.append((
"            view.ComplaintRequested += text => PostComplaint(target, text);\n",
"            view.ComplaintRequested += text => PostComplaint(target, view.Model, view.Effort, text);\n"))

# R4: 协作会话沿用发起方模型与强度
mf.append((
"""                // 沿用上下文：把发起方已完成的轮次写到新会话名下，前缀一致才能复用服务端缓存。
                // Loop 会话跟随设置里的模型，模型对不上时拷过去只有坏处，不拷。
                if (_settings.CollabReuseContext && string.Equals(view.Model, _settings.Model,
                        StringComparison.OrdinalIgnoreCase))
                    ChatStore.Save(target, view.Turns.Where(t => !t.Streaming), id, view.Model,
                        _settings.ReasoningEffort);
                return EnsureLoop(target, select: false, sessionId: id, title: task.Title);
""",
"""                // 沿用上下文：把发起方已完成的轮次写到新会话名下，前缀一致才能复用服务端缓存。
                // 协作会话沿用发起方的模型与强度（见下），与发起方一致，拷贝只有好处。
                if (_settings.CollabReuseContext)
                    ChatStore.Save(target, view.Turns.Where(t => !t.Streaming), id, view.Model, view.Effort);
                var created = EnsureLoop(target, select: false, sessionId: id, title: task.Title);
                created?.SetModel(view.Model);
                created?.SetEffort(view.Effort);
                return created;
"""))

# R5: /fork（Worker）沿用来源会话的模型与强度
mf.append((
"""            var session = CreateSession(source.Workspace);
            //克隆可见历史：新会话既是「带着上下文」起步，后续追问也仍带上下文
            session.Book.ReplaceAll(source.Book.Turns.Select(CloneTurn), null);
""",
"""            var session = CreateSession(source.Workspace);
            var view = EnsureView(session);
            // /fork 沿用来源会话的模型与强度（与 /new 同一套）
            view.SetModel(source.View?.Model);
            view.SetEffort(source.View?.Effort);
            //克隆可见历史：新会话既是「带着上下文」起步，后续追问也仍带上下文
            session.Book.ReplaceAll(source.Book.Turns.Select(CloneTurn), null);
"""))

# R6: /fork（Loop）沿用来源会话的模型与强度
mf.append((
"""            ChatStore.Save(workspace, source.Turns.Select(CloneTurn).ToList(), id);
            ChatStore.Touch(workspace, id, "分叉 " + DateTime.Now.ToString("HH:mm"));
            EnsureLoop(workspace, select: true, sessionId: id)?.Send(request);
""",
"""            ChatStore.Save(workspace, source.Turns.Select(CloneTurn).ToList(), id, source.Model, source.Effort);
            ChatStore.Touch(workspace, id, "分叉 " + DateTime.Now.ToString("HH:mm"));
            var view = EnsureLoop(workspace, select: true, sessionId: id);
            if (view == null) return;
            view.SetModel(source.Model);
            view.SetEffort(source.Effort);
            view.Send(request);
"""))

# R7: 转给 worker——落盘带模型，会话视图也沿用
mf.append((
"""        private bool TransferToWorker(string workspace, IReadOnlyList<ChatTurn> turns)
        {
            if (string.IsNullOrWhiteSpace(workspace) || turns == null || turns.Count == 0) return false;
            workspace = Paths.Normalize(workspace);
            string id = Guid.NewGuid().ToString("N")[..8];
            string title = "转交 " + DateTime.Now.ToString("HH:mm");
            var cloned = turns.Select(CloneTurn).ToList();
            ChatStore.Save(workspace, cloned, id);
            ChatStore.Touch(workspace, id, title);
            ShowSession(CreateSession(workspace, id, title));
            return true;
        }
""",
"""        private bool TransferToWorker(string workspace, IReadOnlyList<ChatTurn> turns, string model, string effort)
        {
            if (string.IsNullOrWhiteSpace(workspace) || turns == null || turns.Count == 0) return false;
            workspace = Paths.Normalize(workspace);
            string id = Guid.NewGuid().ToString("N")[..8];
            string title = "转交 " + DateTime.Now.ToString("HH:mm");
            var cloned = turns.Select(CloneTurn).ToList();
            ChatStore.Save(workspace, cloned, id, model, effort);
            ChatStore.Touch(workspace, id, title);
            var session = CreateSession(workspace, id, title);
            ShowSession(session);
            // 派生会话沿用来源会话的模型与强度，不悄悄落回全局设置（空值走落盘历史里的模型）
            session.View?.SetModel(model);
            session.View?.SetEffort(effort);
            return true;
        }
"""))

# R8: 转给 Loop——同上
mf.append((
"""        private bool TransferToLoop(string workspace, IReadOnlyList<ChatTurn> turns)
        {
            if (string.IsNullOrWhiteSpace(workspace) || turns == null || turns.Count == 0) return false;
            workspace = Paths.Normalize(workspace);
            string id = "loop-" + Guid.NewGuid().ToString("N")[..8];
            string title = "转交 " + DateTime.Now.ToString("HH:mm");
            ChatStore.Save(workspace, turns.Select(CloneTurn).ToList(), id);
            // Loop 会话只登记在 loops.json：写 chats/index.json 会让它被当成 Worker 会话恢复出来
            LoopIndex.Rename(workspace, id, title);
            return EnsureLoop(workspace, true, id) != null;
        }
""",
"""        private bool TransferToLoop(string workspace, IReadOnlyList<ChatTurn> turns, string model, string effort)
        {
            if (string.IsNullOrWhiteSpace(workspace) || turns == null || turns.Count == 0) return false;
            workspace = Paths.Normalize(workspace);
            string id = "loop-" + Guid.NewGuid().ToString("N")[..8];
            string title = "转交 " + DateTime.Now.ToString("HH:mm");
            ChatStore.Save(workspace, turns.Select(CloneTurn).ToList(), id, model, effort);
            // Loop 会话只登记在 loops.json：写 chats/index.json 会让它被当成 Worker 会话恢复出来
            LoopIndex.Rename(workspace, id, title);
            var view = EnsureLoop(workspace, true, id);
            if (view == null) return false;
            view.SetModel(model);
            view.SetEffort(effort);
            return true;
        }
"""))

# R9: 旁路被挤走/关窗落盘——带上旁路用的模型与强度
mf.append((
"""        private void OnSideWorkEvicted(string workspace, IReadOnlyList<ChatTurn> turns)
        {
            if (turns == null || turns.Count == 0) return;
            int steps = turns[^1].ToolStepCount;
            if (_closing)
            {
                // 关窗时只落盘，不建视图、不再请求模型
                string id = Guid.NewGuid().ToString("N")[..8];
                ChatStore.Save(workspace, turns, id);
                ChatStore.Touch(workspace, id, null, steps);
                return;
            }
            var session = CreateSession(workspace, steps: steps);
            session.Book.ReplaceAll(turns, null);
            ChatStore.Save(workspace, turns, session.Id);
            ChatStore.Touch(workspace, session.Id, null, steps);
            EnsureView(session).RequestTitle();
            RefreshOverview();
        }
""",
"""        private void OnSideWorkEvicted(string workspace, string model, string effort, IReadOnlyList<ChatTurn> turns)
        {
            if (turns == null || turns.Count == 0) return;
            int steps = turns[^1].ToolStepCount;
            if (_closing)
            {
                // 关窗时只落盘，不建视图、不再请求模型
                string id = Guid.NewGuid().ToString("N")[..8];
                ChatStore.Save(workspace, turns, id, model, effort);
                ChatStore.Touch(workspace, id, null, steps);
                return;
            }
            var session = CreateSession(workspace, steps: steps);
            session.Book.ReplaceAll(turns, null);
            ChatStore.Save(workspace, turns, session.Id, model, effort);
            ChatStore.Touch(workspace, session.Id, null, steps);
            var view = EnsureView(session);
            // 派生会话沿用旁路用的模型与强度，不悄悄落回全局设置
            view.SetModel(model);
            view.SetEffort(effort);
            view.RequestTitle();
            RefreshOverview();
        }
"""))

patch(P, mf)
