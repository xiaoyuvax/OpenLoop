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
    /// 启动时的源码同步：开发布局直接用同级组件仓库，安装布局才克隆/更新到 source\；
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

                return Path.Combine(InstallRoot, "source", "openloop");
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

        /// <summary>组件仓库根：与源码根同级。开发布局是同级检出，安装布局正好是 <c>source\</c> 下的克隆。</summary>
        public static string ComponentRoot(string folder)
            => SiblingOf(SourceRoot, folder) ?? Path.Combine(InstallRoot, "source", folder);

        public static string PuppetRoot => ComponentRoot("PuppetCore");

        /// <summary>
        /// ChatFlow 与本仓库并列参与编译（App 的 ProjectReference 是 ../../../ChatFlow/ChatFlow）。
        /// 只有 Windows 版本编译它。
        /// </summary>
        public static string ChatFlowRoot => ComponentRoot("ChatFlow");

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
        /// PuppetCore 已切源码引用：这里负责同步仓库并报提交号；版本号由节点模型统一维护
        /// （`Puppet.Common.targets` 的 &lt;Version&gt;）。
        /// </summary>
        public static Task<SourceCheck> CheckPuppetCoreAsync(CancellationToken ct = default)
            => CheckComponentAsync(PuppetCoreUrl, "PuppetCore",
                Path.Combine("Puppet.Core", "Puppet.Core.csproj"), ct);

        /// <summary>
        /// ChatFlow 同样没有版本号约束：同步 + 报提交号，Loop 可以像 PuppetCore 一样迭代它。
        /// 只有 Windows 版本编译它，其它平台既不克隆也不报告。
        /// </summary>
        public static Task<SourceCheck> CheckChatFlowAsync(CancellationToken ct = default)
            => !OperatingSystem.IsWindows()
                ? Task.FromResult(new SourceCheck(SourceState.Unknown, ChatFlowRoot, "", "",
                    "非 Windows 版本：ChatFlow 不参与编译"))
                : CheckComponentAsync(ChatFlowUrl, "ChatFlow",
                    Path.Combine("ChatFlow", "ChatFlow.csproj"), ct);

        /// <summary>
        /// 组件仓库：同级位置已有检出（开发布局的同级仓库，或安装布局的 source\ 克隆）就直接用，
        /// 不 fetch，避免把 Loop 的改动冲掉；没有才克隆到同级位置。
        /// </summary>
        private static Task<SourceCheck> CheckComponentAsync(
            string url, string folder, string marker, CancellationToken ct)
        {
            string root = ComponentRoot(folder);
            if (File.Exists(Path.Combine(root, marker)))
                return ReportHeadAsync(root, "提交 ", ct);
            return CheckCommitAsync(url, root, ct);
        }

        /// <summary>可迭代组件仓库（无版本号）：只同步 + 报提交号。</summary>
        private static async Task<SourceCheck> CheckCommitAsync(string url, string root, CancellationToken ct)
        {
            if (!await GitOkAsync(ct))
                return new(SourceState.NoGit, root, "", "", "找不到 git");

            try
            {
                var sync = await SyncAsync(url, root, ct);
                if (sync != null) return sync;

                return await ReportHeadAsync(root, "已同步到 ", ct);
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

        private static async Task<SourceCheck> ReportHeadAsync(string root, string label, CancellationToken ct)
        {
            if (!await GitOkAsync(ct))
                return new(SourceState.NoGit, root, "", "", "找不到 git");

            try
            {
                string head = (await GitTextAsync(["-C", root, "rev-parse", "--short", "HEAD"], ct)).Trim();
                return new(SourceState.UpToDate, root, head, "",
                    head.Length == 0 ? "读不到提交号" : label + head);
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
                // 远端版本落后不该算「源码过旧」；安装布局的克隆才比远端，用来看有没有更新的源码可迭代。
                bool local = !IsInstallClone(root);
                string remote = local
                    ? ""
                    : ParseVersion(await GitTextAsync(
                        ["-C", root, "show", "FETCH_HEAD:" + versionFile.Replace('\\', '/')], ct));

                string source = PickSourceVersion(localVersion, remote, local);
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

        /// <summary>安装布局的克隆（<c>InstallRoot\source\...</c>）：只有这种副本才用远端版本判断有没有新源码。</summary>
        private static bool IsInstallClone(string root)
        {
            try
            {
                string clone = Path.GetFullPath(Path.Combine(InstallRoot, "source")) + Path.DirectorySeparatorChar;
                return Path.GetFullPath(root).StartsWith(clone, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 取哪个版本号当「源码版本」：本地工作树优先，安装布局用远端；
        /// 首选为空时回退另一个（本地没写过版本号，或远端读不到）。
        /// </summary>
        internal static string PickSourceVersion(string local, string remote, bool localCheckout)
        {
            if (localCheckout && !string.IsNullOrEmpty(local)) return local;
            if (!string.IsNullOrEmpty(remote)) return remote;
            return local ?? "";
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
