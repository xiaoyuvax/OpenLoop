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

        public static string ExeName(string role) =>
            OperatingSystem.IsWindows() ? $"OpenLoop.{role.ToUpperInvariant()}.exe" : $"OpenLoop.{role.ToUpperInvariant()}";

        public static string ExePath(string dir, string role) => Path.Combine(dir, ExeName(role));

        public Dictionary<string, VersionEntry> Read()
        {
            try
            {
                if (!File.Exists(CurrentPath)) return new Dictionary<string, VersionEntry>(StringComparer.OrdinalIgnoreCase);
                return JsonSerializer.Deserialize<Dictionary<string, VersionEntry>>(
                           File.ReadAllBytes(CurrentPath), BusProtocol.Json)
                       ?? new Dictionary<string, VersionEntry>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, VersionEntry>(StringComparer.OrdinalIgnoreCase);
            }
        }

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

        public void Activate(string role, VersionEntry entry)
        {
            var all = Read();
            all[role] = entry;
            AppPaths.EnsureDirectory(Root);
            string temp = CurrentPath + ".tmp";
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(all, BusProtocol.Json));
            File.Move(temp, CurrentPath, true);
        }

        /// <summary>每个角色只留最近 <paramref name="keep"/> 份（含正在跑的），其余删目录。</summary>
        public void Prune(string role, int keep = 2)
        {
            try
            {
                string dir = Path.Combine(Root, role);
                if (!Directory.Exists(dir)) return;

                var current = Current(role);
                var rest = Directory.GetDirectories(dir)
                    .Where(d => !string.Equals(Path.GetFullPath(d), Path.GetFullPath(current?.Dir ?? ""),
                        StringComparison.OrdinalIgnoreCase))
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
