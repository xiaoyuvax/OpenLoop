using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenLoop.Service.Logging;

namespace OpenLoop.Service.Storage
{
    public sealed class ChatEntry
    {
        public string User { get; set; }
        public string Assistant { get; set; }
        public string Reasoning { get; set; }
        public string ToolLines { get; set; }

        /// <summary>思维与工具调用按时间顺序穿插后的文本；旧文件没有该字段，读出来为 null。</summary>
        public string Timeline { get; set; }

        /// <summary>分段：（本段思维，本段正文）；旧文件没有该字段，读出来为 null（界面退回整轮一个折叠区）。</summary>
        public List<ChatPhaseEntry> Phases { get; set; }
        public int Steps { get; set; }
        public long Ms { get; set; }

        /// <summary>协作提示（agent 自动插入的仿用户消息）：界面折叠成一行、淡黄底；旧文件没有该字段。</summary>
        public bool Notice { get; set; }

        /// <summary>自动提示的来源（collab / mention / puppet）；旧文件没有该字段。</summary>
        public string NoticeKind { get; set; }

        /// <summary>写这份 transcript 时用的模型；旧文件没有该字段（null = 未知）。</summary>
        public string Model { get; set; }

        /// <summary>同一份 transcript 的推理力度；旧文件没有该字段（null = 未知）。</summary>
        public string Effort { get; set; }
        public List<ChatMessageEntry> Messages { get; set; }

        /// <summary>
        /// 写这份文件时这一轮还在跑（中途快照）。读回来据此认出「进程崩了、这一轮没跑完」：
        /// 正文为空时按 <see cref="ChatHistory.AssistantFor"/> 补一句可操作的说明。旧文件没有该字段。
        /// </summary>
        public bool Streaming { get; set; }
    }

    public sealed class ChatMessageEntry
    {
        public string Role { get; set; }
        public string Content { get; set; }
    }

    /// <summary>一轮里的一段：（本段思维与工具过程，本段正文）。</summary>
    public sealed class ChatPhaseEntry
    {
        public string Thinking { get; set; }
        public string Answer { get; set; }
    }

    /// <summary>
    /// 对话历史文件（%APPDATA%\OpenLoop\chats）。路径与 JSON 形状必须与既有文件保持一致，
    /// 否则升级后旧会话打不开。
    /// </summary>
    public static class ChatHistory
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string Root { get; set; }

        /// <summary>
        /// 中途快照的默认节流间隔：整轮跑十几二十分钟时按它落一次盘，崩溃最多丢这一段进度。
        /// Worker（A 写）与 Loop（壳写）共用这一套口径。
        /// </summary>
        public static TimeSpan DefaultCheckpointInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 这个工作区的快照节流间隔：读账本 <c>limit.checkpoint_interval</c>（秒），读不到用默认值。
        /// 夹在 5 秒到 10 分钟之间——太短白写盘，太长丢的进度太多。
        /// </summary>
        public static TimeSpan CheckpointIntervalOf(string workspace)
        {
            var fallback = DefaultCheckpointInterval;
            if (string.IsNullOrWhiteSpace(workspace)) return fallback;

            int seconds = -1;
            try
            {
                var ledger = OpenLoop.Core.Store.Ledger.Open(workspace);
                if (ledger.Current != null)
                    seconds = OpenLoop.Core.Store.FeatureCatalog.ValueOf(
                        ledger.Current, ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal),
                        OpenLoop.Core.Store.FeatureCatalog.CheckpointInterval, -1);
            }
            catch (Exception ex)
            {
                AppLog.Warn("读取账本数值失败（limit.checkpoint_interval）：" + ex.Message);
            }

            // 账本里没有这一项（老工作区还没演进到它）= 用默认值；有就夹在 5 秒到 10 分钟之间
            if (seconds <= 0) return fallback;
            return TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 600));
        }

        /// <summary>
        /// 一轮要落盘的正文，两端同一口径：已结束却什么都没留下时补一句可操作的说明
        /// （否则重新打开是一条「静默中断」，既看不出跑了什么，也没法判断要不要继续）。
        /// <paramref name="interrupted"/> = 落盘时这一轮还在跑（进程随后崩了）：正文前面标出来，
        /// 这句以 ServerNotice.Interrupted 开头，拼下一轮上下文时会被剔掉，不会当成模型说过的话。
        /// </summary>
        public static string AssistantFor(string assistant, int steps, bool interrupted = false)
        {
            if (!string.IsNullOrWhiteSpace(assistant))
                return interrupted
                    ? OpenLoop.Core.Messages.ServerNotice.Interrupted + "，下面是中断前已经产出的内容）\n\n" + assistant
                    : assistant;
            string head = interrupted
                ? OpenLoop.Core.Messages.ServerNotice.Interrupted
                : OpenLoop.Core.Messages.ServerNotice.BlankTurn;
            return head + (steps > 0 ? $"，已执行 {steps} 步；说「继续」会接着做。）" : "；说「继续」会重新开一轮。）");
        }

        public static string Dir => Root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenLoop", "chats");

        public static string PathFor(string workspace, string kind)
        {
            string normalized = Path.GetFullPath(workspace.Trim()).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(kind)) normalized += "|" + kind;
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16]
                .ToLowerInvariant();
            return Path.Combine(Dir, hash + ".json");
        }

        public static List<ChatEntry> Read(string workspace, string kind)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return [];
            try
            {
                string path = PathFor(workspace, kind);
                if (!File.Exists(path)) return [];
                return JsonSerializer.Deserialize<List<ChatEntry>>(File.ReadAllText(path, Encoding.UTF8), Json) ?? [];
            }
            catch
            {
                return [];
            }
        }

        /// <summary>会话最后使用的模型与推理力度（旧文件没有时为 null）：重开会话按它恢复，而不是全局上次的。</summary>
        public static (string Model, string Effort) LastModel(string workspace, string kind)
        {
            var entries = Read(workspace, kind);
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (entry == null) continue;
                if (!string.IsNullOrWhiteSpace(entry.Model) || !string.IsNullOrWhiteSpace(entry.Effort))
                    return (entry.Model, entry.Effort);
            }
            return (null, null);
        }

        /// <summary>同一进程内的写盘串行：中途快照与收尾保存可能同时在跑，别互相顶掉临时文件。</summary>
        private static readonly object WriteGate = new();

        public static void Write(string workspace, string kind, IEnumerable<ChatEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return;
            try
            {
                var list = entries?.ToList() ?? [];
                // 没有任何一轮对话 = 不该留下会话：连文件和索引一起清掉（关窗时壳会对空会话调 Save）
                if (list.Count == 0)
                {
                    Delete(workspace, kind);
                    return;
                }
                lock (WriteGate)
                {
                    CarryTranscripts(workspace, kind, list);
                    Directory.CreateDirectory(Dir);
                    string path = PathFor(workspace, kind);
                    // 临时名带进程号与随机段：A 与壳会同时写同一份会话（中途快照 + 界面保存），
                    // 共用一个 .tmp 会互相顶掉（会话索引用共用临时名时实测丢过 40 多条）。
                    string temp = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
                    try
                    {
                        File.WriteAllText(temp, JsonSerializer.Serialize(list, Json), new UTF8Encoding(false));
                        File.Move(temp, path, true);
                    }
                    finally
                    {
                        try
                        {
                            if (File.Exists(temp)) File.Delete(temp);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 两个写入方（A 的 Persist、壳的 ChatStore.Save）都是整表覆盖，而壳手里的轮次往往没有
        /// transcript：写之前把磁盘上同一轮的那份续上，否则一次界面保存就把上下文抹掉。
        /// </summary>
        private static void CarryTranscripts(string workspace, string kind, List<ChatEntry> entries)
        {
            if (entries.Count == 0) return;
            var onDisk = Read(workspace, kind);
            for (int i = 0; i < entries.Count && i < onDisk.Count; i++)
            {
                var entry = entries[i];
                var disk = onDisk[i];
                if (entry == null || disk?.Messages is not { Count: > 0 }) continue;
                if (entry.Messages is { Count: > 0 }) continue;
                if (!string.Equals(entry.User, disk.User, StringComparison.Ordinal)) continue;
                entry.Messages = disk.Messages;
            }
        }

        public static void Delete(string workspace, string kind)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return;
            try
            {
                lock (WriteGate)
                {
                    string path = PathFor(workspace, kind);
                    if (File.Exists(path)) File.Delete(path);
                    if (!string.IsNullOrWhiteSpace(kind)) Forget(kind);
                }
            }
            catch
            {
            }
        }

        // ---- 会话索引 ----
        //
        // 对话文件按「工作目录 + id」分桶，光靠目录扫不出来（哈希名），所以要一张索引。
        // 壳和 A 是两个进程：A 每轮落盘 Touch 一次，壳只在改名/侧线会话落盘时写。
        // 每次写都是「读-改-写」整张表，所以锁必须跨进程（static lock 只管本进程）、
        // 临时文件名也必须唯一（共用 index.json.tmp 时两个进程会互相顶掉，实测丢过 40 多条）。

        private const string IndexMutexName = "OpenLoop.chats.index";

        private static readonly object IndexGate = new();
        private static Mutex _indexMutex;
        private static bool _indexMutexBroken;

        public static string IndexPath => Path.Combine(Dir, "index.json");

        public static IReadOnlyList<ChatIndexEntry> Index()
        {
            try
            {
                if (!File.Exists(IndexPath)) return [];
                return JsonSerializer.Deserialize<List<ChatIndexEntry>>(File.ReadAllText(IndexPath, Encoding.UTF8), Json)
                       ?? [];
            }
            catch
            {
                return [];
            }
        }

        /// <param name="manual">标题是否由用户手动改的（true=不再自动覆盖）；null = 不动这一位。</param>
        public static void Touch(string workspace, string kind, string title = null, int? steps = null,
            bool? manual = null)
        {
            if (string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(kind)) return;
            string full = Path.GetFullPath(workspace.Trim());
            Mutate(all =>
            {
                var item = all.FirstOrDefault(e => e.Id == kind);
                if (item == null)
                {
                    all.Add(new ChatIndexEntry
                    {
                        Id = kind,
                        Workspace = full,
                        Title = title ?? "",
                        TitleManual = manual ?? false,
                        Steps = steps ?? 0,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    item.Workspace = full;
                    // 旧索引：把「上次活动」当作开始时间的兜底，必须在更新 UpdatedAt 之前取
                    if (item.CreatedAt == default) item.CreatedAt = item.UpdatedAt;
                    item.UpdatedAt = DateTime.UtcNow;
                    if (title != null) item.Title = title;
                    if (manual.HasValue) item.TitleManual = manual.Value;
                    // 改名等调用不带步数：别把索引里的步数抹成 0
                    if (steps.HasValue) item.Steps = steps.Value;
                }
                return all;
            });
        }

        public static void Forget(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind)) return;
            Mutate(all => all.Where(e => e.Id != kind).ToList());
        }

        /// <summary>
        /// 旧索引没有步数：每份对话文件读一次，把最后一轮步数写回索引。不动「最后活动」时间，
        /// 否则回填会把历史会话全推到列表顶部。返回补上的 id → 步数。
        /// </summary>
        public static Dictionary<string, int> BackfillSteps()
        {
            var filled = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in Index())
            {
                if (item == null || item.Steps > 0 || string.IsNullOrEmpty(item.Id)
                    || string.IsNullOrEmpty(item.Workspace)) continue;
                int steps = Read(item.Workspace, item.Id).LastOrDefault()?.Steps ?? 0;
                if (steps > 0) filled[item.Id] = steps;
            }

            if (filled.Count == 0) return filled;
            Mutate(all =>
            {
                foreach (var item in all)
                    if (item?.Id != null && item.Steps <= 0 && filled.TryGetValue(item.Id, out int steps))
                        item.Steps = steps;
                return all;
            });
            return filled;
        }

        /// <summary>把「读 index.json → 改 → 写新文件替换」整段放进跨进程锁里。</summary>
        private static void Mutate(Func<List<ChatIndexEntry>, List<ChatIndexEntry>> change)
        {
            lock (IndexGate)
            {
                bool locked = false;
                try
                {
                    locked = EnterIndexMutex();
                    var all = change(Index().ToList());

                    Directory.CreateDirectory(Dir);
                    string temp = $"{IndexPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
                    try
                    {
                        File.WriteAllText(temp, JsonSerializer.Serialize(all, Json), new UTF8Encoding(false));
                        File.Move(temp, IndexPath, true);
                    }
                    finally
                    {
                        try
                        {
                            if (File.Exists(temp)) File.Delete(temp);
                        }
                        catch
                        {
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn("会话索引写入失败：" + ex.Message);
                }
                finally
                {
                    if (locked) ExitIndexMutex();
                }
            }
        }

        /// <summary>命名互斥锁；拿到返回 true（必须配 <see cref="ExitIndexMutex"/>）。</summary>
        private static bool EnterIndexMutex()
        {
            if (_indexMutexBroken) return false;
            try
            {
                _indexMutex ??= new Mutex(false, IndexMutexName);
                // 等不到（30s）也照写：丢一条比整次落盘失败更糟
                return _indexMutex.WaitOne(TimeSpan.FromSeconds(30));
            }
            catch (AbandonedMutexException)
            {
                return true; // 上个进程没释放就退了，锁现在归我们
            }
            catch (Exception ex)
            {
                // 环境不支持命名锁时退回进程内锁：总比不写强
                _indexMutexBroken = true;
                AppLog.Warn("会话索引的跨进程锁不可用，改用进程内锁：" + ex.Message);
                return false;
            }
        }

        private static void ExitIndexMutex()
        {
            try
            {
                _indexMutex?.ReleaseMutex();
            }
            catch
            {
            }
        }
    }

    /// <summary>chats/index.json 的一行：会话 id → 工作目录（哈希文件名反查用）。</summary>
    public sealed class ChatIndexEntry
    {
        public string Id { get; set; }
        public string Workspace { get; set; }
        public string Title { get; set; }

        /// <summary>标题是用户手动改的：自动总结（首轮/结束时）不再覆盖；旧索引没有这一项 = false。</summary>
        public bool TitleManual { get; set; }

        /// <summary>最后一轮的步数（与界面一致）：会话列表在冷启动时不读 30MB 的对话文件也能显示步数。</summary>
        public int Steps { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>首次登记时间（UTC）。旧索引没有这一项，读出来是 default，调用方退回 UpdatedAt。</summary>
        public DateTime CreatedAt { get; set; }
    }
}
