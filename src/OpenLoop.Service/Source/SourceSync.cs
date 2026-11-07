using System.Diagnostics;
using System.Text;
using OpenLoop.Service.Runtime;
using OpenLoop.Service.Shell;

namespace OpenLoop.Service.Source
{
    public enum SourceState
    {
        Unknown,
        NoGit,
        Failed,
        UpToDate,
        Newer,
        Older
    }

    public sealed record SourceCheck(
        SourceState State, string Root, string SourceVersion, string RunningVersion, string Detail)
    {
        public override string ToString() =>
            $"{State} source={SourceVersion} running={RunningVersion} {Detail}".Trim();
    }

    /// <summary>
    /// 启动时的源码同步：克隆/更新 OpenLoop 与 PuppetCore 仓库，比较源码版本与运行版本。
    /// 只 fetch 不 reset——工作树里可能有 Loop 的改动。
    /// 判定与执行分开：这里只回答「是什么状态」，迭代由 Loop 的迭代轮次负责。
    /// </summary>
    public static class SourceSync
    {
        public const string OpenLoopUrl = "https://github.com/xiaoyuvax/OpenLoop.git";
        public const string PuppetCoreUrl = "https://github.com/xiaoyuvax/PuppetCore.git";

        private const string OpenLoopVersionFile = "src/OpenLoop.A/OpenLoop.A.csproj";

        public static string SourceRoot
        {
            get
            {
                // 开发环境：当前目录就是 OpenLoop 仓库时直接用它，不再往安装目录里复制一份
                string cwd = Directory.GetCurrentDirectory();
                if (File.Exists(Path.Combine(cwd, "OpenLoop.slnx"))) return cwd;

                // 壳重启后 cwd 变成安装目录：重启脚本把原来的源码根带过来，避免换成本地那份旧克隆
                string inherited = Environment.GetEnvironmentVariable("OPENLOOP_SOURCE");
                if (!string.IsNullOrWhiteSpace(inherited) &&
                    File.Exists(Path.Combine(inherited.Trim(), "OpenLoop.slnx")))
                    return inherited.Trim();

                return Path.Combine(InstallRoot, "source", "openloop");
            }
        }

        public static string PuppetRoot => Path.Combine(InstallRoot, "source", "puppetcore");

        /// <summary>
        /// 安装根：可执行文件所在目录（可用 <c>OPENLOOP_HOME</c> 覆盖）。
        /// 源码跟二进制放在一起，权限随安装位置——装在私有目录源码就是私有的，
        /// 装在公共目录源码就是公有的；不会出现「源码在私有目录、二进制在公共目录」的跨用户竞争。
        /// </summary>
        public static string InstallRoot
        {
            get
            {
                string custom = Environment.GetEnvironmentVariable("OPENLOOP_HOME");
                if (!string.IsNullOrWhiteSpace(custom)) return Path.GetFullPath(custom.Trim());

                string exe = Environment.ProcessPath;
                return Path.GetDirectoryName(string.IsNullOrWhiteSpace(exe) ? AppContext.BaseDirectory : exe)
                       ?? AppContext.BaseDirectory;
            }
        }

        public static Task<SourceCheck> CheckAsync(CancellationToken ct = default)
            => CheckRepoAsync(OpenLoopUrl, SourceRoot, OpenLoopVersionFile, ct);

        /// <summary>
        /// PuppetCore 没有 csproj 版本号：只同步 + 报提交号（Loop 用它演进 Puppet 工具，不做版本比较）。
        /// </summary>
        public static async Task<SourceCheck> CheckPuppetCoreAsync(CancellationToken ct = default)
        {
            if (!await GitOkAsync(ct))
                return new(SourceState.NoGit, PuppetRoot, "", "", "找不到 git");

            try
            {
                var sync = await SyncAsync(PuppetCoreUrl, PuppetRoot, ct);
                if (sync != null) return sync;

                string head = (await GitTextAsync(["-C", PuppetRoot, "rev-parse", "--short", "HEAD"], ct)).Trim();
                return new(SourceState.UpToDate, PuppetRoot, head, "",
                    head.Length == 0 ? "读不到提交号" : "已同步到 " + head);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new(SourceState.Failed, PuppetRoot, "", "", ex.Message);
            }
        }

        /// <summary>克隆或 fetch 一个仓库；失败时返回具体的失败状态，成功返回 null。</summary>
        private static async Task<SourceCheck> SyncAsync(string url, string root, CancellationToken ct)
        {
            if (!Directory.Exists(Path.Combine(root, ".git")))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(root)!);
                var clone = await GitAsync(["clone", "--depth", "1", url, root], ct, TimeSpan.FromMinutes(3));
                return clone.Ok
                    ? null
                    : new(SourceState.Failed, root, "", "", "克隆失败：" + FirstLine(clone.Error));
            }

            // 只 fetch：工作树里可能有 Loop 的改动，绝不 reset/checkout
            await GitAsync(["-C", root, "fetch", "--depth", "1", "origin"], ct, TimeSpan.FromMinutes(2));
            return null;
        }

        public static async Task<SourceCheck> CheckRepoAsync(
            string url, string root, string versionFile, CancellationToken ct = default)
        {
            string running = RunningVersion();

            if (!await GitOkAsync(ct))
                return new(SourceState.NoGit, root, "", running, "找不到 git");

            try
            {
                var sync = await SyncAsync(url, root, ct);
                if (sync != null) return sync with { RunningVersion = running };

                string remote = await GitTextAsync(
                    ["-C", root, "show", "FETCH_HEAD:" + versionFile.Replace('\\', '/')], ct);
                string source = ParseVersion(remote);

                if (source.Length == 0)
                {
                    string localPath = Path.Combine(root, versionFile);
                    if (File.Exists(localPath)) source = ParseVersion(File.ReadAllText(localPath, Encoding.UTF8));
                }
                if (source.Length == 0)
                    return new(SourceState.Unknown, root, "", running, "读不到源码版本");

                var state = Classify(source, running);
                return new(state, root, source, running, state switch
                {
                    SourceState.Newer => "源码比运行版本新：可以迭代",
                    SourceState.Older => "源码比运行版本旧：更新源码、放弃迭代，或用 Puppet 自愈",
                    SourceState.UpToDate => "源码与运行版本一致",
                    _ => "版本号无法比较"
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new(SourceState.Failed, root, "", running, ex.Message);
            }
        }

        /// <summary>源码工作树里的版本号（迭代发布时用作新版本名）。</summary>
        public static string SourceVersion(string root)
        {
            try
            {
                string path = Path.Combine(string.IsNullOrWhiteSpace(root) ? SourceRoot : root, OpenLoopVersionFile);
                return File.Exists(path) ? ParseVersion(File.ReadAllText(path, Encoding.UTF8)) : "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>比较两个版本号；无法解析返回 <see cref="SourceState.Unknown"/>。</summary>
        public static SourceState Classify(string sourceVersion, string runningVersion)
        {
            if (!Version.TryParse(Clean(sourceVersion), out var source) ||
                !Version.TryParse(Clean(runningVersion), out var running))
                return SourceState.Unknown;

            int compare = source.CompareTo(running);
            return compare > 0 ? SourceState.Newer : compare < 0 ? SourceState.Older : SourceState.UpToDate;
        }

        /// <summary>从 csproj/AssemblyInformationalVersion 文本里取版本号（去掉 +sha / -preview）。</summary>
        public static string ParseVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            int tag = text.IndexOf("<Version>", StringComparison.OrdinalIgnoreCase);
            if (tag >= 0)
            {
                int start = tag + "<Version>".Length;
                int end = text.IndexOf("</Version>", start, StringComparison.OrdinalIgnoreCase);
                if (end > start) return Clean(text[start..end]);
            }

            return Clean(text.Trim());
        }

        private static string Clean(string value)
        {
            string text = (value ?? "").Trim();
            int plus = text.IndexOf('+');
            if (plus >= 0) text = text[..plus];
            int dash = text.IndexOf('-');
            if (dash > 0 && Version.TryParse(text[..dash], out _)) text = text[..dash];
            return text;
        }

        /// <summary>运行版本：优先 versions/current.json 里 A 的版本，其次 worker 目录里的 A 版本。</summary>
        public static string RunningVersion()
        {
            try
            {
                var current = new VersionStore().Current("a");
                if (!string.IsNullOrWhiteSpace(current?.Version)) return Clean(current.Version);
            }
            catch
            {
            }

            try
            {
                string worker = Path.Combine(
                    AppContext.BaseDirectory, "worker",
                    OperatingSystem.IsWindows() ? "OpenLoop.A.exe" : "OpenLoop.A");
                if (File.Exists(worker))
                {
                    string product = FileVersionInfo.GetVersionInfo(worker).ProductVersion ?? "";
                    string value = Clean(product);
                    if (value.Length > 0) return value;
                }
            }
            catch
            {
            }

            var assembly = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
            return assembly == null ? "" : Clean($"{assembly.Major}.{assembly.Minor}.{assembly.Build}");
        }

        private static async Task<bool> GitOkAsync(CancellationToken ct)
            => (await GitAsync(["version"], ct, TimeSpan.FromSeconds(20))).Ok;

        private sealed record GitResult(bool Ok, string Out, string Error);

        private static async Task<GitResult> GitAsync(
            string[] args, CancellationToken ct, TimeSpan? timeout = null)
        {
            var info = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            info.Environment["GIT_TERMINAL_PROMPT"] = "0";
            info.Environment["GCM_INTERACTIVE"] = "never";

            using var process = new Process { StartInfo = info };
            try
            {
                if (!process.Start()) return new(false, "", "git 启动失败");
            }
            catch (Exception ex)
            {
                return new(false, "", ex.Message);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (timeout is { } limit) cts.CancelAfter(limit);

            Task<string> stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(cts.Token);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                if (ct.IsCancellationRequested) throw;
                return new(false, "", "git 超时");
            }

            return new(process.ExitCode == 0, await stdout, await stderr);
        }

        private static async Task<string> GitTextAsync(string[] args, CancellationToken ct)
            => (await GitAsync(args, ct, TimeSpan.FromMinutes(1))).Out;

        private static string FirstLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            int newline = text.IndexOf('\n');
            return (newline < 0 ? text : text[..newline]).Trim();
        }
    }
}
