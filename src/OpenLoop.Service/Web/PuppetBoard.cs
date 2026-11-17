using System.Text.Json;
using Puppet.Core;

namespace OpenLoop.Service.Web
{
    /// <summary>Puppet 留言板上的一个会话（一个 agent id 及其最近活动）。</summary>
    public sealed class PuppetSession
    {
        public string Host { get; set; }
        public string Id { get; set; }
        public string Purpose { get; set; }
        public string Doing { get; set; }
        public string[] Steps { get; set; } = [];
        public DateTime LastAt { get; set; }
        public string LastText { get; set; }
        public bool Live { get; set; }

        /// <summary>最近一条留言的时间（心跳可能比它新，但列表要展示的是留言摘要）。</summary>
        public DateTime LastMessageAt { get; set; }
    }

    /// <summary>留言板上的一条消息。</summary>
    public sealed class PuppetMessage
    {
        public string Host { get; set; }
        public long Seq { get; set; }
        public string AgentId { get; set; }
        public string Text { get; set; }
        public string To { get; set; }
        public DateTime At { get; set; }
    }

    /// <summary>
    /// 会话页 [Puppet] 的只读视图：合并 @A、@I（以及本进程兜底）三张留言板。
    /// 这些会话没有主 agent 驱动，谁在板上说话就显示谁；用户以 U 的身份参与。
    /// </summary>
    public static class PuppetBoard
    {
        public const string UserId = "U";

        private static readonly TimeSpan LiveWithin = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(2);

        private static readonly object Gate = new();
        private static List<PuppetSession> _sessions = [];
        private static DateTime _sessionsAt;

        /// <summary>合并后的会话列表（2 秒缓存：会话总览刷新很频繁，不能每次都打 HTTP）。</summary>
        public static IReadOnlyList<PuppetSession> Sessions()
        {
            lock (Gate)
            {
                if (DateTime.UtcNow - _sessionsAt < CacheFor) return _sessions.ToList();
            }

            var now = DateTime.Now;
            var sessions = new Dictionary<string, PuppetSession>(StringComparer.Ordinal);
            foreach (var host in SessionBoard.Hosts())
            {
                var state = Read(host);
                foreach (var a in state.Agents)
                {
                    string key = host.Name + "|" + a.Id;
                    sessions[key] = new PuppetSession
                    {
                        Host = host.Name,
                        Id = a.Id,
                        Purpose = a.Purpose,
                        Doing = a.Operation,
                        Steps = a.Steps,
                        LastAt = a.LastActive,
                        Live = now - a.LastActive <= LiveWithin
                    };
                }
                foreach (var m in state.Messages)
                {
                    string key = host.Name + "|" + m.AgentId;
                    if (!sessions.TryGetValue(key, out var session))
                        sessions[key] = session = new PuppetSession { Host = host.Name, Id = m.AgentId };
                    if (m.At > session.LastAt) session.LastAt = m.At;
                    if (m.At >= session.LastMessageAt)
                    {
                        session.LastMessageAt = m.At;
                        session.LastText = m.Text;
                    }
                }
            }

            var result = sessions.Values
                .Where(s => !string.IsNullOrEmpty(s.Id)
                    && !string.Equals(s.Id, UserId, StringComparison.Ordinal)
                    && now - s.LastAt <= AgentBook.Retention)
                .OrderByDescending(s => s.LastAt)
                .ToList();

            lock (Gate)
            {
                _sessions = result;
                _sessionsAt = DateTime.UtcNow;
            }
            return result.ToList();
        }

        /// <summary>某个会话在它所在板上的对话：它说的、发给它的、用户消息与广播。</summary>
        public static IReadOnlyList<PuppetMessage> Chat(string hostName, string agentId)
        {
            var host = SessionBoard.Hosts().FirstOrDefault(h => string.Equals(h.Name, hostName, StringComparison.Ordinal));
            if (host == null) return [];
            var messages = Read(host).Messages;
            return messages
                .Where(m => string.IsNullOrEmpty(agentId)
                    || string.Equals(m.AgentId, agentId, StringComparison.Ordinal)
                    || string.Equals(m.AgentId, UserId, StringComparison.Ordinal)
                    || string.Equals(m.To ?? "", agentId, StringComparison.Ordinal)
                    || string.IsNullOrEmpty(m.To))
                .OrderBy(m => m.At)
                .ThenBy(m => m.Seq)
                .ToList();
        }

        /// <summary>以用户身份在指定板上发言（to 为空 = 广播）。</summary>
        public static bool Say(string hostName, string text, string to = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var host = SessionBoard.Hosts().FirstOrDefault(h => string.Equals(h.Name, hostName, StringComparison.Ordinal))
                       ?? SessionBoard.Hosts().FirstOrDefault();
            if (host == null) return false;
            if (host.Local)
            {
                AgentBook.Heartbeat(UserId, "用户", "", 0, "openloop", message: text, messageTo: to);
                return true;
            }
            return SessionBoard.PostTo(host, "/agent/book/heartbeat",
                new { id = UserId, purpose = "用户", say = text, sayTo = to });
        }

        /// <summary>界面上的一行：当前有哪些板在线。</summary>
        public static string HostsText()
            => string.Join(" ", SessionBoard.Hosts().Select(h => h.Name));

        private sealed class BookAgent
        {
            public string Id { get; set; }
            public string Purpose { get; set; }
            public string Operation { get; set; }
            public string[] Steps { get; set; } = [];
            public DateTime LastActive { get; set; }
        }

        private sealed class BoardState
        {
            public List<BookAgent> Agents { get; } = [];
            public List<PuppetMessage> Messages { get; } = [];
        }

        private static BoardState Read(SessionBoard.BoardHost host)
        {
            var state = new BoardState();
            if (host.Local)
            {
                foreach (var a in AgentBook.GetAll(includeStale: true))
                    state.Agents.Add(new BookAgent
                    {
                        Id = a.Id, Purpose = a.Purpose, Operation = a.Operation,
                        Steps = AgentBook.SnapshotSteps(a), LastActive = a.LastHeartbeatAt
                    });
                foreach (var m in AgentBook.GetChat())
                    state.Messages.Add(new PuppetMessage
                    {
                        Host = host.Name, Seq = m.Seq, AgentId = m.AgentId, Text = m.Text, To = m.To, At = m.At
                    });
                return state;
            }

            using var agents = Parse(SessionBoard.GetFrom(host, "/agent/book?includeStale=true"));
            if (agents != null && TryGet(agents.RootElement, "agents", out var list)
                && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    var steps = new List<string>();
                    if (TryGet(item, "steps", out var s) && s.ValueKind == JsonValueKind.Array)
                        foreach (var step in s.EnumerateArray()) steps.Add(step.ToString());
                    state.Agents.Add(new BookAgent
                    {
                        Id = Text(item, "id"),
                        Purpose = Text(item, "purpose"),
                        Operation = Text(item, "operation"),
                        Steps = steps.ToArray(),
                        LastActive = Time(item, "lastActive")
                    });
                }
            }

            using var chat = Parse(SessionBoard.GetFrom(host, "/agent/chat?tail=300"));
            if (chat != null && TryGet(chat.RootElement, "messages", out var messages)
                && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in messages.EnumerateArray())
                    state.Messages.Add(new PuppetMessage
                    {
                        Host = host.Name,
                        Seq = TryGet(item, "seq", out var seq) && seq.TryGetInt64(out long n) ? n : 0,
                        AgentId = Text(item, "agentId"),
                        Text = Text(item, "text"),
                        To = Text(item, "to"),
                        At = Time(item, "at")
                    });
            }
            return state;
        }

        private static JsonDocument Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonDocument.Parse(json); }
            catch (JsonException) { return null; }
        }

        /// <summary>Puppet 的 JSON 有 camelCase 也有 PascalCase 字段，按名忽略大小写取。</summary>
        private static bool TryGet(JsonElement item, string name, out JsonElement value)
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out value)) return true;
            if (item.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in item.EnumerateObject())
                {
                    if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                    value = property.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }

        private static string Text(JsonElement item, string name)
            => TryGet(item, name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";

        private static DateTime Time(JsonElement item, string name)
            => DateTime.TryParse(Text(item, name), out var value) ? value : DateTime.MinValue;
    }
}
