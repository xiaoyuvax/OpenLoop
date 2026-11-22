using System.Text;
using System.Text.Json;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Tools;
using OpenLoop.Service.Logging;
using OpenLoop.Service.Storage;

namespace OpenLoop.Service.Worker
{
    /// <summary>
    /// 发起方（Initiator）要派给协作会话的一段活。Prompt 是协作会话自己看到的任务正文；
    /// Context 是主控按需设计的上下文摘要（为空则协作会话从零开始，不继承发起方的对话历史）。
    /// </summary>
    public sealed class CollabTask
    {
        public string Title { get; set; } = "";
        public string Prompt { get; set; } = "";
        public string Context { get; set; } = "";
        public string Model { get; set; }
    }

    /// <summary>
    /// 协作能力对工具的投影：由 <see cref="WorkerSessionHost"/> 实现（它才拥有会话与队列），
    /// 工具本身不碰会话表，只把模型的话翻成调用。发起方的工作区由宿主按会话 id 解析，
    /// 工具不需要自己拼路径。
    /// </summary>
    public interface ICollabHost
    {
        /// <summary>按任务清单启动若干协作会话（同工作区、平级，不是本会话的子 agent）；返回一行一行的启动结果。</summary>
        string StartCollab(string initiatorId, IReadOnlyList<CollabTask> tasks);

        /// <summary>登记监测：之后按设置里的轮询间隔把各会话状态作为仿用户输入插进发起方的队列。</summary>
        string MonitorCollab(string initiatorId, IReadOnlyList<string> sessionIds);

        /// <summary>读某个会话的轮次（含当前状态）；不给 id 时列发起方工作区的会话。</summary>
        string ReadSession(string initiatorId, string sessionId, int offset, int count);
    }

    /// <summary>发起方会话查协作会话当前状态的一行摘要：状态、步数、最近一步、结束原因。</summary>
    public static class CollabStatus
    {
        /// <summary>状态词：界面与汇报共用同一套说法。</summary>
        public static string StateText(string state) => state switch
        {
            WorkerSessionHost.Running => "运行中",
            WorkerSessionHost.Done => "已正常结束",
            WorkerSessionHost.Interrupted => "已停止/异常结束",
            _ => "空闲（尚未开跑）"
        };

        public static string Line(WorkerSessionSnapshot snapshot)
        {
            if (snapshot == null) return "（会话已不存在）";

            string state = StateText(snapshot.State);

            string text = $"- {snapshot.Id} {state} · {snapshot.Steps} 步";
            if (snapshot.Cost is { } cost) text += $" · 成本 {cost:0.0000}";
            if (!string.IsNullOrWhiteSpace(snapshot.Outcome)) text += $" · outcome={snapshot.Outcome}";

            var last = snapshot.Turns?.LastOrDefault();
            if (snapshot.State == WorkerSessionHost.Running)
            {
                string doing = !string.IsNullOrWhiteSpace(last?.Status) ? last.Status
                    : !string.IsNullOrWhiteSpace(last?.ToolLines) ? LastLine(last.ToolLines)
                    : "";
                // 进行中的工具行带 ">> " 前缀（界面上是未收尾标记），汇报里是噪声
                doing = doing.Trim().TrimStart('>').Trim();
                if (doing.Length > 0) text += " · 正在：" + Clip(doing, 160);
            }
            else if (last != null)
            {
                // 失败轮的正文就是接口报错，别只报一句「异常结束」
                string tail = last.Failed && !string.IsNullOrWhiteSpace(last.Assistant)
                    ? "错误：" + Clip(last.Assistant, 240)
                    : "结尾：" + Clip(LastNonEmpty(last.Assistant, last.ToolLines), 200);
                text += " · " + tail;
            }
            return text;
        }

        /// <summary>
        /// 状态签名：状态 + 轮数 + 步数都变了才算「有新进展」，避免每轮轮询都往队列里塞同一条。
        /// 不用 Revision：流式增量会一直改它，等于每个轮询周期都汇报一次。
        /// </summary>
        public static string Signature(IEnumerable<WorkerSessionSnapshot> snapshots)
            => string.Join("|", (snapshots ?? []).Select(s =>
                s == null ? "-" : $"{s.Id}:{s.State}:{s.Turns?.Count ?? 0}:{s.Steps}"));

        private static string LastLine(string text)
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return lines.Length == 0 ? "" : lines[^1].Trim();
        }

        private static string LastNonEmpty(string first, string second)
            => !string.IsNullOrWhiteSpace(first) ? first : second ?? "";

        internal static string Clip(string text, int max)
            => string.IsNullOrEmpty(text) || text.Length <= max ? text ?? "" : text[..max] + "…";
    }

    /// <summary>
    /// 一段协作关系：谁发起的、盯哪几个会话、上一次汇报到哪。
    /// 落在 %APPDATA%\OpenLoop\chats\collab\&lt;工作区 hash&gt;\&lt;发起方 id&gt;.json，
    /// 发起方会话重建（探活接管）后监测还能接着跑；单独子目录，不会混进会话索引。
    /// </summary>
    public sealed class CollabWatch
    {
        public string Initiator { get; set; } = "";
        public string Workspace { get; set; } = "";
        public List<string> Sessions { get; set; } = [];

        /// <summary>上一次汇报过的状态签名；没变就不插消息，免得队列被刷满。</summary>
        public string Signature { get; set; } = "";

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    }

    public static class CollabSidecar
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static void Save(CollabWatch watch)
        {
            if (watch == null || string.IsNullOrWhiteSpace(watch.Initiator)) return;
            try
            {
                string path = PathFor(watch.Workspace, watch.Initiator);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(watch, Json), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppLog.Warn("保存协作监测状态失败：" + ex.Message);
            }
        }

        public static void Delete(string workspace, string initiatorId)
        {
            if (string.IsNullOrWhiteSpace(initiatorId)) return;
            try
            {
                string path = PathFor(workspace, initiatorId);
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // 删不掉就先留着，下次被覆盖
            }
        }

        public static CollabWatch Load(string workspace, string initiatorId)
        {
            if (string.IsNullOrWhiteSpace(initiatorId)) return null;
            try
            {
                string path = PathFor(workspace, initiatorId);
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<CollabWatch>(File.ReadAllText(path, Encoding.UTF8), Json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>列出某个工作区的全部协作关系（探活接管用）。</summary>
        public static IReadOnlyList<CollabWatch> List(string workspace)
        {
            var list = new List<CollabWatch>();
            try
            {
                string dir = DirFor(workspace);
                if (!Directory.Exists(dir)) return list;
                foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
                {
                    try
                    {
                        var watch = JsonSerializer.Deserialize<CollabWatch>(File.ReadAllText(file, Encoding.UTF8), Json);
                        if (watch != null && !string.IsNullOrWhiteSpace(watch.Initiator)) list.Add(watch);
                    }
                    catch
                    {
                        // 单个文件坏掉不影响其它协作
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("读取协作监测状态失败：" + ex.Message);
            }
            return list;
        }

        /// <summary>工作区的协作目录：借 ChatHistory 的工作区 hash 分目录，与对话文件同一棵树。</summary>
        private static string DirFor(string workspace)
            => string.IsNullOrWhiteSpace(workspace)
                ? Path.Combine(ChatHistory.Dir, "collab")
                : Path.Combine(ChatHistory.Dir, "collab",
                    Path.GetFileNameWithoutExtension(ChatHistory.PathFor(workspace, "collab")));

        private static string PathFor(string workspace, string initiatorId)
            => Path.Combine(DirFor(workspace), Safe(initiatorId) + ".json");

        /// <summary>id 进文件名：只留字母数字与短横，其余压成下划线。</summary>
        private static string Safe(string id)
        {
            var sb = new StringBuilder();
            foreach (char c in id ?? "")
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.Length == 0 ? "unknown" : sb.ToString();
        }
    }

    public static class CollabTools
    {
        /// <summary>
        /// 没有发起方会话（会话 id 为空）时不提供协作工具：脱离会话的协作没有监测对象。
        /// 宿主自己已有 ReadSession（Loop 的会话读取工具）时传 includeReadSession=false，避免同名工具出现两次。
        /// </summary>
        public static IEnumerable<ITool> For(ICollabHost host, string initiatorId, bool includeReadSession = true)
        {
            if (host == null || string.IsNullOrWhiteSpace(initiatorId)) return [];
            var tools = new List<ITool> { new CollabStartTool(host, initiatorId), new CollabMonitorTool(host, initiatorId) };
            if (includeReadSession) tools.Add(new CollabReadTool(host, initiatorId));
            return tools;
        }
    }

    /// <summary>分解任务后启动多个同工作区的协作会话。</summary>
    public sealed class CollabStartTool : ITool
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private readonly ICollabHost _host;
        private readonly string _initiatorId;

        public CollabStartTool(ICollabHost host, string initiatorId)
        {
            _host = host;
            _initiatorId = initiatorId;
        }

        public string Name => "CollabStart";
        public string Description =>
            "把一个任务分解成互相独立的分工，各开一个同工作区的协作会话（平级会话，不是子 agent）。" +
            "协作会话默认带上你已完成的对话（同模型才复用前缀缓存），也可以用 context 另给一份背景。" +
            "你自己不再执行任务，只做分解、监测与协调；启动后调用 CollabMonitor 交出会话 id 开始监测。";
        public string ParamsSchema =>
            "tasks: array（必需；每项 {title?: string, prompt: string 该会话要独立完成的活, " +
            "context?: string 你替它准备的上下文（不给=它从零开始）, model?: string 可选换模型}）";
        public bool IsReadOnly => false;

        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            if (!args.Raw.TryGetValue("tasks", out var raw) || raw is not JsonElement element
                || element.ValueKind != JsonValueKind.Array)
                return Task.FromResult(AgentToolResult.Failure("tasks 必须是数组，每项含 prompt"));

            var tasks = new List<CollabTask>();
            try
            {
                foreach (var item in element.EnumerateArray())
                {
                    var task = item.ValueKind == JsonValueKind.String
                        ? new CollabTask { Prompt = item.GetString() ?? "" }
                        : JsonSerializer.Deserialize<CollabTask>(item.GetRawText(), Json);
                    if (task == null) continue;
                    if (string.IsNullOrWhiteSpace(task.Title)) task.Title = CollabStatus.Clip(task.Prompt, 40);
                    if (!string.IsNullOrWhiteSpace(task.Prompt)) tasks.Add(task);
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(AgentToolResult.Failure("tasks 解析失败：" + ex.Message));
            }

            if (tasks.Count == 0)
                return Task.FromResult(AgentToolResult.Failure("tasks 里没有有效任务（每项至少要有 prompt）"));

            // 一次起太多会话会同时烧掉多份额度：超出上限就截断，并在结果里说清楚
            string note = "";
            if (tasks.Count > MaxTasks)
            {
                note = $"（一次最多起 {MaxTasks} 个协作会话，已忽略后面 {tasks.Count - MaxTasks} 项；剩下的请下一批再起）\n";
                tasks = tasks.Take(MaxTasks).ToList();
            }

            try
            {
                return Task.FromResult(AgentToolResult.Success(note + _host.StartCollab(_initiatorId, tasks)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(AgentToolResult.Failure("启动协作会话失败：" + ex.Message));
            }
        }

        /// <summary>单次启动的协作会话上限：并发额度是有限的，模型给的清单可能很长。</summary>
        private const int MaxTasks = 8;
    }

    /// <summary>把协作会话交给后台监测：状态按轮询间隔作为仿用户消息插进发起方的队列。</summary>
    public sealed class CollabMonitorTool : ITool
    {
        private readonly ICollabHost _host;
        private readonly string _initiatorId;

        public CollabMonitorTool(ICollabHost host, string initiatorId)
        {
            _host = host;
            _initiatorId = initiatorId;
        }

        public string Name => "CollabMonitor";
        public string Description =>
            "开始监测协作会话：之后每隔一段时间，各会话的状态会作为一条仿用户消息进入你的输入队列，由你处理。" +
            "会话全部正常结束或不可恢复地异常中止后监测自动停止。省略 sessions 表示盯本会话启动的全部协作会话。";
        public string ParamsSchema =>
            "sessions: string 或 string[] 可选（要监测的会话 id；省略=本会话启动的全部协作会话）";
        public bool IsReadOnly => false;

        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            try
            {
                return Task.FromResult(AgentToolResult.Success(
                    _host.MonitorCollab(_initiatorId, args.StrList("sessions"))));
            }
            catch (Exception ex)
            {
                return Task.FromResult(AgentToolResult.Failure("开始监测失败：" + ex.Message));
            }
        }
    }

    /// <summary>协作场景下给发起方读别的会话（现在 Worker 默认拿不到）：按轮返回，并带上当前状态与错误信息。</summary>
    public sealed class CollabReadTool : ITool
    {
        private readonly ICollabHost _host;
        private readonly string _initiatorId;

        public CollabReadTool(ICollabHost host, string initiatorId)
        {
            _host = host;
            _initiatorId = initiatorId;
        }

        public string Name => "ReadSession";
        public string Description =>
            "读一个协作会话的轮次与当前状态（运行中 / 已正常结束 / 异常结束及错误信息）。" +
            "不给 session 时列出本工作区的会话。";
        public string ParamsSchema =>
            "session: string 可选（会话 id 或前缀；省略=列清单）; " +
            "offset: number 可选（从第几轮开始，1 起；省略=最近 count 轮）; count: number 可选（最多轮数，默认 10，上限 50）";
        public bool IsReadOnly => true;

        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            try
            {
                string text = _host.ReadSession(_initiatorId, args.Str("session"),
                    args.Int("offset", 0), args.Int("count", 10));
                const int max = 12000;
                if (text != null && text.Length > max) text = text[..max] + "\n…（已截断，继续读请设 offset）";
                return Task.FromResult(AgentToolResult.Success(text ?? ""));
            }
            catch (Exception ex)
            {
                return Task.FromResult(AgentToolResult.Failure("读取协作会话失败：" + ex.Message));
            }
        }
    }
}
