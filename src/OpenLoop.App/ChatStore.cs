using System.Runtime.CompilerServices;
using ChatFlow;
using OpenLoop.Core.Messages;
using OpenLoop.Service.Storage;

namespace OpenLoop.App
{
    /// <summary>
    /// 对话历史按工作目录分开存放，放 %APPDATA%，不进工作区。
    /// 每轮同时保存该轮结束时的完整消息列表（含 tool-call / tool-result），恢复后模型能看到自己做过什么。
    /// </summary>
    internal static class ChatStore
    {
        private static readonly ConditionalWeakTable<ChatTurn, List<LlmMessage>> Transcripts = new();

        /// <summary>记下这一轮结束时的消息列表；下一次 Save 会一起落盘，BuildHistory 用它当上下文。</summary>
        public static void AttachTranscript(ChatTurn turn, IReadOnlyList<LlmMessage> messages)
        {
            if (turn == null || messages == null) return;
            var list = messages.Where(m => m != null
                    && !string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(m.Content))
                .Select(m => new LlmMessage(m.Role, m.Content))
                .ToList();
            if (list.Count > 0) Transcripts.AddOrUpdate(turn, list);
        }

        /// <summary>这一轮落盘的消息列表；旧会话没有时为 null。</summary>
        public static IReadOnlyList<LlmMessage> Transcript(ChatTurn turn)
            => turn != null && Transcripts.TryGetValue(turn, out var list) ? list : null;

        public static List<ChatTurn> Load(string workspace, string kind = null)
        {
            var turns = new List<ChatTurn>();
            if (string.IsNullOrWhiteSpace(workspace)) return turns;
            var entries = ChatHistory.Read(workspace, kind);
            foreach (var entry in entries)
            {
                if (entry == null || (string.IsNullOrEmpty(entry.User) && string.IsNullOrEmpty(entry.Assistant)))
                    continue;
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", entry.User ?? ""),
                    Assistant = new ChatMessage("assistant", entry.Assistant ?? ""),
                    ReasoningText = entry.Reasoning ?? "",
                    ToolCallLines = entry.ToolLines ?? "",
                    ToolTraceText = entry.ToolLines ?? "",
                    TimelineText = entry.Timeline ?? "",
                    ToolStepCount = entry.Steps,
                    ToolTotalMs = entry.Ms
                };
                if (entry.Phases is { Count: > 0 })
                    foreach (var phase in entry.Phases)
                        turn.Phases.Add((phase?.Thinking ?? "", phase?.Answer ?? ""));
                if (entry.Messages is { Count: > 0 })
                    AttachTranscript(turn, entry.Messages
                        .Where(m => !string.IsNullOrEmpty(m?.Content))
                        .Select(m => new LlmMessage(m.Role ?? "user", m.Content))
                        .ToList());
                turns.Add(turn);
            }
            return turns;
        }

        public static void Save(string workspace, IEnumerable<ChatTurn> turns, string kind = null,
            string model = null, string effort = null)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return;
            var entries = (turns ?? []).Select(t => new ChatEntry
            {
                User = t.UserText,
                // 已结束的轮次不许落盘成空白：重新打开会话时那会变成一条「静默中断」，
                // 既看不出跑了什么，也没法判断要不要继续。
                Assistant = AssistantOf(t),
                Reasoning = t.ReasoningText,
                ToolLines = t.ToolCallLines,
                Timeline = t.TimelineText,
                Phases = t.Phases.Select(p => new ChatPhaseEntry { Thinking = p.Thinking, Answer = p.Answer }).ToList(),
                Steps = t.ToolStepCount,
                Ms = t.ToolTotalMs,
                Model = model,
                Effort = effort,
                Messages = Transcripts.TryGetValue(t, out var transcript)
                    ? transcript.Select(m => new ChatMessageEntry { Role = m.Role, Content = m.Content }).ToList()
                    : null
            }).ToList();

            ChatHistory.Write(workspace, kind, entries);
        }

        /// <summary>这一轮要落盘的正文；已结束却什么都没留下时补一句可操作的说明。</summary>
        private static string AssistantOf(ChatTurn turn)
        {
            string text = turn.AssistantText;
            if (!string.IsNullOrWhiteSpace(text)) return text;
            if (turn.Streaming) return text;   // 正在跑的轮次照旧留空，收尾时会再存一次
            int steps = turn.ToolStepCount;
            return OpenLoop.Core.Messages.ServerNotice.BlankTurn
                   + (steps > 0 ? $"，已执行 {steps} 步；说「继续」会接着做。）" : "；说「继续」会重新开一轮。）");
        }

        /// <summary>
        /// 下一轮要带的上下文：以最近一份 transcript 为准（与 Worker 一致），
        /// 没有 transcript 的旧会话退回 user/assistant 正文。
        /// </summary>
        public static List<LlmMessage> History(IReadOnlyList<ChatTurn> turns)
        {
            var list = new List<LlmMessage>();
            if (turns == null) return list;

            for (int i = turns.Count - 1; i >= 0; i--)
            {
                var stored = Transcript(turns[i]);
                if (stored == null || stored.Count == 0) continue;

                foreach (var message in stored)
                {
                    if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new LlmMessage(message.Role, message.Content));
                }
                for (int j = i + 1; j < turns.Count; j++) AppendText(list, turns[j]);
                return list;
            }

            foreach (var turn in turns) AppendText(list, turn);
            return list;
        }

        private static void AppendText(List<LlmMessage> list, ChatTurn turn)
        {
            if (!string.IsNullOrWhiteSpace(turn?.UserText)) list.Add(new LlmMessage("user", turn.UserText));
            // 失败轮的正文是接口报错/停止提示，不是模型说过的话
            if (!string.IsNullOrWhiteSpace(turn?.AssistantText) && !ServerNotice.IsNotice(turn.AssistantText))
                list.Add(new LlmMessage("assistant", turn.AssistantText));
        }

        /// <summary>
        /// 冷启动整理历史：文件空闲超过一小时（服务端前缀缓存必然过期）或换了模型时做一次。
        /// 会话进行中不得调用，否则会改写已提交的前缀、打断前缀缓存。
        /// </summary>
        public static int PruneCold(string workspace, string kind, string model, IReadOnlyList<ChatTurn> turns)
        {
            if (string.IsNullOrWhiteSpace(workspace) || turns == null || turns.Count == 0) return 0;

            string path = ChatHistory.PathFor(workspace, kind);
            bool idle = !File.Exists(path)
                || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromMinutes(60);
            bool modelChanged = ChatHistory.Read(workspace, kind).Any(e =>
                !string.IsNullOrWhiteSpace(e?.Model)
                && !string.Equals(e.Model, model, StringComparison.OrdinalIgnoreCase));
            if (!idle && !modelChanged) return 0;

            for (int i = turns.Count - 1; i >= 0; i--)
            {
                if (!Transcripts.TryGetValue(turns[i], out var stored) || stored.Count == 0) continue;
                int changed = OpenLoop.Core.Agent.HistoryPruner.Prune(stored);
                if (changed > 0)
                    OpenLoop.Service.Logging.AppLog.Info(
                        $"Loop 会话冷启动整理历史：删除/改写 {changed} 条（换模型={modelChanged}）");
                return changed;
            }
            return 0;
        }

        /// <summary>删除一个会话的对话文件与索引项。</summary>
        public static void Delete(string workspace, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(sessionId)) return;
            try
            {
                File.Delete(ChatHistory.PathFor(workspace, sessionId));
            }
            catch
            {
            }
            ChatHistory.Forget(sessionId);
        }

        /// <summary>
        /// 会话索引：文件名是哈希，重启后没有索引就无从把会话找回来。
        /// 每轮落盘由 A 自己登记（<c>WorkerSessionHost.Persist</c>）；壳只在改名、
        /// 以及「A 不知道的会话」（关窗时被淘汰的侧线对话）落盘时写，别在存对话时顺手写。
        /// </summary>
        public static void Touch(string workspace, string sessionId, string title = null, int? steps = null)
            => ChatHistory.Touch(workspace, sessionId, title, steps);

        /// <summary>旧索引没有步数：读一次对话文件补齐（不动最后活动时间），返回 id → 步数。</summary>
        public static Dictionary<string, int> BackfillSteps() => ChatHistory.BackfillSteps();

        /// <summary>Loop 会话的 id：<c>loop-xxxxxxxx</c>，旧版是字面量 <c>loop</c>。</summary>
        internal static bool IsLoopSessionId(string id)
            => string.Equals(id, "loop", StringComparison.OrdinalIgnoreCase)
               || id?.StartsWith("loop-", StringComparison.OrdinalIgnoreCase) == true;

        /// <summary>有对话文件的已存会话；最近活动的在前。开始时间与最后活动时间都取自索引。</summary>
        public static List<SavedSession> SavedSessions()
        {
            var result = new List<SavedSession>();
            try
            {
                foreach (var item in ChatHistory.Index().OrderByDescending(e => e.UpdatedAt))
                {
                    if (item == null || string.IsNullOrEmpty(item.Id) || string.IsNullOrEmpty(item.Workspace)) continue;
                    // Loop 会话归 loops.json 管；旧版「转交」误写进这份索引的 loop id 不再当 Worker 会话恢复
                    if (IsLoopSessionId(item.Id)) continue;
                    if (!File.Exists(ChatHistory.PathFor(item.Workspace, item.Id))) continue;
                    // 旧索引没有 CreatedAt：退回 UpdatedAt（至少不是重启时刻）
                    var created = item.CreatedAt == default ? item.UpdatedAt : item.CreatedAt;
                    result.Add(new SavedSession(item.Id, item.Workspace, item.Title ?? "",
                        created.ToLocalTime(), item.UpdatedAt.ToLocalTime(), item.Steps));
                }
            }
            catch
            {
            }
            return result;
        }

        internal sealed record SavedSession(
            string Id, string Workspace, string Title, DateTime StartedAt, DateTime LastActiveAt, int Steps);
    }
}
