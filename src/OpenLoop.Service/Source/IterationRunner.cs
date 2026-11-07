using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Runtime;
using OpenLoop.Service.Shell;

namespace OpenLoop.Service.Source
{
    /// <summary>一个部件在这一轮里的版本与动作，给确认弹框用。</summary>
    public sealed record RolePlan(string Role, string Label, string Current, string Target, bool Update, string Note);

    public sealed record ShellStage(string Dir, string Version);

    public sealed class IterationReport
    {
        public string At { get; set; } = DateTimeOffset.Now.ToString("O");
        public string Workspace { get; set; } = "";
        public string Version { get; set; } = "";
        public bool Ok { get; set; }
        public bool ShellRestarted { get; set; }
        public bool RolledBack { get; set; }

        /// <summary>壳的发布目录；有值就说明最后还需要重启壳。</summary>
        public string ShellDir { get; set; }

        public List<Iteration.Step> Steps { get; set; } = [];
    }

    /// <summary>
    /// 一轮迭代的编排：先出计划 → 编译 →（壳）发布 → 换 A → A 启动后更新 I → 最后重启壳。
    /// A 与 I 不同时换；I 不能换自己，所以由刚换好的 A 接手（见 <see cref="LoopUpdater"/>）。
    /// 失败就停在原地，由调用方按迭代前的 current.json 回退。
    /// </summary>
    public static class IterationRunner
    {
        public static string RequestPath => Path.Combine(AppPaths.RuntimeDir, "loop-update.json");
        public static string ResultPath => Path.Combine(AppPaths.RuntimeDir, "loop-update-result.json");
        public static string ReportPath => Path.Combine(AppPaths.RuntimeDir, "iteration-report.json");
        public static string ShellFailedPath => Path.Combine(AppPaths.RuntimeDir, "iteration-shell-failed.txt");
        public static string ShellStartedPath => Path.Combine(AppPaths.RuntimeDir, "shell-started.json");
        public static string RestartLogPath => Path.Combine(AppPaths.RuntimeDir, "iteration-restart.log");

        /// <summary>新壳进到界面后写一次；重启脚本靠它判断「新壳真的起来了」，比只看进程存活可靠。</summary>
        public static void MarkShellStarted()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.RuntimeDir);
                File.WriteAllBytes(ShellStartedPath, JsonSerializer.SerializeToUtf8Bytes(new
                {
                    pid = Environment.ProcessId,
                    at = DateTimeOffset.Now.ToString("O")
                }, BusProtocol.Json));
            }
            catch
            {
            }
        }

        /// <summary>三个部件各自的现版本、目标版本与是否需要更新。</summary>
        public static async Task<List<RolePlan>> PlanAsync(string shellExe = null, CancellationToken ct = default)
        {
            string target = SourceSync.SourceVersion(null);
            var dirtyRoles = RolesFor(await ChangedPathsAsync(ct));

            var plans = new List<RolePlan>();
            foreach (var (role, label, note) in new[]
                     {
                         (BusRoles.A, "Worker（A）", "先换 A，由它接管 I 的更新"),
                         (BusRoles.I, "Loop（I）", "由启动后的 A 更新"),
                         ("shell", "壳", "最后重启，会退出界面")
                     })
            {
                string current = role == "shell"
                    ? ExeVersion(string.IsNullOrWhiteSpace(shellExe) ? Environment.ProcessPath : shellExe)
                    : RoleVersion(role);
                bool newer = target.Length > 0 && current.Length > 0 &&
                             SourceSync.Classify(target, current) == SourceState.Newer;
                plans.Add(new RolePlan(role, label, current, target, newer || dirtyRoles.Contains(role), note));
            }

            // I 要更新就必须重启一次 A（A 才能接手），A 本身不一定重编
            if (plans.First(p => p.Role == BusRoles.I).Update)
            {
                int index = plans.FindIndex(p => p.Role == BusRoles.A);
                if (!plans[index].Update)
                    plans[index] = plans[index] with { Note = "只为接手 I 的更新而重启" };
            }

            return plans;
        }

        /// <summary>
        /// 改动文件 → 需要重编的部件：Core/Service 是全共享，A/I/App 各管自己，
        /// 文档、测试、脚本不动二进制。拿不准的路径按「全都重编」处理。
        /// </summary>
        internal static HashSet<string> RolesFor(IEnumerable<string> paths)
        {
            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in paths ?? [])
            {
                string path = (raw ?? "").Replace('\\', '/').Trim().Trim('"');
                if (path.Length == 0) continue;

                int arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrow >= 0)
                {
                    AddRole(roles, path[..arrow]);
                    AddRole(roles, path[(arrow + 4)..]);
                    continue;
                }
                AddRole(roles, path);
            }
            return roles;
        }

        private static void AddRole(HashSet<string> roles, string path)
        {
            path = path.Trim().Trim('"');
            if (path.Length == 0) return;

            if (path.StartsWith("src/OpenLoop.Core/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("src/OpenLoop.Service/", StringComparison.OrdinalIgnoreCase))
            {
                roles.UnionWith([BusRoles.A, BusRoles.I, "shell"]);
                return;
            }
            if (path.StartsWith("src/OpenLoop.A/", StringComparison.OrdinalIgnoreCase))
            {
                roles.Add(BusRoles.A);
                return;
            }
            if (path.StartsWith("src/OpenLoop.I/", StringComparison.OrdinalIgnoreCase))
            {
                roles.Add(BusRoles.I);
                return;
            }
            if (path.StartsWith("src/OpenLoop.App/", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("OpenLoop.ico", StringComparison.OrdinalIgnoreCase))
            {
                roles.Add("shell");
                return;
            }
            if (path.StartsWith("src/", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("OpenLoop.slnx", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("Directory.Build.", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("global.json", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("NuGet.config", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            {
                roles.UnionWith([BusRoles.A, BusRoles.I, "shell"]);
                return;
            }
            if (path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("samples/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(".github/", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                path.Equals(".gitignore", StringComparison.OrdinalIgnoreCase) ||
                path.Equals(".gitattributes", StringComparison.OrdinalIgnoreCase) ||
                path.Equals(".editorconfig", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("LICENSE", StringComparison.OrdinalIgnoreCase))
                return;

            roles.UnionWith([BusRoles.A, BusRoles.I, "shell"]);
        }

        /// <summary>变更摘要：未提交文件、diff 规模、最近提交。给确认弹框用。</summary>
        public static async Task<string> ChangeSummaryAsync(CancellationToken ct = default)
        {
            string root = SourceSync.SourceRoot;
            var sb = new StringBuilder();

            var status = await Iteration.ExecAsync("git", ["-C", root, "status", "--porcelain"], root, ct);
            var files = status.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            sb.AppendLine(files.Count == 0 ? "工作树干净（按版本号判断）" : $"未提交改动：{files.Count} 个文件");
            foreach (string file in files.Take(8)) sb.AppendLine("  " + file);
            if (files.Count > 8) sb.AppendLine($"  …另有 {files.Count - 8} 个");

            var stat = await Iteration.ExecAsync("git", ["-C", root, "diff", "--stat"], root, ct);
            foreach (string line in stat.Output.Split('\n').TakeLast(2))
                if (line.Trim().Length > 0) sb.AppendLine(line.Trim());

            var log = await Iteration.ExecAsync("git", ["-C", root, "log", "--oneline", "-3"], root, ct);
            if (log.Ok && log.Output.Trim().Length > 0)
            {
                sb.AppendLine("最近提交：");
                foreach (string line in log.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(3))
                    sb.AppendLine("  " + line.Trim());
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 执行整轮（不含重启壳，那一步会把界面关掉，由调用方做）。
        /// 失败就停在原地并回传已完成的步骤，由调用方回退与报告。
        /// </summary>
        public static async Task<IterationReport> RunAsync(
            List<RolePlan> plan, string workspace, Action<string> progress, CancellationToken ct = default)
        {
            plan ??= [];
            var report = new IterationReport { Workspace = workspace, Version = plan.FirstOrDefault()?.Target ?? "" };
            void Add(Iteration.Step step)
            {
                report.Steps.Add(step);
                progress?.Invoke(step.ToString());
            }

            bool needA = plan.FirstOrDefault(p => p.Role == BusRoles.A)?.Update == true;
            bool needI = plan.FirstOrDefault(p => p.Role == BusRoles.I)?.Update == true;
            bool needShell = plan.FirstOrDefault(p => p.Role == "shell")?.Update == true;

            var build = await Iteration.BuildAsync(ct);
            Add(build);
            if (!build.Ok) return Finish(report, false);

            if (needShell)
            {
                var publish = await PublishShellAsync(report.Version, ct);
                Add(publish);
                if (!publish.Ok) return Finish(report, false);
                report.ShellDir = publish.Detail;
            }

            if (needA || needI)
            {
                var publish = await Iteration.PublishAsync(BusRoles.A, report.Version, ct);
                Add(publish);
                if (!publish.Ok) return Finish(report, false);

                var stage = await Iteration.StageAsync(BusRoles.A, publish.Detail, report.Version, ct);
                Add(stage);
                if (!stage.Ok) return Finish(report, false);

                // 交接单：A 起来以后自己发布 / 登记 / 重启 I
                if (needI) WriteLoopUpdate(report.Version);
                else ClearLoopUpdate();

                int previousPid = RuntimeInfo.TryLoad(AppPaths.RuntimeFile(BusRoles.A))?.Pid ?? 0;
                var restart = await Iteration.RestartAsync(BusRoles.A, ct);
                Add(restart);
                if (!restart.Ok) return Finish(report, false);

                // 必须等到「新 pid」报到：旧 A 在退出前还活着，只看到活进程会误判成已经换好
                var back = await WaitRestartedAsync(BusRoles.A, previousPid, TimeSpan.FromSeconds(45), ct);
                Add(back);
                if (!back.Ok) return Finish(report, false);

                if (needI)
                {
                    var loop = await WaitLoopUpdateAsync(TimeSpan.FromMinutes(6), ct);
                    Add(loop);
                    if (!loop.Ok) return Finish(report, false);
                }
            }

            return Finish(report, true);
        }

        /// <summary>迭代前 current.json 里、迭代中确实换过、且旧目录还在的角色。</summary>
        internal static List<string> RolesToRollback(
            IReadOnlyDictionary<string, VersionEntry> before, VersionStore store)
        {
            var roles = new List<string>();
            foreach (string role in new[] { BusRoles.I, BusRoles.A })
            {
                if (before == null || !before.TryGetValue(role, out var previous)) continue;
                if (string.IsNullOrWhiteSpace(previous?.Dir) || !Directory.Exists(previous.Dir)) continue;
                if (string.Equals(store.Current(role)?.Dir, previous.Dir, StringComparison.OrdinalIgnoreCase))
                    continue;
                roles.Add(role);
            }
            return roles;
        }

        /// <summary>失败回退：把换过的角色重新登记回旧目录再重启（先 I 后 A）。</summary>
        public static async Task<List<Iteration.Step>> RollbackAsync(
            IReadOnlyDictionary<string, VersionEntry> before, Action<string> progress,
            CancellationToken ct = default, VersionStore store = null)
        {
            store ??= new VersionStore();
            var steps = new List<Iteration.Step>();
            var roles = RolesToRollback(before, store);
            if (roles.Count == 0)
            {
                steps.Add(new("rollback", true, "没有换过版本，无需回退"));
                progress?.Invoke(steps[^1].ToString());
                return steps;
            }

            foreach (string role in roles)
            {
                var previous = before[role];
                var stage = await Iteration.StageAsync(role, previous.Dir, previous.Version, ct);
                steps.Add(stage);
                progress?.Invoke(stage.ToString());
                if (!stage.Ok) continue;

                var restart = await Iteration.RestartAsync(role, ct);
                steps.Add(restart);
                progress?.Invoke(restart.ToString());
            }
            return steps;
        }

        /// <summary>纯文本报告：进度框与重启后的结果窗口共用。</summary>
        public static string ReportText(IterationReport report)
        {
            if (report == null) return "";
            var sb = new StringBuilder();
            sb.AppendLine($"版本 {report.Version} · {report.At}");
            sb.AppendLine(report.Ok ? "结果：成功" : "结果：失败");
            if (report.ShellRestarted) sb.AppendLine("壳：已重启到新版本");
            if (report.RolledBack) sb.AppendLine("已回退到迭代前的版本");
            sb.AppendLine("步骤：");
            foreach (var step in report.Steps) sb.AppendLine("  " + step);
            return sb.ToString().TrimEnd();
        }

        /// <summary>把壳的发布产物放到 versions/shell/&lt;版本-sha8&gt;；运行目录等最后一步再覆盖。</summary>
        public static async Task<Iteration.Step> PublishShellAsync(string version, CancellationToken ct = default)
        {
            string root = SourceSync.SourceRoot;
            string temp = Path.Combine(Path.GetTempPath(), "openloop-shell-" + Guid.NewGuid().ToString("N"));
            try
            {
                var (ok, output) = await Iteration.ExecAsync("dotnet",
                    ["publish", Path.Combine("src", "OpenLoop.App"), "-c", "Release", "-o", temp, "--nologo", "-v", "q"],
                    root, ct);
                if (!ok) return new("publish shell", false, Iteration.Tail(output, 12));

                string exe = Path.Combine(temp, OperatingSystem.IsWindows() ? "OpenLoop.exe" : "OpenLoop");
                if (!File.Exists(exe)) return new("publish shell", false, "没有产物 " + exe);

                string dir = Path.Combine(AppPaths.VersionsDir, "shell", Iteration.VersionDirName(version, Sha256(exe)));
                Directory.CreateDirectory(dir);
                foreach (string file in Directory.EnumerateFiles(temp, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(dir, Path.GetRelativePath(temp, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, true);
                }
                return new("publish shell", true, dir);
            }
            catch (Exception ex)
            {
                return new("publish shell", false, ex.Message);
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
            }
        }

        /// <summary>
        /// 退出壳，由独立脚本把发布目录覆盖到运行目录并重新拉起。
        /// 只覆盖新壳带的文件（不 purge），覆盖前把会被覆盖的旧文件备份；新壳 8 秒内退出就从备份还原并起旧壳。
        /// 报告先落盘，新壳起来后读它把结果弹出来。
        /// </summary>
        public static void LaunchRestarter(ShellStage shell, string workspace)
        {
            if (string.IsNullOrWhiteSpace(shell?.Dir) || !Directory.Exists(shell.Dir)) return;

            string install = AppContext.BaseDirectory.TrimEnd('\\', '/');
            string exe = Path.Combine(install, OperatingSystem.IsWindows() ? "OpenLoop.exe" : "OpenLoop");
            string from = Path.GetFullPath(shell.Dir).TrimEnd('\\', '/');
            string backup = Path.Combine(AppPaths.VersionsDir, "shell",
                "rollback-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            string script = Path.Combine(Path.GetTempPath(), "openloop-restart-" + Guid.NewGuid().ToString("N") + ".ps1");
            string args = $"--loop \"{(workspace ?? "").Trim()}\" --iteration";
            int pid = Environment.ProcessId;

            if (string.Equals(from, Path.GetFullPath(install), StringComparison.OrdinalIgnoreCase))
                return;

            PruneShell(shell.Dir);

            File.WriteAllText(script,
                RestartScript(pid, install, exe, args, from, backup, ShellFailedPath, ShellStartedPath, RestartLogPath),
                new UTF8Encoding(true));

            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            // 新壳的 cwd 会变成安装目录：把源码根带给它，后续迭代仍对着同一份源码
            start.Environment["OPENLOOP_SOURCE"] = SourceSync.SourceRoot;
            foreach (string arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script })
                start.ArgumentList.Add(arg);
            Process.Start(start);
        }

        /// <summary>壳的发布目录与回退备份都占空间：只留最近几份。</summary>
        internal static void PruneShell(string keepDir, int keepVersions = 3, int keepBackups = 2)
        {
            try
            {
                string root = Path.Combine(AppPaths.VersionsDir, "shell");
                if (!Directory.Exists(root)) return;
                string keep = Path.GetFullPath(keepDir ?? "");

                foreach (var (pattern, keepCount) in new[] { ("rollback-*", keepBackups), ("*", keepVersions) })
                {
                    var old = Directory.GetDirectories(root, pattern)
                        .Where(d => pattern != "*" ||
                                    !Path.GetFileName(d).StartsWith("rollback-", StringComparison.OrdinalIgnoreCase))
                        .Where(d => !string.Equals(Path.GetFullPath(d), keep, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(Directory.GetLastWriteTimeUtc)
                        .Skip(keepCount);
                    foreach (string dir in old)
                    {
                        try { Directory.Delete(dir, true); } catch { }
                    }
                }
            }
            catch
            {
            }
        }

        internal static string RestartScript(
            int pid, string install, string exe, string args, string from, string backup,
            string failedMarker, string startedMarker, string log)
        {
            string q(string value) => "'" + (value ?? "").Replace("'", "''") + "'";
            return $$"""
                $ErrorActionPreference = 'Continue'
                while (Get-Process -Id {{pid}} -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 300 }

                New-Item -ItemType Directory -Force -Path {{q(backup)}} | Out-Null
                $fromLen = {{q(from)}}.Length + 1
                foreach ($f in (Get-ChildItem -LiteralPath {{q(from)}} -Recurse -File)) {
                    $rel = $f.FullName.Substring($fromLen)
                    $old = Join-Path {{q(install)}} $rel
                    if (Test-Path -LiteralPath $old) {
                        $dst = Join-Path {{q(backup)}} $rel
                        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dst) | Out-Null
                        Copy-Item -LiteralPath $old -Destination $dst -Force
                    }
                    $target = Join-Path {{q(install)}} $rel
                    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
                    Copy-Item -LiteralPath $f.FullName -Destination $target -Force
                }

                Remove-Item -LiteralPath {{q(startedMarker)}} -Force -ErrorAction SilentlyContinue
                $proc = Start-Process -FilePath {{q(exe)}} -WorkingDirectory {{q(install)}} -ArgumentList {{q(args)}} -PassThru
                # 新壳进到界面会写 startedMarker；只看进程存活会把「起来后又崩」当成成功
                $back = $false
                for ($i = 0; $i -lt 60; $i++) {
                    Start-Sleep -Milliseconds 500
                    if (Test-Path -LiteralPath {{q(startedMarker)}}) { break }
                    if (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) { $back = $true; break }
                }
                if (-not (Test-Path -LiteralPath {{q(startedMarker)}})) { $back = $true }

                if ($back) {
                    $code = 'unknown'
                    try { $proc.Refresh(); if ($proc.HasExited) { $code = $proc.ExitCode } } catch { }
                    Add-Content -LiteralPath {{q(log)}} -Value ("restart failed " + (Get-Date -Format o) + " pid=" + $proc.Id + " code=" + $code) -Encoding utf8
                    $backupLen = {{q(backup)}}.Length + 1
                    foreach ($f in (Get-ChildItem -LiteralPath {{q(backup)}} -Recurse -File)) {
                        $rel = $f.FullName.Substring($backupLen)
                        Copy-Item -LiteralPath $f.FullName -Destination (Join-Path {{q(install)}} $rel) -Force
                    }
                    Set-Content -LiteralPath {{q(failedMarker)}} -Value 'new shell failed to start' -Encoding utf8
                    Remove-Item -LiteralPath {{q(startedMarker)}} -Force -ErrorAction SilentlyContinue
                    Start-Process -FilePath {{q(exe)}} -WorkingDirectory {{q(install)}} -ArgumentList {{q(args)}} | Out-Null
                } else {
                    Add-Content -LiteralPath {{q(log)}} -Value ("restart ok " + (Get-Date -Format o) + " pid=" + $proc.Id) -Encoding utf8
                }
                """;
        }

        public static void WriteReport(IterationReport report)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.RuntimeDir);
                string temp = ReportPath + ".tmp";
                File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(report, BusProtocol.Json));
                File.Move(temp, ReportPath, true);
            }
            catch
            {
            }
        }

        /// <summary>取启动前留下的报告（10 分钟内才算这次重启的），取走即删。</summary>
        public static IterationReport TakeReport(TimeSpan? maxAge = null)
        {
            try
            {
                if (!File.Exists(ReportPath)) return null;
                var report = JsonSerializer.Deserialize<IterationReport>(File.ReadAllBytes(ReportPath), BusProtocol.Json);
                File.Delete(ReportPath);
                if (report == null) return null;
                if (DateTimeOffset.TryParse(report.At, out var at) &&
                    DateTimeOffset.Now - at > (maxAge ?? TimeSpan.FromMinutes(10)))
                    return null;
                return report;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>重启脚本发现新壳没起来并已回退：启动后要把它算作失败。</summary>
        public static bool TakeShellFailure()
        {
            try
            {
                if (!File.Exists(ShellFailedPath)) return false;
                File.Delete(ShellFailedPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>A 启动时读交接单；读到就删掉，保证只跑一次。</summary>
        public static (string Version, bool Present) TakeLoopUpdate()
        {
            try
            {
                if (!File.Exists(RequestPath)) return ("", false);
                var request = JsonSerializer.Deserialize<LoopUpdateRequest>(File.ReadAllBytes(RequestPath), BusProtocol.Json);
                File.Delete(RequestPath);
                return (request?.Version ?? "", true);
            }
            catch
            {
                return ("", false);
            }
        }

        public static void WriteLoopResult(Iteration.Step step, string version)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.RuntimeDir);
                var payload = new LoopUpdateResult
                {
                    Ok = step.Ok,
                    Version = version,
                    Detail = step.Detail,
                    Steps = [step]
                };
                File.WriteAllBytes(ResultPath, JsonSerializer.SerializeToUtf8Bytes(payload, BusProtocol.Json));
            }
            catch
            {
            }
        }

        private static void WriteLoopUpdate(string version)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.RuntimeDir);
                // 上一轮的 result 必须清掉，否则 WaitLoopUpdateAsync 会立刻读到旧报告、以为 I 已经更完
                File.Delete(ResultPath);
                File.WriteAllBytes(RequestPath,
                    JsonSerializer.SerializeToUtf8Bytes(new LoopUpdateRequest { Version = version }, BusProtocol.Json));
            }
            catch
            {
            }
        }

        private static void ClearLoopUpdate()
        {
            try { File.Delete(RequestPath); } catch { }
            try { File.Delete(ResultPath); } catch { }
        }

        private static IterationReport Finish(IterationReport report, bool ok)
        {
            report.Ok = ok;
            report.At = DateTimeOffset.Now.ToString("O");
            if (!ok) ClearLoopUpdate();
            return report;
        }

        /// <summary>等到一个新 pid 报到；只看到活进程不够（旧进程退出前也活着）。</summary>
        public static async Task<Iteration.Step> WaitRestartedAsync(
            string role, int previousPid, TimeSpan timeout, CancellationToken ct = default)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var info = RuntimeInfo.TryLoad(AppPaths.RuntimeFile(role));
                if (info is { IsAlive: true } && info.Pid != previousPid)
                    return new("restart " + role, true, $"pid={info.Pid} version={info.Version}");
                await Task.Delay(500, ct);
            }
            return new("restart " + role, false, $"{(int)timeout.TotalSeconds}s 内没等到新进程（壳会按更新单回退）");
        }

        private static async Task<Iteration.Step> WaitLoopUpdateAsync(TimeSpan timeout, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    if (File.Exists(ResultPath))
                    {
                        var result = JsonSerializer.Deserialize<LoopUpdateResult>(
                            File.ReadAllBytes(ResultPath), BusProtocol.Json);
                        if (result != null)
                            return new("update i", result.Ok, result.Detail ?? "");
                    }
                }
                catch
                {
                }
                await Task.Delay(700, ct);
            }
            return new("update i", false, $"{(int)timeout.TotalMinutes} 分钟内 A 没回音");
        }

        private static async Task<List<string>> ChangedPathsAsync(CancellationToken ct)
        {
            var status = await Iteration.ExecAsync("git", ["-C", SourceSync.SourceRoot, "status", "--porcelain"],
                SourceSync.SourceRoot, ct);
            if (!status.Ok) return [];

            return status.Output.Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Where(l => l.Length > 3)
                .Select(l => l[3..].Trim())
                .Where(l => l.Length > 0)
                .ToList();
        }

        public static string RoleVersion(string role)
        {
            var runtime = RuntimeInfo.TryLoad(AppPaths.RuntimeFile(role));
            if (!string.IsNullOrWhiteSpace(runtime?.Version)) return runtime.Version;

            var store = new VersionStore();
            string exe = store.Exe(role);
            if (string.IsNullOrWhiteSpace(exe))
                exe = Path.Combine(AppContext.BaseDirectory, "worker", VersionStore.ExeName(role));
            return ExeVersion(exe);
        }

        public static string ExeVersion(string exe)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return "";
                string raw = FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "";
                return SourceSync.ParseVersion(raw);
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

        private sealed class LoopUpdateRequest
        {
            public string Version { get; set; }
        }

        private sealed class LoopUpdateResult
        {
            public bool Ok { get; set; }
            public string Version { get; set; }
            public string Detail { get; set; }
            public List<Iteration.Step> Steps { get; set; } = [];
        }
    }
}
