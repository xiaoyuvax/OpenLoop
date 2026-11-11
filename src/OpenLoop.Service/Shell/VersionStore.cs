using System.Text;
using System.Text.Json;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Shell
{
    /// <summary>一个角色当前跑的那份产物。</summary>
    public sealed class VersionEntry
    {
        public string Version { get; set; }
        public string Dir { get; set; }
        public string Sha256 { get; set; }
        public string At { get; set; }
    }

    /// <summary>被更新方退出前写的交接单：壳读到它就知道该拉起哪个目录。</summary>
    public sealed class UpdateRequest
    {
        public string Role { get; set; }
        public string Version { get; set; }
        public string Dir { get; set; }
        public string Sha256 { get; set; }
        public string RequestedBy { get; set; }
        public string At { get; set; }

        public static string PathFor(string role) =>
            System.IO.Path.Combine(AppPaths.RuntimeDir, "update-" + role + ".json");

        public void Save()
        {
            AppPaths.EnsureDirectory(System.IO.Path.GetDirectoryName(PathFor(Role)));
            string path = PathFor(Role);
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(this, BusProtocol.Json));
            File.Move(temp, path, true);
        }

        public static UpdateRequest TryLoad(string role)
        {
            try
            {
                string path = PathFor(role);
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<UpdateRequest>(File.ReadAllBytes(path), BusProtocol.Json);
            }
            catch
            {
                return null;
            }
        }

        public static void Clear(string role)
        {
            try { File.Delete(PathFor(role)); } catch { }
        }
    }

    /// <summary>
    /// 版本目录：<c>versions/&lt;role&gt;/&lt;version+sha8&gt;/</c> 各带一份完整产物，
    /// <c>current.json</c> 记每个角色当前跑哪份。只有壳写 current.json。
    /// </summary>
    public sealed class VersionStore
    {
        public VersionStore(string root = null) => Root = root ?? AppPaths.VersionsDir;

        public string Root { get; }
        public string CurrentPath => Path.Combine(Root, "current.json");

        /// <summary>上一个好版本（换版前的 current），崩循环时用来回退；与 current 分开存，避免两个角色互相覆盖。</summary>
        public string PreviousPath => Path.Combine(Root, "previous.json");

        public static string ExeName(string role) =>
            OperatingSystem.IsWindows() ? $"OpenLoop.{role.ToUpperInvariant()}.exe" : $"OpenLoop.{role.ToUpperInvariant()}";

        public static string ExePath(string dir, string role) => Path.Combine(dir, ExeName(role));

        public Dictionary<string, VersionEntry> Read() => ReadOrRecover(out _);

        /// <summary>
        /// 读 current.json；损坏时先用 current.json.bak 兜底，两份都坏才返回空表，
        /// 并把坏文件留成 current.json.bad-*（否则下一次 Activate 会把另一个角色静默抹掉）。
        /// </summary>
        public Dictionary<string, VersionEntry> ReadOrRecover(out bool recovered)
        {
            recovered = false;
            if (!File.Exists(CurrentPath)) return Empty();
            if (TryReadFile(CurrentPath, out var current)) return current;
            if (TryReadFile(CurrentPath + ".bak", out var backup))
            {
                recovered = true;
                return backup;
            }
            try
            {
                File.Copy(CurrentPath, CurrentPath + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
            }
            catch
            {
            }
            return Empty();
        }

        private static bool TryReadFile(string path, out Dictionary<string, VersionEntry> all)
        {
            all = Empty();
            try
            {
                if (!File.Exists(path)) return false;
                var parsed = JsonSerializer.Deserialize<Dictionary<string, VersionEntry>>(
                    File.ReadAllBytes(path), BusProtocol.Json);
                if (parsed == null || parsed.Count == 0) return false;
                all = new Dictionary<string, VersionEntry>(parsed, StringComparer.OrdinalIgnoreCase);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Dictionary<string, VersionEntry> Empty()
            => new(StringComparer.OrdinalIgnoreCase);

        public VersionEntry Current(string role)
            => Read().TryGetValue(role, out var entry) ? entry : null;

        /// <summary>当前版本的可执行文件；没登记过就是 null（壳回退到 bin/worker）。</summary>
        public string Exe(string role)
        {
            var entry = Current(role);
            if (entry?.Dir == null) return null;
            string exe = ExePath(entry.Dir, role);
            return File.Exists(exe) ? exe : null;
        }

        /// <summary>
        /// 记一个新版本。默认把换掉的旧版本记进 previous.json（崩循环回退用）；
        /// 自动回退时传 <paramref name="rememberPrevious"/> = false，免得把刚判坏的版本又记成「上一版」。
        /// </summary>
        public void Activate(string role, VersionEntry entry, bool rememberPrevious = true)
        {
            var all = ReadOrRecover(out _);
            if (rememberPrevious && all.TryGetValue(role, out var old) && !SameDir(old?.Dir, entry?.Dir))
                WritePrevious(role, old);

            all[role] = entry;
            WriteFile(CurrentPath, all);
            try { File.Copy(CurrentPath, CurrentPath + ".bak", true); } catch { }
        }

        /// <summary>上一个好版本；没换过或回退过就是 null。</summary>
        public VersionEntry Previous(string role)
        {
            if (!TryReadFile(PreviousPath, out var all)) return null;
            return all.TryGetValue(role, out var entry) ? entry : null;
        }

        private void WritePrevious(string role, VersionEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Dir)) return;
            var all = TryReadFile(PreviousPath, out var existing) ? existing : Empty();
            all[role] = entry;
            WriteFile(PreviousPath, all);
        }

        private static bool SameDir(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void WriteFile(string path, Dictionary<string, VersionEntry> all)
        {
            AppPaths.EnsureDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(all, BusProtocol.Json));
            File.Move(temp, path, true);
        }

        /// <summary>每个角色只留最近 <paramref name="keep"/> 份（含正在跑的与可回退的），其余删目录。</summary>
        public void Prune(string role, int keep = 2)
        {
            try
            {
                string dir = Path.Combine(Root, role);
                if (!Directory.Exists(dir)) return;

                string current = Normalize(Current(role)?.Dir);
                string previous = Normalize(Previous(role)?.Dir);
                var rest = Directory.GetDirectories(dir)
                    .Where(d => !SameDir(d, current) && !SameDir(d, previous))
                    .OrderByDescending(Directory.GetLastWriteTimeUtc)
                    .Skip(Math.Max(0, keep - 1))
                    .ToList();

                foreach (string old in rest)
                {
                    try { Directory.Delete(old, true); } catch { }
                }
            }
            catch
            {
            }
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar); }
            catch { return null; }
        }

        /// <summary>每次更新结果一行，append-only。</summary>
        public void Log(string role, string result, string version, string dir, string detail = null)
        {
            try
            {
                AppPaths.EnsureDirectory(AppPaths.RuntimeDir);
                string line = JsonSerializer.Serialize(new
                {
                    at = DateTimeOffset.Now.ToString("O"),
                    role,
                    result,
                    version,
                    dir,
                    detail
                }, BusProtocol.Json);
                File.AppendAllText(Path.Combine(AppPaths.RuntimeDir, "update-log.jsonl"),
                    line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }
}
