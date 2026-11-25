using System.Text;
using OpenLoop.Service.Worker;

namespace OpenLoop.App
{
    /// <summary>
    /// Loop 侧的协作宿主：协作会话是同工作区的其他 Loop 会话（平级，不是子 agent，没有父子调用栈）。
    /// 会话本体在壳里（LoopView），所以启动与列举都走宿主注入的委托；
    /// 监测汇报作为仿用户消息进发起方队列（PendingItem.Auto，队列栏用特殊底色标出）。
    /// shortcut: 监测关系只在内存里——壳重启后由发起方重新 CollabMonitor；只有 A 侧落盘（跨 A 重启接管）。
    /// </summary>
    internal sealed class LoopCollabHost : ICollabHost
    {
        private readonly LoopView _initiator;
        private readonly Func<CollabTask, LoopView> _open;
        private readonly Func<IReadOnlyList<LoopView>> _peers;

        /// <summary>本会话启动过的协作会话 id（CollabMonitor 省略 sessions 时盯这些）。</summary>
        private readonly List<string> _spawned = [];

        private readonly Dictionary<string, CancellationTokenSource> _loops = new(StringComparer.OrdinalIgnoreCase);

        public LoopCollabHost(LoopView initiator, Func<CollabTask, LoopView> open,
            Func<IReadOnlyList<LoopView>> peers)
        {
            _initiator = initiator;
            _open = open;
            _peers = peers;
        }

        public string StartCollab(string initiatorId, IReadOnlyList<CollabTask> tasks)
        {
            if (_open == null) return "宿主没有提供协作会话的创建方式";
            if (tasks == null || tasks.Count == 0) return "没有任务";

            var sb = new StringBuilder();
            sb.Append("已启动 ").Append(tasks.Count)
              .AppendLine(" 个协作会话（同工作区、平级的 Loop 会话；各自独立跑，不是你的子 agent）：");
            foreach (var task in tasks)
            {
                LoopView view;
                try
                {
                    view = _open(task);
                }
                catch (Exception ex)
                {
                    sb.Append("- （启动失败）").AppendLine(task.Title + "：" + ex.Message);
                    continue;
                }
                if (view == null)
                {
                    sb.Append("- （启动失败）").AppendLine(task.Title);
                    continue;
                }

                _spawned.Add(view.SessionId);
                string prompt = string.IsNullOrWhiteSpace(task.Context)
                    ? task.Prompt
                    : "## 你被交代的上下文\n" + task.Context.Trim() + "\n\n## 你要做的\n" + task.Prompt;
                view.Post(prompt);
                sb.Append("- ").Append(view.SessionId).Append(' ').Append(task.Title);
                // Loop 会话跟随设置里的模型，没有逐会话模型：给了不同的 model 只能忽略，别让它以为生效了
                if (!string.IsNullOrWhiteSpace(task.Model)
                    && !string.Equals(task.Model, view.Model, StringComparison.OrdinalIgnoreCase))
                    sb.Append("（Loop 会话跟随当前模型 ").Append(view.Model).Append("，已忽略 model=").Append(task.Model).Append('）');
                sb.AppendLine();
            }

            sb.Append("下一步：调用 CollabMonitor（省略 sessions 即盯这些会话），它们的进展会作为仿用户消息进入你的队列。");
            return sb.ToString();
        }

        public string MonitorCollab(string initiatorId, IReadOnlyList<string> sessionIds)
        {
            var ids = (sessionIds ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
            if (ids.Count == 0) ids = [.. _spawned];
            if (ids.Count == 0) return "没有可监测的协作会话：先用 CollabStart 启动，或用 sessions 指定 id";

            var known = Peers().Select(v => v.SessionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = ids.Where(id => !known.Contains(id)).ToList();
            ids = ids.Where(known.Contains).ToList();
            if (ids.Count == 0)
                return "没有找到这些会话（它们可能已被关闭）：" + string.Join("、", missing);

            StartLoop(ids);

            int seconds = PollSeconds();
            string text = $"已开始监测 {ids.Count} 个会话（每 {seconds} 秒检查一次，有进展才汇报；全部结束后自动停）："
                          + string.Join("、", ids);
            if (missing.Count > 0) text += "\n没找到（已忽略）：" + string.Join("、", missing);
            return text;
        }

        public string ReadSession(string initiatorId, string sessionId, int offset, int count)
        {
            var peers = Peers();
            if (string.IsNullOrWhiteSpace(sessionId)) return Listing(peers);

            var match = peers.FirstOrDefault(v => string.Equals(v.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                        ?? peers.FirstOrDefault(v => v.SessionId.StartsWith(sessionId, StringComparison.OrdinalIgnoreCase));
            if (match == null) return $"没有找到会话：{sessionId}\n" + Listing(peers);

            return Describe(match, offset, count);
        }

        /// <summary>发起方会话关掉/退出时停止监测。</summary>
        public void Stop()
        {
            lock (_loops)
            {
                foreach (var cts in _loops.Values)
                {
                    try { cts.Cancel(); } catch (ObjectDisposedException) { }
                }
                _loops.Clear();
            }
        }

        private IReadOnlyList<LoopView> Peers()
            => (_peers?.Invoke() ?? [])
                .Where(v => v != null && !ReferenceEquals(v, _initiator) && v.Workspace == _initiator.Workspace)
                .ToList();

        private LoopView Find(string id)
            => Peers().FirstOrDefault(v => string.Equals(v.SessionId, id, StringComparison.OrdinalIgnoreCase));

        private int PollSeconds()
            => Math.Clamp(OpenLoop.Service.Config.AppSettings.Load()?.CollabPollSeconds ?? 30, 5, 3600);

        private void StartLoop(IReadOnlyList<string> ids)
        {
            var cts = new CancellationTokenSource();
            lock (_loops)
            {
                if (_loops.TryGetValue(_initiator.SessionId, out var old)) old.Cancel();
                _loops[_initiator.SessionId] = cts;
            }
            _ = Task.Run(() => MonitorAsync(ids, cts));
        }

        /// <summary>
        /// 监测循环：每轮把各会话状态拼成一条消息插进发起方队列（有变化才插，免得刷屏）；
        /// 全部会话都不再运行时汇报最后一次并收工。发起方视图销毁也收工。
        /// </summary>
        private async Task MonitorAsync(IReadOnlyList<string> ids, CancellationTokenSource cts)
        {
            var ct = cts.Token;
            string signature = "";
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(PollSeconds()), ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested) break;
                    if (_initiator.IsDisposed) break;

                    var views = ids.Select(Find).ToList();
                    string current = Signature(views);
                    bool alive = views.Any(v => v != null && v.Running);
                    bool changed = !string.Equals(current, signature, StringComparison.Ordinal);

                    if (!changed && alive) continue;
                    if (!changed && !alive) break;

                    var sb = new StringBuilder(alive ? "[协作监测] 有会话仍在进行：" : "[协作监测] 全部会话已结束：");
                    foreach (var view in views)
                        sb.AppendLine().Append(Line(view));

                    signature = current;
                    _initiator.EnqueueAuto(sb.ToString());
                    if (!alive) break;
                }
            }
            catch (OperationCanceledException)
            {
                // 被新的监测请求或会话关闭取代
            }
            catch (Exception ex)
            {
                OpenLoop.Service.Logging.AppLog.Warn("Loop 协作监测失败：" + ex.Message);
            }
            finally
            {
                lock (_loops)
                    if (_loops.TryGetValue(_initiator.SessionId, out var current) && ReferenceEquals(current, cts))
                        _loops.Remove(_initiator.SessionId);
                cts.Dispose();
            }
        }

        /// <summary>状态签名：状态 + 轮数 + 步数变了才算有新进展。</summary>
        private static string Signature(IReadOnlyList<LoopView> views)
            => string.Join("|", views.Select(v =>
            {
                if (v == null) return "-";
                var turns = v.TurnSnapshot();
                return $"{v.SessionId}:{v.State}:{turns.Count}:{turns.LastOrDefault()?.ToolStepCount ?? 0}";
            }));

        private static string Line(LoopView view)
        {
            if (view == null) return "- （会话已关闭）";

            var turns = view.TurnSnapshot();
            var last = turns.LastOrDefault();
            string text = $"- {view.SessionId} {StateText(view.State)} · {turns.Count} 轮";
            if (last != null && last.ToolStepCount > 0) text += $" · {last.ToolStepCount} 步";

            if (view.Running)
            {
                if (!string.IsNullOrWhiteSpace(last?.ToolCallLines))
                    text += " · 正在：" + Clip(LastLine(last.ToolCallLines), 160);
            }
            else if (last != null)
            {
                // 失败轮的正文就是接口报错，别只报一句「异常结束」
                bool failed = OpenLoop.Core.Messages.ServerNotice.IsNotice(last.AssistantText);
                text += failed
                    ? " · 错误：" + Clip(last.AssistantText, 240)
                    : " · 结尾：" + Clip(string.IsNullOrWhiteSpace(last.AssistantText) ? last.ToolCallLines : last.AssistantText, 200);
            }
            return text;
        }

        /// <summary>会话标题（不含工作区前缀）：ReadSession 按标题找 Loop 会话时用。</summary>
        internal static string PlainTitle(LoopView view)
        {
            try { return view.Title ?? ""; } catch { return ""; }
        }

        /// <summary>状态词：与 Worker 侧 CollabStatus.StateText 同一套说法。</summary>
        internal static string StateText(SessionState state) => state switch
        {
            SessionState.Running => "运行中",
            SessionState.Done => "已正常结束",
            SessionState.Interrupted => "已停止/异常结束",
            _ => "空闲（尚未开跑）"
        };

        internal static string Listing(IReadOnlyList<LoopView> views)
        {
            if (views == null || views.Count == 0) return "（本工作区没有其他 Loop 会话）";
            var sb = new StringBuilder("本工作区的 Loop 会话（session 可填 id 或前缀）：");
            foreach (var view in views)
                sb.AppendLine().Append(Line(view));
            return sb.ToString();
        }

        internal static string Describe(LoopView view, int offset, int count)
        {
            var turns = view.TurnSnapshot();
            var sb = new StringBuilder();
            sb.Append("会话 ").Append(view.SessionId).Append('（').Append(StateText(view.State))
              .Append(" · ").Append(turns.Count).Append(" 轮 · 模型 ").AppendLine(view.Model + "）");
            if (turns.Count == 0) return sb.Append("还没有轮次。").ToString();
            if (offset > turns.Count) return sb.Append("共 ").Append(turns.Count).Append(" 轮，offset 超出范围。").ToString();

            count = Math.Clamp(count, 1, 50);
            int start = offset > 0 ? offset : Math.Max(1, turns.Count - count + 1);
            int end = Math.Min(turns.Count + 1, start + count);
            sb.Append("第 ").Append(start).Append('-').Append(end - 1)
              .Append(" 轮（共 ").Append(turns.Count).AppendLine(" 轮）：");
            for (int i = start; i < end; i++)
            {
                var turn = turns[i - 1];
                sb.Append(i).Append(". 用户：").AppendLine(Clip(turn.UserText, 300));
                if (!string.IsNullOrWhiteSpace(turn.ToolCallLines))
                    sb.Append("   工具：").AppendLine(Clip(turn.ToolCallLines, 600));
                sb.Append("   回复：").AppendLine(Clip(turn.AssistantText, 500));
            }
            if (end <= turns.Count)
                sb.Append("（还有 ").Append(turns.Count - end + 1).Append(" 轮，继续读请设 offset=").Append(end).Append('）');
            return sb.ToString().TrimEnd();
        }

        private static string LastLine(string text)
        {
            var lines = (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return lines.Length == 0 ? "" : lines[^1].Trim();
        }

        private static string Clip(string text, int max)
            => string.IsNullOrEmpty(text) || text.Length <= max ? text ?? "" : text[..max] + "…";
    }
}
