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
        /// <summary>上游（origin）比本地工作树更新：只提示先更新源码，不阻止继续用本地代码迭代。</summary>
        public bool UpstreamNewer { get; init; }

        /// <summary>这个仓库的 origin 地址（fork 检测用）。</summary>
        public string Remote { get; init; } = "";

        /// <summary>origin 与清单里的地址不一致：用户已经把组件换成了自己的 fork。</summary>
        public bool Forked { get; init; }

        public override string ToString() =>
            $"{(UpstreamNewer ? "UpstreamNewer " : "")}{State} source={SourceVersion} running={RunningVersion} {Detail}".Trim();
    }

    /// <summary>
    /// 启动时的源码同步：开发布局直接用同级组件仓库，安装布局才克隆/更新到 src\；
    /// 比较 OpenLoop 源码版本与运行版本（组件只报提交号）。
    /// 只 fetch 不 reset——工作树里可能有 Loop 的改动。
    /// 判定与执行分开：这里只回答「是什么状态」，迭代由 Loop 的迭代轮次负责。
    /// </summary>
    public static class SourceSync
    {
        public const string OpenLoopUrl = "https://github.com/xiaoyuvax/OpenLoop.git";
        public const string PuppetCoreUrl = "https://github.com/xiaoyuvax/PuppetCore.git";
        public const string ChatFlowUrl = "https://github.com/xiaoyuvax/ChatFlow.git";

        private static readonly string OpenLoopVersionFile = VersionBump.FileFor("a");

        public static string SourceRoot
        {
            get
            {
                // 开发环境：当前目录就是 OpenLoop 仓库时直接用它，不再往安装目录里复制一份
                string dev = DevRoot;
                if (dev != null) return dev;

                // 壳重启后 cwd 变成安装目录：重启脚本把原来的源码根带过来，避免换成本地那份旧克隆
                string inherited = Environment.GetEnvironmentVariable("OPENLOOP_SOURCE");
                if (!string.IsNullOrWhiteSpace(inherited) &&
                    File.Exists(Path.Combine(inherited.Trim(), "OpenLoop.slnx")))
                    return inherited.Trim();

                return Path.Combine(InstallRoot, "src", "openloop");
            }
        }

        /// <summary>开发检出根：当前目录就是 OpenLoop 仓库；安装布局下为 null。</summary>
        public static string DevRoot
        {
            get
            {
                try
                {
                    string cwd = Directory.GetCurrentDirectory();
                    return File.Exists(Path.Combine(cwd, "OpenLoop.slnx")) ? cwd : null;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>与给定源码根同级、且参与编译的组件仓库路径（只拼路径，不判断存在）。</summary>
        public static string SiblingOf(string devRoot, string folder)
        {
            if (string.IsNullOrWhiteSpace(devRoot) || string.IsNullOrWhiteSpace(folder)) return null;
            string parent = Path.GetDirectoryName(devRoot.TrimEnd(Path.DirectorySeparatorChar));
            return string.IsNullOrEmpty(parent) ? null : Path.Combine(parent, folder);
        }

        /// <summary>组件仓库根：与源码根同级。开发布局是同级检出，安装布局正好是 <c>src\</c> 下的克隆。</summary>
        public static string ComponentRoot(string folder)
            => SiblingOf(SourceRoot, folder) ?? Path.Combine(InstallRoot, "src", folder);

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
                return InstallRootOf(Path.GetDirectoryName(string.IsNullOrWhiteSpace(exe) ? AppContext.BaseDirectory : exe)
                                     ?? AppContext.BaseDirectory);
            }
        }

        /// <summary>
        /// A/I 的二进制装在 <c>&lt;安装根&gt;\worker\</c> 下：从 exe 目录推安装根时要退一层，
        /// 否则源码根会算成 <c>worker\source\openloop</c>（不存在，git 直接报目录无效）。
        /// </summary>
        internal static string InstallRootOf(string exeDir)
        {
            if (string.IsNullOrWhiteSpace(exeDir)) return exeDir;
            string dir = Path.GetFullPath(exeDir);
            string trimmed = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(Path.GetFileName(trimmed), "worker", StringComparison.OrdinalIgnoreCase)) return dir;
            return Path.GetDirectoryName(trimmed) ?? dir;
        }

        public static Task<SourceCheck> CheckAsync(CancellationToken ct = default)
            => CheckRepoAsync(OpenLoopUrl, SourceRoot, OpenLoopVersionFile, ct);

        /// <summary>
        /// 清单里的组件逐个同步：同级已有检出就只报提交号（不联网），没有才 clone。
        /// 清单缺条目时由 <see cref="ComponentCatalog"/> 补回内置默认项，所以老安装也能跑。
        /// </summary>
        public static async Task<IReadOnlyList<(ComponentSpec Spec, SourceCheck Check)>> CheckComponentsAsync(
            CancellationToken ct = default)
        {
            var result = new List<(ComponentSpec, SourceCheck)>();
            foreach (var spec in ComponentCatalog.Iterable())
                result.Add((spec, await CheckComponentAsync(spec, ct)));
            return result;
        }

        /// <summary>
        /// 组件仓库：同级位置已有检出（开发布局的同级仓库，或安装布局的 src\ 克隆）就直接用，
        /// 不 fetch，避免把 Loop 的改动冲掉；没有才克隆到同级位置。
        /// 清单里的地址拉不下来（写错的 fork）时退回内置官方地址再试一次。
        /// </summary>
        private static async Task<SourceCheck> CheckComponentAsync(ComponentSpec spec, CancellationToken ct)
        {
            string root = ComponentRoot(spec.Dir);
            if (File.Exists(Path.Combine(root, spec.Marker)))
                return await ReportHeadAsync(spec, root, ct);

            // 新加的 fork 条目只留了目录名：没地址就别去 clone，直接说清楚缺什么
            if (string.IsNullOrWhiteSpace(spec.Url))
                return new(SourceState.Unknown, root, "", "",
                    "清单没有仓库地址：把 fork 地址写进 components.json 的 url 再重试");

            var check = await CheckCommitAsync(spec, root, ct);
            string official = ComponentCatalog.DefaultUrl(spec.Id);
            if (check.State != SourceState.Failed || official.Length == 0
                || ComponentCatalog.SameUrl(official, spec.Url))
                return check;

            var fallback = await CheckCommitAsync(spec with { Url = official }, root, ct);
            return fallback.State == SourceState.Failed
                ? fallback with { Detail = $"清单地址 {spec.Url} 与官方地址 {official} 都拉不下来：{fallback.Detail}" }
                : fallback with { Detail = $"清单地址 {spec.Url} 不可用，已按官方地址克隆：{fallback.Detail}" };
        }

        /// <summary>可迭代组件仓库：只同步 + 报提交号。</summary>
        private static async Task<SourceCheck> CheckCommitAsync(ComponentSpec spec, string root, CancellationToken ct)
        {
            if (!await GitOkAsync(ct))
                return new(SourceState.NoGit, root, "", "", "找不到 git");

            try
            {
                var sync = await SyncAsync(spec.Url, root, ct);
                if (sync != null) return sync;

                return await ReportHeadAsync(spec, root, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new(SourceState.Failed, root, "", "", ex.Message);
            }
        }

        /// <summary>报 HEAD 提交号；origin 与清单地址不一致时一并说明（用户已经把组件换成 fork）。</summary>
        private static async Task<SourceCheck> ReportHeadAsync(ComponentSpec spec, string root, CancellationToken ct)
        {
            if (!await GitOkAsync(ct))
                return new(SourceState.NoGit, root, "", "", "找不到 git");

            try
            {
                string head = (await GitTextAsync(["-C", root, "rev-parse", "--short", "HEAD"], ct)).Trim();
                string origin = await OriginAsync(root, ct);
                bool forked = spec.Url.Length > 0 && origin.Length > 0 && !ComponentCatalog.SameUrl(origin, spec.Url);
                string detail = head.Length == 0 ? "读不到提交号" : "提交 " + head;
                if (forked)
                    detail += $"；origin 指向 {origin}（清单里是 {spec.Url}）：改动会提交到你的 fork，" +
                              "把 fork 地址交给 Loop 更新 components.json";
                return new(SourceState.UpToDate, root, head, "", detail) { Remote = origin, Forked = forked };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new(SourceState.Failed, root, "", "", ex.Message);
            }
        }

        /// <summary>仓库的 origin 地址；读不到返回空串。</summary>
        public static async Task<string> OriginAsync(string root, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(root)) return "";
            try { return (await GitTextAsync(["-C", root, "remote", "get-url", "origin"], ct)).Trim(); }
            catch { return ""; }
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

                string localPath = Path.Combine(root, versionFile);
                string localVersion = File.Exists(localPath)
                    ? ParseVersion(File.ReadAllText(localPath, Encoding.UTF8))
                    : "";

                // 开发检出（含 OPENLOOP_SOURCE 指定的工作树）里本地工作树就是要编译的源码，
                // 远端版本落后不该算「源码过旧」；安装布局的克隆也先认本地工作树（迭代的升版还没提交），
                // 远端只用来发现「上游确实有更新的源码」。
                bool local = !IsInstallClone(root);
                string remote = local
                    ? ""
                    : ParseVersion(await GitTextAsync(
                        ["-C", root, "show", "FETCH_HEAD:" + versionFile.Replace('\\', '/')], ct));

                bool upstreamNewer = !local
                                     && localVersion.Length > 0 && remote.Length > 0
                                     && Classify(remote, localVersion) == SourceState.Newer;

                string source = PickSourceVersion(localVersion, remote, local);
                if (source.Length == 0)
                    return new(SourceState.Unknown, root, "", running, "读不到源码版本");

                var state = Classify(source, running);
                string detail = upstreamNewer
                    ? $"上游有更新（origin {remote} 高于本地工作树 {localVersion}）：请先更新源码；仍可用本地代码继续迭代"
                    : state switch
                    {
                        SourceState.Newer => "源码比运行版本新：可以迭代",
                        SourceState.Older => "源码比运行版本旧：更新源码、放弃迭代，或用 Puppet 自愈",
                        SourceState.UpToDate => "源码与运行版本一致",
                        _ => "版本号无法比较"
                    };
                return new(state, root, source, running, detail) { UpstreamNewer = upstreamNewer };
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
            => RoleSourceVersion("a", root);

        /// <summary>某个角色自己的源码版本（各组件版本可以不一致）；读不到返回空串。</summary>
        public static string RoleSourceVersion(string role, string root = null)
        {
            try
            {
                string relative = VersionBump.FileFor(role);
                if (relative == null) return "";
                string path = Path.Combine(string.IsNullOrWhiteSpace(root) ? SourceRoot : root, relative);
                return File.Exists(path) ? ParseVersion(File.ReadAllText(path, Encoding.UTF8)) : "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>安装布局的克隆（<c>InstallRoot\src\...</c>）：只有这种副本才用远端版本判断有没有新源码。</summary>
        private static bool IsInstallClone(string root)
        {
            try
            {
                string clone = Path.GetFullPath(Path.Combine(InstallRoot, "src")) + Path.DirectorySeparatorChar;
                return Path.GetFullPath(root).StartsWith(clone, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 取哪个版本号当「源码版本」：本地工作树就是要编译的源码，优先它；
        /// 只有远端更高（上游确实有新源码可拉）才算远端，否则安装布局会把迭代刚写进工作树的号误判成过旧。
        /// </summary>
        internal static string PickSourceVersion(string local, string remote, bool localCheckout)
        {
            if (string.IsNullOrEmpty(local)) return remote ?? "";
            if (localCheckout || string.IsNullOrEmpty(remote)) return local;
            return Classify(remote, local) == SourceState.Newer ? remote : local;
        }

        /// <summary>比较两个版本号；无法解析返回 <see cref="SourceState.Unknown"/>。</summary>
        public static SourceState Classify(string sourceVersion, string runningVersion)
        {
            if (!Version.TryParse(Clean(sourceVersion), out var source) ||
                !Version.TryParse(Clean(runningVersion), out var running))
                return SourceState.Unknown;

            int compare = Pad(source).CompareTo(Pad(running));
            return compare > 0 ? SourceState.Newer : compare < 0 ? SourceState.Older : SourceState.UpToDate;
        }

        /// <summary>同一版本的不同写法（0.1.2 与 0.1.2.0）算相等。</summary>
        public static bool SameVersion(string left, string right)
            => Version.TryParse(Clean(left), out var a) && Version.TryParse(Clean(right), out var b) && Pad(a) == Pad(b);

        /// <summary>System.Version 把缺的段当 -1，0.1.2 与 0.1.2.0 会比出不相等；补齐成 0 再比。</summary>
        private static Version Pad(Version version)
            => new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

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

            // 没有标签时只接受「裸版本号」：整份文件内容当版本号会一路传到迭代计划里显示出来
            string bare = Clean(text.Trim());
            return Version.TryParse(bare, out _) ? bare : "";
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

        /// <summary>运行版本：优先 versions/current.json 里 A 的版本，其次 backends 目录里的 A 版本。</summary>
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
                    AppContext.BaseDirectory, "backends",
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
