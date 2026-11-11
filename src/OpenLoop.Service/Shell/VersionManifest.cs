using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenLoop.Service.Bus;

namespace OpenLoop.Service.Shell
{
    /// <summary>
    /// 版本目录清单：发布时由**该版本自己的可执行文件**写出（<c>--write-manifest</c>），
    /// stage 时由被更新方读出来核对「包自己说的协议号」和整目录哈希——
    /// 请求里声明的协议号只代表发起方，旧壳推新协议包原来查不出来。
    /// 没有清单的旧目录按老规则处理，保证还能回退到升级前的版本。
    /// </summary>
    public sealed class VersionManifest
    {
        public const string FileName = "manifest.json";

        public string Role { get; set; }
        public string Version { get; set; }
        public int Protocol { get; set; }
        public string ExeSha256 { get; set; }
        public string FilesSha256 { get; set; }
        public string At { get; set; }

        /// <summary>写清单：exe 与整目录哈希都在这里算，脚本发布和 Loop 迭代共用一个实现。</summary>
        public static VersionManifest Write(string dir, string role, string version, int protocol)
        {
            string exe = VersionStore.ExePath(dir, role);
            var manifest = new VersionManifest
            {
                Role = role,
                Version = version ?? "",
                Protocol = protocol,
                ExeSha256 = File.Exists(exe) ? Sha256File(exe) : "",
                FilesSha256 = ComputeFilesSha256(dir),
                At = DateTimeOffset.Now.ToString("O")
            };
            File.WriteAllBytes(Path.Combine(dir, FileName),
                JsonSerializer.SerializeToUtf8Bytes(manifest, BusProtocol.Json));
            return manifest;
        }

        public static VersionManifest TryRead(string dir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dir)) return null;
                string path = Path.Combine(dir, FileName);
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<VersionManifest>(File.ReadAllBytes(path), BusProtocol.Json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 目录里所有文件的哈希（按相对路径排序，路径和文件哈希一起进摘要）。
        /// 清单自身、构建留痕 source-ref.txt、日志不算——它们发布后才出现或会增长。
        /// </summary>
        public static string ComputeFilesSha256(string dir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return "";
                var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Select(f => (Full: f, Rel: Path.GetRelativePath(dir, f).Replace('\\', '/')))
                    .Where(f => !Ignored(f.Rel))
                    .OrderBy(f => f.Rel, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                using var buffer = new MemoryStream();
                foreach (var (full, rel) in files)
                {
                    byte[] name = Encoding.UTF8.GetBytes(rel + "\n");
                    buffer.Write(name, 0, name.Length);
                    byte[] hash = SHA256.HashData(File.ReadAllBytes(full));
                    buffer.Write(hash, 0, hash.Length);
                }
                return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }

        private static bool Ignored(string rel)
            => rel.Equals(FileName, StringComparison.OrdinalIgnoreCase)
               || rel.Equals("source-ref.txt", StringComparison.OrdinalIgnoreCase)
               || rel.EndsWith(".log", StringComparison.OrdinalIgnoreCase);

        private static string Sha256File(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
    }
}
