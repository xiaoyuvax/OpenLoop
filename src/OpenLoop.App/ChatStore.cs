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

        /// <summary>
        /// 会话打开时还没有挂 transcript 的轮次按需回读文件：Load 不再把每轮的 Messages 全量载进内存
        /// （几十 MB 的会话，JSON 解析时间与内存占用都由它贡献）。挂在 <see cref="Transcripts"/> 里的
        /// 仍是完整消息列表，取一次之后就不回读。
        /// </summary>
        private static readonly ConditionalWeakTable<ChatTurn, string> DeferredFiles = new();

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

        /// <summary>
        /// 这一轮落盘的消息列表；旧会话没有时为 null。轮次带延迟回读登记时先回读一次（可能整表为 null）。
        /// </summary>
        public static IReadOnlyList<LlmMessage> Transcript(ChatTurn turn)
        {
            if (turn == null) return null;
            if (Transcripts.TryGetValue(turn, out var list)) return list;
            if (DeferredFiles.TryGetValue(turn, out var file))
            {
                DeferredFiles.Remove(turn);
                LoadTranscriptsFor(turn, file);
                return Transcripts.TryGetValue(turn, out list) ? list : null;
            }
            return null;
        }

        /// <summary>把一个会话文件里挂得上号的 transcript 读进内存（只挑有 Messages 的轮次）。</summary>
        private static void LoadTranscriptsFor(ChatTurn turn, string file)
        {
            try
            {
                if (!File.Exists(file)) return;
                var entries = ChatHistory.ReadEntries(file);
                // 该轮在文件里的位置没有直接记号：transcript 属于「这条 user 消息」的那一轮，按 user 文本对号
                var match = entries.FirstOrDefault(e => e?.Messages is { Count: > 0 }
                    && string.Equals(e.User, turn.UserText, StringComparison.Ordinal));
                if (match != null)
                    AttachTranscript(turn, match.Messages
                        .Where(m => !string.IsNullOrEmpty(m?.Content))
                        .Select(m => new LlmMessage(m.Role ?? "user", m.Content))
                        .ToList());
            }
            catch
            {
                // 回读失败就当没有 transcript：BuildHistory 会退回正文
            }
        }

        /// <summary>
        /// 把登记了延迟回读的轮次在调用线程批量预热：整份文件每解析一遍就挂上所有对得上号的轮次，
        /// 逐轮调 <see cref="Transcript"/> 触发的零散回读（每个登记轮次各解析一遍整份 JSON）就不再发生。
        /// 打开大会话后先在后台调一次这个，SaveLoopChat/History 走 UI 线程时就不会再踩回读。
        /// </summary>
        public static int WarmTranscripts(IEnumerable<ChatTurn> turns)
        {
            if (turns == null) return 0;
            int warmed = 0;
            foreach (var turn in turns)
            {
                if (turn == null || !DeferredFiles.TryGetValue(turn, out var file)) continue;
                LoadTranscriptsFor(turn, file);
                warmed++;
            }
            return warmed;
        }

        /// <summary>还有多少轮挂着延迟回读（诊断用）。</summary>
        internal static int DeferredCount => DeferredFiles.Count();

        public static List<ChatTurn> Load(string workspace, string kind = null)
        {
            var turns = new List<ChatTurn>();
            if (string.IsNullOrWhiteSpace(workspace)) return turns;
            Turns(ChatHistory.Read(workspace, kind), turns, ChatHistory.PathFor(workspace, kind));
            return turns;
        }

        /// <summary>
        /// 把已读出的条目投影成轮次（纯内存）：打开一个会话只解析一次文件，
        /// 模型恢复 / 历史加载 / 冷启动整理共用同一份 entries，别各读一遍几十 MB 的 JSON。
        /// </summary>
        public static List<ChatTurn> Turns(IReadOnlyList<ChatEntry> entries, string transcriptSource = null)
        {
            var turns = new List<ChatTurn>();
            Turns(entries, turns, transcriptSource);
            return turns;
        }

        private static void Turns(IReadOnlyList<ChatEntry> entries, List<ChatTurn> turns, string transcriptSource)
        {
            foreach (var entry in entries)
            {
                if (entry == null || (string.IsNullOrEmpty(entry.User) && string.IsNullOrEmpty(entry.Assistant)))
                    continue;
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", entry.User ?? ""),
                    // 空白正文（中途崩掉的轮次）读回来补一句可操作的说明，与 Worker 侧同一口径；
                    // 一律按「已结束」显示，不能把崩溃的轮次恢复成「还在跑」。
                    Assistant = new ChatMessage("assistant",
                        OpenLoop.Service.Storage.ChatHistory.AssistantFor(entry.Assistant, entry.Steps,
                            entry.Streaming)),
                    ReasoningText = entry.Reasoning ?? "",
                    ToolCallLines = entry.ToolLines ?? "",
                    ToolTraceText = entry.ToolLines ?? "",
                    TimelineText = entry.Timeline ?? "",
                    ToolStepCount = entry.Steps,
                    ToolTotalMs = entry.Ms,
                    // 协作提示：读回来仍是折叠的一行（旧文件没有这个字段，按普通消息显示）
                    Notice = entry.Notice,
                    NoticeKind = entry.NoticeKind ?? ""
                };
                if (entry.Phases is { Count: > 0 })
                    foreach (var phase in entry.Phases)
                        turn.Phases.Add((phase?.Thinking ?? "", phase?.Answer ?? ""));
                // transcript 延迟加载：打开会话不把每轮完整消息全部载进内存（大头在 Messages），
                // BuildHistory 第一次要某轮的上下文时才回读文件
                if (entry.Messages is { Count: > 0 } && transcriptSource != null)
                    DeferredFiles.AddOrUpdate(turn, transcriptSource);
                turns.Add(turn);
            }
        }

        public static void Save(string workspace, IEnumerable<ChatTurn> turns, string kind = null,
            string model = null, string effort = null)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return;
            ChatHistory.Write(workspace, kind, Entries(turns, model, effort));
        }

        /// <summary>
        /// 把轮次投影成落盘条目（纯内存，不碰磁盘）：要在别的线程写盘时先在这边拍快照，
        /// 流式中的字段不会在写盘途中被界面改掉。
        /// </summary>
        public static List<ChatEntry> Entries(IEnumerable<ChatTurn> turns, string model = null, string effort = null)
            => EntriesFromSnapshot(
                (turns ?? []).Select(t => (Turn: t, Transcript: Transcript(t))).ToList(), model, effort);

        /// <summary>
        /// 从浅引用快照投影（给后台写盘线程用）：UI 线程只取各字段的当前引用，深拷贝全部挪到后台。
        /// 界面对已存在轮次只会替换字段引用、不会原地改写旧字符串对象，投影读到的都是一致值。
        /// </summary>
        public static List<ChatEntry> EntriesFromSnapshot(
            IReadOnlyList<(ChatTurn Turn, IReadOnlyList<LlmMessage> Transcript)> snapshot,
            string model = null, string effort = null)
            => snapshot.Select(x => new ChatEntry
            {
                User = x.Turn.UserText,
                // 已结束的轮次不许落盘成空白：重新打开会变成一条「静默中断」。补法在 ChatHistory.AssistantFor
                // 里，与 Worker（A 的 Persist）同一口径，别在这里再写一份。
                Assistant = x.Turn.Streaming
                    ? x.Turn.AssistantText
                    : OpenLoop.Service.Storage.ChatHistory.AssistantFor(x.Turn.AssistantText, x.Turn.ToolStepCount),
                Reasoning = x.Turn.ReasoningText,
                ToolLines = x.Turn.ToolCallLines,
                Timeline = x.Turn.TimelineText,
                Phases = x.Turn.Phases.Select(p => new ChatPhaseEntry { Thinking = p.Thinking, Answer = p.Answer }).ToList(),
                Steps = x.Turn.ToolStepCount,
                Ms = x.Turn.ToolTotalMs,
                Notice = x.Turn.Notice,
                NoticeKind = x.Turn.NoticeKind,
                Model = model,
                Effort = effort,
                // 中途快照：这一轮还在跑，读回来要能认出来（Loop 的流式轮次靠它区分「跑到一半」与「空轮」）
                Streaming = x.Turn.Streaming,
                Messages = x.Transcript?
                    .Select(m => new ChatMessageEntry { Role = m.Role, Content = m.Content }).ToList()
            }).ToList();

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
        public static int PruneCold(string workspace, string kind, string model, IReadOnlyList<ChatTurn> turns,
            IReadOnlyList<ChatEntry> entries = null)
        {
            if (string.IsNullOrWhiteSpace(workspace) || turns == null || turns.Count == 0) return 0;

            string path = ChatHistory.PathFor(workspace, kind);
            bool idle = !File.Exists(path)
                || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromMinutes(60);
            // 已读出的 entries 就不再读文件：打开一个会话只解析一次
            bool modelChanged = (entries ?? ChatHistory.Read(workspace, kind)).Any(e =>
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
        public static void Touch(string workspace, string sessionId, string title = null, int? steps = null,
            bool? manual = null)
            => ChatHistory.Touch(workspace, sessionId, title, steps, manual);

        /// <summary>旧索引没有步数：读一次会话文件补齐（不动最后活动时间），返回 id → 步数。</summary>
        public static Dictionary<string, int> BackfillSteps() => ChatHistory.BackfillSteps();

        /// <summary>Loop 会话的 id？<c>loop-xxxxxxxx</c>，旧版是字面量 <c>loop</c>。</summary>
        internal static bool IsLoopSessionId(string id)
            => string.Equals(id, "loop", StringComparison.OrdinalIgnoreCase)
               || id?.StartsWith("loop-", StringComparison.OrdinalIgnoreCase) == true;

        /// <summary>有会话文件的已存会话；最近活动的在前。开始时间与最后活动时间都取自索引。</summary>
        public static List<SavedSession> SavedSessions()
        {
            var result = new List<SavedSession>();
            try
            {
                foreach (var item in ChatHistory.Index().OrderByDescending(e => e.UpdatedAt))
                {
                    if (item == null || string.IsNullOrEmpty(item.Id) || string.IsNullOrEmpty(item.Workspace)) continue;
                    // Loop 会话归 loops.json 管；旧版「转交」等写进这份索引的 loop id 不再当 Worker 会话恢复
                    if (IsLoopSessionId(item.Id)) continue;
                    if (!File.Exists(ChatHistory.PathFor(item.Workspace, item.Id))) continue;
                    // 旧索引没有 CreatedAt：退回 UpdatedAt（至少不是重复同一时刻）
                    var created = item.CreatedAt == default ? item.UpdatedAt : item.CreatedAt;
                    result.Add(new SavedSession(item.Id, item.Workspace, item.Title ?? "",
                        created.ToLocalTime(), item.UpdatedAt.ToLocalTime(), item.Steps, item.TitleManual));
                }
            }
            catch
            {
            }
            return result;
        }

        internal sealed record SavedSession(
            string Id, string Workspace, string Title, DateTime StartedAt, DateTime LastActiveAt, int Steps,
            bool TitleManual = false);
    }
}
