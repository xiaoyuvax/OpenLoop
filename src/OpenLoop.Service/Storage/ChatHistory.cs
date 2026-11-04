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
        public int Steps { get; set; }
        public long Ms { get; set; }
        public List<ChatMessageEntry> Messages { get; set; }
    }

    public sealed class ChatMessageEntry
    {
        public string Role { get; set; }
        public string Content { get; set; }
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

        public static void Write(string workspace, string kind, IEnumerable<ChatEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return;
            try
            {
                Directory.CreateDirectory(Dir);
                string path = PathFor(workspace, kind);
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(entries?.ToList() ?? [], Json), new UTF8Encoding(false));
                File.Move(temp, path, true);
            }
            catch
            {
            }
        }

        public static void Delete(string workspace, string kind)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return;
            try
            {
                string path = PathFor(workspace, kind);
                if (File.Exists(path)) File.Delete(path);
                if (!string.IsNullOrWhiteSpace(kind)) Forget(kind);
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

        public static void Touch(string workspace, string kind, string title = null)
        {
            if (string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(kind)) return;
            string full = Path.GetFullPath(workspace.Trim());
            Mutate(all =>
            {
                var item = all.FirstOrDefault(e => e.Id == kind);
                if (item == null)
                {
                    all.Add(new ChatIndexEntry { Id = kind, Workspace = full, Title = title ?? "" });
                }
                else
                {
                    item.Workspace = full;
                    item.UpdatedAt = DateTime.UtcNow;
                    if (title != null) item.Title = title;
                }
                return all;
            });
        }

        public static void Forget(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind)) return;
            Mutate(all => all.Where(e => e.Id != kind).ToList());
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
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
