using System.Net.Http;
using System.Text;
using System.Text.Json;
using OpenLoop.Service.Runtime;
using Puppet.Core;

namespace OpenLoop.Service.Web
{
    /// <summary>
    /// 会话在 Puppet 留言板上的登记、心跳、步骤与遗言。
    /// 留言板默认落在 A 的 Puppet 实例：A 里的 Worker 会话直接写，壳里的 Loop 会话经 A 的 /agent/* 远程写。
    /// A 掉线时写自动转投 I 的 Puppet（再不行用本进程留言板兜底），因此会话历史可能出现 @A 与 @I 两份。
    /// </summary>
    public static class SessionBoard
    {
        private static readonly HttpClient Http = new(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        private static readonly HashSet<string> Greeted = new(StringComparer.Ordinal);
        private static readonly object Gate = new();

        /// <summary>每个会话最近一次写成功的宿主：遗言要回到有它历史的那个板上。</summary>
        private static readonly Dictionary<string, string> HostOf = new(StringComparer.Ordinal);

        /// <summary>一个可用的留言板宿主：@A / @I 走 HTTP，@Local 是本进程内存。</summary>
        public sealed class BoardHost
        {
            public string Name { get; init; }
            public string Endpoint { get; init; }
            public bool Local => string.IsNullOrEmpty(Endpoint);
        }

        /// <summary>当前活着的留言板宿主（A、I，最后是本进程兜底）。</summary>
        public static IReadOnlyList<BoardHost> Hosts()
        {
            var list = new List<BoardHost>();
            foreach (string role in new[] { "a", "i" })
            {
                string endpoint = EndpointOf(role);
                if (endpoint != null)
                    list.Add(new BoardHost { Name = role == "a" ? "@A" : "@I", Endpoint = endpoint });
            }

            // 壳里的 Loop 会话：A/I 都不在时用自己进程内的留言板
            if (PuppetBridge.LocalHub != null
                && !string.Equals(PuppetBridge.SelfRole, "a", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(PuppetBridge.SelfRole, "i", StringComparison.OrdinalIgnoreCase))
                list.Add(new BoardHost { Name = "@Local" });
            return list;
        }

        /// <summary>读某个宿主的 /agent/*；本进程宿主不用 HTTP（返回 null，调用方直接读 AgentBook）。</summary>
        public static string GetFrom(BoardHost host, string path)
            => host == null || host.Local ? null : GetFrom(host.Endpoint, path);

        /// <summary>只往指定宿主发：在 Puppet 视图里回到一个 @I 的会话，不能又转投 @A。</summary>
        public static bool PostTo(BoardHost host, string path, object body)
            => host == null || host.Local ? false : PostTo(host.Endpoint, path, body);

        /// <summary>会话开跑：首次报到时附一句自我介绍（身份 + 当前任务），之后每次只更新在做什么。</summary>
        public static void Opening(string agentId, string role, string workspace, string model, string task, bool remote = false)
        {
            bool first;
            lock (Gate) first = Greeted.Add(agentId);

            string purpose = $"{role} 会话 · {workspace}";
            string doing = Clip(task, 160);
            string say = first
                ? $"我是 {agentId}（{role}，工作区 {workspace}，模型 {model}）：当前任务：{Clip(task, 200)}"
                : null;
            _ = Task.Run(() => Send(agentId, purpose, doing, say, remote));
        }

        /// <summary>会话关闭：清掉本会话之前的留言，只留最后一句遗言。</summary>
        public static void Leaving(string agentId, string words, bool remote = false)
        {
            lock (Gate)
                if (!Greeted.Remove(agentId)) return;

            string farewell = Clip(string.IsNullOrWhiteSpace(words) ? "（会话结束）" : words.Trim(), 600);
            _ = Task.Run(() => Leave(agentId, farewell, remote));
        }

        /// <summary>工具步骤上报：同工作区的其他会话靠它看各自改到哪、结果如何。</summary>
        public static void Working(string agentId, string line, bool remote = false)
        {
            if (string.IsNullOrWhiteSpace(agentId) || string.IsNullOrWhiteSpace(line)) return;
            string step = Clip(line.Trim(), 200);
            _ = Task.Run(() =>
            {
                if (!remote || !PostAny("/agent/book/heartbeat", new { id = agentId, step }, out string host))
                    AgentBook.Heartbeat(agentId, null, null, 0, "openloop", step: step);
                else
                    lock (Gate) HostOf[agentId] = host;
            });
        }

        /// <summary>同工作区出现新会话时塞进输入队列的一条提示：与用户输入同一条队列，用户可以撤回。</summary>
        public static string PeerNotice(IReadOnlyList<string> peers)
        {
            string list = string.Join("；", peers ?? Array.Empty<string>());
            string target = OpenLoop.Core.Agent.AgentIdentity.BoardTarget;
            return $"【协作提示】同工作区新会话已启动：{list}。技能 peer-board 已可用："
                + $"Puppet(target=\"{target}\", action=\"book\") 看它的工具步骤，"
                + $"Puppet(target=\"{target}\", action=\"say\", to=\"<对方 id>\", text=\"...\") 定向留言；改动同一文件前先问一句。";
        }

        /// <summary>同工作区的其他会话（5 分钟内有心跳）。工作区按登记时的 Purpose 后缀精确比对。</summary>
        public static IReadOnlyList<PeerInfo> Peers(string workspace, string selfId, bool remote = false)
        {
            string ws = (workspace ?? "").TrimEnd('\\', '/');
            if (ws.Length == 0) return [];

            var all = new List<PeerInfo>();
            if (!remote)
            {
                all.AddRange(AgentBook.GetAll().Select(ToPeer));
            }
            else
            {
                foreach (var host in Hosts())
                {
                    if (host.Local)
                    {
                        all.AddRange(AgentBook.GetAll().Select(ToPeer));
                        continue;
                    }
                    string json = GetFrom(host, "/agent/book");
                    if (string.IsNullOrWhiteSpace(json)) continue;
                    try
                    {
                        var book = JsonSerializer.Deserialize<BookResponse>(json, BookJson);
                        all.AddRange((book?.Agents ?? new List<BookAgent>()).Select(a => new PeerInfo
                        {
                            Id = a.Id, Purpose = a.Purpose, Doing = a.Operation, Steps = a.Steps?.ToArray() ?? []
                        }));
                    }
                    catch
                    {
                        // 单个宿主解析失败不影响另一个
                    }
                }
            }

            return all
                .GroupBy(p => p.Id, StringComparer.Ordinal)
                .Select(g => g.First())
                .Where(p => !string.IsNullOrEmpty(p.Id) && !string.Equals(p.Id, selfId, StringComparison.Ordinal)
                    && (p.Purpose ?? "").TrimEnd('\\', '/').EndsWith(ws, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static PeerInfo ToPeer(Puppet.Core.AgentInfo a)
        {
            return new PeerInfo
            {
                Id = a.Id, Purpose = a.Purpose, Doing = a.Operation, Steps = AgentBook.SnapshotSteps(a)
            };
        }

        private static void Send(string agentId, string purpose, string op, string say, bool remote)
        {
            if (!remote || !PostAny("/agent/book/heartbeat", new { id = agentId, purpose, op, say }, out string host))
                AgentBook.Heartbeat(agentId, purpose, op, 0, "openloop", note: null, message: say);
            else
                lock (Gate) HostOf[agentId] = host;
        }

        private static void Leave(string agentId, string farewell, bool remote)
        {
            if (!remote)
            {
                AgentBook.Leave(agentId, farewell);
                return;
            }

            string name;
            lock (Gate) HostOf.Remove(agentId, out name);
            var host = string.IsNullOrEmpty(name)
                ? null
                : Hosts().FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.Ordinal));
            if (host != null && !host.Local && PostTo(host, "/agent/book/release", new { id = agentId, farewell })) return;
            if (!PostAny("/agent/book/release", new { id = agentId, farewell }, out _))
                AgentBook.Leave(agentId, farewell);
        }

        /// <summary>按 A → I 顺序投递，任意一个成功就停（A 掉线时自动落到 I）。</summary>
        private static bool PostAny(string path, object body, out string hostName)
        {
            hostName = null;
            foreach (var host in Hosts())
            {
                if (host.Local) continue;
                if (!PostTo(host.Endpoint, path, body)) continue;
                hostName = host.Name;
                return true;
            }
            return false;
        }

        private static string GetFrom(string endpoint, string path)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint + path);
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + PuppetBridge.Key);
                using var response = Http.Send(request);
                if (!response.IsSuccessStatusCode) return null;
                using var reader = new StreamReader(response.Content.ReadAsStream(), Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch
            {
                return null;
            }
        }

        private static bool PostTo(string endpoint, string path, object body)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint + path);
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + PuppetBridge.Key);
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                using var response = Http.Send(request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                // 对端未启动/正在换版：换下一个宿主或落到本进程
                return false;
            }
        }

        private static string EndpointOf(string role)
        {
            var runtime = RuntimeInfo.TryLoad(AppPaths.RuntimeFile(role));
            if (runtime is not { IsAlive: true } || string.IsNullOrWhiteSpace(runtime.Bus)) return null;
            return runtime.Bus.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? runtime.Bus : "http://" + runtime.Bus;
        }

        private static readonly JsonSerializerOptions BookJson = new() { PropertyNameCaseInsensitive = true };

        private sealed class BookResponse
        {
            public List<BookAgent> Agents { get; set; }
        }

        private sealed class BookAgent
        {
            public string Id { get; set; }
            public string Purpose { get; set; }
            public string Operation { get; set; }
            public List<string> Steps { get; set; }
        }

        private static string Clip(string text, int max)
            => string.IsNullOrEmpty(text) || text.Length <= max ? text ?? "" : text[..max] + "…";
    }

    /// <summary>同工作区的另一个会话，给技能正文/提示词用。</summary>
    public sealed class PeerInfo
    {
        public string Id { get; set; }
        public string Purpose { get; set; }
        public string Doing { get; set; }
        public string[] Steps { get; set; } = [];

        /// <summary>一行摘要：A-xxxx（在做：…；最近：Edit[src/a.cs] → ok；Shell[build] → 失败）</summary>
        public string Summary()
        {
            string doing = Doing ?? "";
            if (doing.Length > 80) doing = doing[..80] + "…";
            var steps = Steps ?? Array.Empty<string>();
            var sb = new StringBuilder(Id ?? "");
            if (doing.Length > 0) sb.Append("（在做：").Append(doing);
            if (steps.Length > 0)
                sb.Append(doing.Length > 0 ? "；最近：" : "（最近：").Append(string.Join("；", steps.TakeLast(3)));
            return sb.Append('）').ToString();
        }
    }
}
