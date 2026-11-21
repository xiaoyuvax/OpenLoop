using System.Text;
using OpenLoop.Service.Config;
using OpenLoop.Service.Logging;
using OpenLoop.Service.Storage;

namespace OpenLoop.Service.Worker
{
    /// <summary>
    /// 协作：发起方分解任务 → 起若干同工作区的平级会话 → 自己只做监测与协调。
    /// 会话表在主文件里；这里只放协作关系的登记、启动与轮询。
    /// 协作会话不是子 agent：没有父子调用栈，只有一条「谁派的活」的记账。
    /// </summary>
    public sealed partial class WorkerSessionHost : ICollabHost
    {
        /// <summary>协作会话 id → 发起方 id。</summary>
        private readonly Dictionary<string, string> _collabParent = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>发起方 id → 监测循环的取消源（同一发起方只留一个循环）。</summary>
        private readonly Dictionary<string, CancellationTokenSource> _collabLoops = new(StringComparer.OrdinalIgnoreCase);

        private string ParentOf(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return null;
            lock (_lock) return _collabParent.TryGetValue(sessionId, out string parent) ? parent : null;
        }

        /// <summary>按任务清单起协作会话：同工作区、平级；reuse 时把发起方已完成的轮次拷过去复用前缀缓存。</summary>
        public string StartCollab(string initiatorId, IReadOnlyList<CollabTask> tasks)
        {
            if (string.IsNullOrWhiteSpace(initiatorId)) return "缺少发起方会话 id";
            if (tasks == null || tasks.Count == 0) return "没有任务";

            Session initiator;
            lock (_lock)
                if (!_sessions.TryGetValue(initiatorId, out initiator)) return "找不到发起方会话：" + initiatorId;

            var settings = initiator.Settings;
            bool reuse = settings?.Invoke()?.CollabReuseContext ?? true;
            string workspace = initiator.Workspace;

            var sb = new StringBuilder();
            sb.Append("已启动 ").Append(tasks.Count)
              .AppendLine(" 个协作会话（同工作区，平级；各自独立跑，不是你的子 agent）：");
            foreach (var task in tasks)
            {
                string id = Guid.NewGuid().ToString("N")[..8];
                string model = string.IsNullOrWhiteSpace(task.Model) ? initiator.Model : task.Model;
                CreateCollab(id, workspace, model, initiator.Effort, settings, reuse ? initiatorId : null);
                lock (_lock) _collabParent[id] = initiatorId;
                ChatHistory.Touch(workspace, id, string.IsNullOrWhiteSpace(task.Title) ? null : task.Title, 0);

                string prompt = string.IsNullOrWhiteSpace(task.Context)
                    ? task.Prompt
                    : "## 你被交代的上下文\n" + task.Context.Trim() + "\n\n## 你要做的\n" + task.Prompt;
                Send(id, "task", prompt);

                sb.Append("- ").Append(id).Append(' ').AppendLine(task.Title);
            }
            sb.Append("下一步：调用 CollabMonitor（省略 sessions 即盯这些会话），它们的进展会作为仿用户消息进入你的队列。");
            return sb.ToString();
        }

        /// <summary>登记监测：之后按设置的间隔把各会话状态作为仿用户消息插进发起方队列，全部收尾后自动停。</summary>
        public string MonitorCollab(string initiatorId, IReadOnlyList<string> sessionIds)
        {
            if (string.IsNullOrWhiteSpace(initiatorId)) return "缺少发起方会话 id";

            Session initiator;
            lock (_lock)
                if (!_sessions.TryGetValue(initiatorId, out initiator)) return "找不到发起方会话：" + initiatorId;

            var ids = (sessionIds ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
            if (ids.Count == 0)
                lock (_lock)
                    ids = _collabParent
                        .Where(p => string.Equals(p.Value, initiatorId, StringComparison.OrdinalIgnoreCase))
                        .Select(p => p.Key).ToList();

            if (ids.Count == 0) return "没有可监测的协作会话：先用 CollabStart 启动，或用 sessions 指定 id";

            var watch = new CollabWatch { Initiator = initiatorId, Workspace = initiator.Workspace, Sessions = ids };
            CollabSidecar.Save(watch);
            StartCollabLoop(watch);

            int seconds = PollSeconds(initiatorId);
            return $"已开始监测 {ids.Count} 个会话（每 {seconds} 秒检查一次，有进展才汇报；全部结束后自动停）："
                   + string.Join("、", ids);
        }

        /// <summary>读一个会话的轮次与当前状态；不给 id 时列发起方工作区的会话。</summary>
        public string ReadSession(string initiatorId, string sessionId, int offset, int count)
        {
            Session initiator = null;
            lock (_lock) _sessions.TryGetValue(initiatorId ?? "", out initiator);
            string workspace = initiator?.Workspace ?? AppSettings.Load()?.Workspace ?? "";

            var rows = List(summary: true)
                .Where(s => string.Equals(s.Workspace, workspace, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (string.IsNullOrWhiteSpace(sessionId)) return Listing(rows);

            var match = rows.FirstOrDefault(s => string.Equals(s.Id, sessionId, StringComparison.OrdinalIgnoreCase))
                        ?? rows.FirstOrDefault(s => s.Id.StartsWith(sessionId, StringComparison.OrdinalIgnoreCase));
            if (match == null) return $"没有找到会话：{sessionId}\n" + Listing(rows);

            return Describe(Get(match.Id) ?? match, offset, count);
        }

        /// <summary>
        /// agent 自动插入一条消息（协作监测汇报）：空闲直接开跑，运行中进队列并标记 Auto，
        /// 界面据此用特殊底色区分用户敲的与 agent 塞的。已停止（暂停）的会话只排队，
        /// 不因监测汇报自己动起来。
        /// </summary>
        public bool EnqueueAuto(string id, string text, string tag = "collab")
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            lock (_lock)
            {
                if (!_sessions.TryGetValue(id ?? "", out var session)) return false;
                session.Revision++;
                if (session.State == Running || session.Paused)
                    session.Pending.Add(new PendingSend { Text = text, Tag = tag ?? "collab", Auto = true });
                else
                    Start(session, tag ?? "collab", text);
            }

            Changed?.Invoke(id);
            return true;
        }

        /// <summary>
        /// 建一个协作会话：与发起方同工作区、平级。inheritFrom 非空且模型相同时把发起方
        /// 已完成的轮次拷过去——前缀一致，服务端缓存能直接复用；不同模型拷过去只有坏处，不拷。
        /// </summary>
        public WorkerSessionSnapshot CreateCollab(string id, string workspace, string model, string effort,
            Func<AppSettings> settings, string inheritFrom)
        {
            var snapshot = Create(workspace, model, effort, settings, id);
            if (string.IsNullOrWhiteSpace(inheritFrom)) return snapshot;

            lock (_lock)
            {
                if (!_sessions.TryGetValue(id ?? "", out var target)) return snapshot;
                if (!_sessions.TryGetValue(inheritFrom, out var source)) return snapshot;
                if (!string.Equals(source.Model, target.Model, StringComparison.OrdinalIgnoreCase)) return snapshot;

                foreach (var turn in source.Turns)
                {
                    // 正在跑的这一轮还没有最终 transcript，拷过去只会塞进半截正文
                    if (turn.Streaming) break;
                    target.Turns.Add(new WorkerTurn
                    {
                        User = turn.User,
                        Assistant = turn.Assistant,
                        Reasoning = turn.Reasoning,
                        ToolLines = turn.ToolLines,
                        Timeline = turn.Timeline,
                        Phases = turn.Phases?.ToList() ?? [],
                        Steps = turn.Steps,
                        Ms = turn.Ms,
                        Transcript = turn.Transcript?.ToList()
                    });
                }
                target.HistoryPruned = true;
                target.Revision++;
            }
            return snapshot;
        }

        /// <summary>发起方会话重建（A 重启后的探活接管）时接着盯上次登记的协作。</summary>
        private void ResumeCollab(string initiatorId, string workspace)
        {
            if (string.IsNullOrWhiteSpace(initiatorId)) return;
            var watch = CollabSidecar.Load(workspace, initiatorId);
            if (watch == null || watch.Sessions.Count == 0) return;
            StartCollabLoop(watch);
        }

        private int PollSeconds(string initiatorId)
        {
            Session session;
            lock (_lock) _sessions.TryGetValue(initiatorId ?? "", out session);
            int seconds = session?.Settings?.Invoke()?.CollabPollSeconds ?? 30;
            return Math.Clamp(seconds, 5, 3600);
        }

        /// <summary>轮询用的轻量投影：状态与计数照旧，轮次只留最后一轮（状态行要的那几个字段）。</summary>
        private WorkerSessionSnapshot Tail(string id)
        {
            lock (_lock)
            {
                if (!_sessions.TryGetValue(id ?? "", out var session)) return null;
                var snapshot = brief(session);
                var last = session.Turns.LastOrDefault();
                if (last != null)
                    snapshot.Turns =
                    [
                        new WorkerTurn
                        {
                            Assistant = last.Assistant,
                            ToolLines = last.ToolLines,
                            Status = last.Status,
                            Failed = last.Failed,
                            Steps = last.Steps
                        }
                    ];
                return snapshot;
            }
        }

        private void StartCollabLoop(CollabWatch watch)
        {
            var cts = new CancellationTokenSource();
            lock (_lock)
            {
                if (_collabLoops.TryGetValue(watch.Initiator, out var old)) old.Cancel();
                _collabLoops[watch.Initiator] = cts;
            }
            _ = Task.Run(() => CollabLoopAsync(watch, cts));
        }

        /// <summary>
        /// 监测循环：每轮把各会话状态拼成一条消息插进发起方队列（有变化才插，免得刷屏）；
        /// 全部会话都不再运行时汇报最后一次并收工。发起方会话消失也收工。
        /// </summary>
        private async Task CollabLoopAsync(CollabWatch watch, CancellationTokenSource cts)
        {
            var ct = cts.Token;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(PollSeconds(watch.Initiator)), ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested) break;

                    if (Get(watch.Initiator) == null) break;

                    // 只要状态 + 最后一轮：Get 会把整段历史都投影出来，30 秒一次的轮询不该付这个代价
                    var snapshots = watch.Sessions.Select(Tail).ToList();
                    string signature = CollabStatus.Signature(snapshots);
                    bool alive = snapshots.Any(s => s != null && s.State == Running);
                    bool changed = !string.Equals(signature, watch.Signature, StringComparison.Ordinal);

                    if (!changed && alive) continue;
                    if (!changed && !alive) break;

                    var sb = new StringBuilder(alive ? "[协作监测] 有会话仍在进行：" : "[协作监测] 全部会话已结束：");
                    for (int i = 0; i < watch.Sessions.Count; i++)
                        sb.AppendLine().Append(CollabStatus.Line(snapshots[i]));

                    watch.Signature = signature;
                    CollabSidecar.Save(watch);
                    EnqueueAuto(watch.Initiator, sb.ToString(), "collab");

                    if (!alive) break;
                }
            }
            catch (OperationCanceledException)
            {
                // 被新的监测请求或会话结束取代
            }
            catch (Exception ex)
            {
                AppLog.Warn("协作监测失败：" + ex.Message);
            }
            finally
            {
                lock (_lock)
                    if (_collabLoops.TryGetValue(watch.Initiator, out var current) && ReferenceEquals(current, cts))
                        _collabLoops.Remove(watch.Initiator);
                // 全部收尾后不留档案：下次重建会话不会把已经结束的协作又捞起来
                if (Get(watch.Initiator) == null || !watch.Sessions.Any(id => Get(id)?.State == Running))
                    CollabSidecar.Delete(watch.Workspace, watch.Initiator);
                cts.Dispose();
            }
        }

        /// <summary>会话清单：id 在前，主控要拿它去 CollabMonitor / ReadSession。</summary>
        internal static string Listing(IReadOnlyList<WorkerSessionSnapshot> sessions)
        {
            if (sessions == null || sessions.Count == 0) return "（没有会话）";
            var sb = new StringBuilder("会话（session 可填 id 或前缀）：");
            foreach (var s in sessions)
            {
                sb.AppendLine();
                sb.Append("- ").Append(s.Id).Append(' ').Append(CollabStatus.StateText(s.State));
                if (!string.IsNullOrWhiteSpace(s.Parent)) sb.Append(" · 发起方 ").Append(s.Parent);
                sb.Append(" · ").Append(s.Steps).Append(" 步 · ").Append(s.Workspace);
            }
            return sb.ToString();
        }

        private static string Describe(WorkerSessionSnapshot s, int offset, int count)
        {
            var sb = new StringBuilder();
            sb.Append("会话 ").Append(s.Id).Append('（').Append(CollabStatus.StateText(s.State))
              .Append(" · ").Append(s.Steps).Append(" 步 · 模型 ").Append(s.Model).AppendLine("）");
            if (!string.IsNullOrWhiteSpace(s.Outcome)) sb.Append("outcome: ").AppendLine(s.Outcome);

            var turns = s.Turns ?? [];
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
                sb.Append(i).Append(". 用户：").AppendLine(CollabStatus.Clip(turn.User, 300));
                if (!string.IsNullOrWhiteSpace(turn.ToolLines))
                    sb.Append("   工具：").AppendLine(CollabStatus.Clip(turn.ToolLines, 600));
                sb.Append("   回复：").AppendLine(CollabStatus.Clip(turn.Assistant, 500));
            }
            if (end <= turns.Count)
                sb.Append("（还有 ").Append(turns.Count - end + 1).Append(" 轮，继续读请设 offset=").Append(end).Append('）');
            return sb.ToString().TrimEnd();
        }
    }
}
