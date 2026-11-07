using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Runtime;
using OpenLoop.Service.Shell;

namespace OpenLoop.Service.Source
{
    /// <summary>
    /// 迭代轮次：壳（不是 Loop）执行「编译 → 发布到 versions/ → 登记 stage → 可选重启」。
    /// Loop 只能提交源码改动，换版动作留在壳里，忙时由 worker 的 409 挡住。
    /// </summary>
    public static class Iteration
    {
        public sealed record Step(string Name, bool Ok, string Detail)
        {
            public override string ToString() => $"{(Ok ? "OK" : "FAIL")} {Name} {Detail}";
        }

        private const string Solution = "OpenLoop.slnx";

        public static async Task<Step> BuildAsync(CancellationToken ct = default)
        {
            string root = SourceSync.SourceRoot;
            if (!File.Exists(Path.Combine(root, Solution)))
                return new("build", false, "找不到 " + Path.Combine(root, Solution));

            var (ok, output) = await ExecAsync("dotnet",
                ["build", Solution, "-c", "Release", "--nologo", "-v", "q"], root, ct);
            return new("build", ok, Tail(output, ok ? 3 : 12));
        }

        /// <summary>发布一个角色到 <c>versions/&lt;role&gt;/&lt;version&gt;-&lt;sha8&gt;/</c>，返回目录。</summary>
        public static async Task<Step> PublishAsync(string role, string version, CancellationToken ct = default)
        {
            string root = SourceSync.SourceRoot;
            string temp = Path.Combine(Path.GetTempPath(), $"openloop-{role}-{Guid.NewGuid():N}");
            try
            {
                var (ok, output) = await ExecAsync("dotnet",
                    ["publish", Path.Combine("src", "OpenLoop." + role.ToUpperInvariant()),
                        "-c", "Release", "-o", temp, "--nologo", "-v", "q"], root, ct);
                if (!ok) return new("publish " + role, false, Tail(output, 12));

                string exe = VersionStore.ExePath(temp, role);
                if (!File.Exists(exe)) return new("publish " + role, false, "没有产物 " + exe);

                string dir = Path.Combine(AppPaths.VersionsDir, role, VersionDirName(version, Sha256(exe)));
                Directory.CreateDirectory(dir);
                foreach (string file in Directory.EnumerateFiles(temp, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(dir, Path.GetRelativePath(temp, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, true);
                }
                return new("publish " + role, true, dir);
            }
            catch (Exception ex)
            {
                return new("publish " + role, false, ex.Message);
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
            }
        }

        /// <summary>把已发布的目录登记到运行中的 worker；只登记，不重启。</summary>
        public static Task<Step> StageAsync(string role, string dir, string version, CancellationToken ct = default)
        {
            string exe = VersionStore.ExePath(dir, role);
            string body = JsonSerializer.Serialize(new
            {
                version = string.IsNullOrWhiteSpace(version) ? Path.GetFileName(dir.TrimEnd('\\', '/')) : version,
                dir,
                sha256 = File.Exists(exe) ? Sha256(exe) : "",
                protocol = BusProtocol.Version
            }, BusProtocol.Json);
            return PostAsync("stage " + role, role, "/bus/stage", body, ct);
        }

        /// <summary>请 worker 重启到已登记版本；忙时 409，等这一轮跑完再来。</summary>
        public static Task<Step> RestartAsync(string role, CancellationToken ct = default)
            => PostAsync("restart " + role, role, "/bus/restart",
                JsonSerializer.Serialize(new { reason = "iteration" }, BusProtocol.Json), ct);

        /// <summary>目录名：版本号 + 可执行文件 sha8；版本号不可用时退回 adhoc。</summary>
        internal static string VersionDirName(string version, string sha)
        {
            string clean = new((version ?? "").Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_').ToArray());
            if (clean.Length == 0) clean = "adhoc";
            string tail = (sha ?? "").Length >= 8 ? sha[..8] : (sha ?? "").PadRight(8, '0');
            return clean + "-" + tail;
        }

        /// <summary>只留最后几行非空输出，避免把整段构建日志贴进聊天。</summary>
        internal static string Tail(string text, int lines)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var tail = text.Split('\n')
                .Select(l => l.TrimEnd('\r').Trim())
                .Where(l => l.Length > 0)
                .TakeLast(Math.Max(1, lines))
                .ToList();
            string joined = string.Join(" | ", tail);
            return joined.Length > 600 ? "…" + joined[^600..] : joined;
        }

        private static async Task<Step> PostAsync(
            string name, string role, string path, string body, CancellationToken ct)
        {
            var runtime = RuntimeInfo.TryLoad(AppPaths.RuntimeFile(role));
            if (runtime == null || !runtime.IsAlive) return new(name, false, "worker 未运行");

            string key = BusKey();
            if (key.Length == 0) return new(name, false, "缺少 bus.key");

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                http.DefaultRequestHeaders.Add(BusProtocol.KeyHeader, key);
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var response = await http.PostAsync($"http://{runtime.Bus}{path}", content, ct);
                string text = await response.Content.ReadAsStringAsync(ct);
                return new(name, response.IsSuccessStatusCode,
                    response.IsSuccessStatusCode ? ((int)response.StatusCode).ToString() : $"{(int)response.StatusCode} {Tail(text, 2)}");
            }
            catch (Exception ex)
            {
                return new(name, false, ex.Message);
            }
        }

        private static string BusKey()
        {
            try
            {
                return File.Exists(AppPaths.BusKeyPath)
                    ? File.ReadAllText(AppPaths.BusKeyPath, Encoding.UTF8).Trim()
                    : "";
            }
            catch
            {
                return "";
            }
        }

        private static string Sha256(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        internal static async Task<(bool Ok, string Output)> ExecAsync(
            string file, string[] args, string workdir, CancellationToken ct)
        {
            var start = new ProcessStartInfo(file)
            {
                WorkingDirectory = workdir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string arg in args) start.ArgumentList.Add(arg);

            try
            {
                using var process = Process.Start(start);
                if (process == null) return (false, "无法启动 " + file);

                var stdout = process.StandardOutput.ReadToEndAsync(ct);
                var stderr = process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);
                return (process.ExitCode == 0, ((await stdout) + "\n" + (await stderr)).Trim());
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
