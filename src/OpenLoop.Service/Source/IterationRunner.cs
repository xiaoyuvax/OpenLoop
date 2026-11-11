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

        /// <summary>
        /// 七个节点的「现版本 → 目标版本」：先 restore（指纹取的是解析后的包闭包），
        /// 再按依赖图算指纹并级联升版。上游的内容或号一变，下游的指纹必然变，不需要另写级联规则。
        /// </summary>
        public static async Task<List<RolePlan>> PlanAsync(string shellExe = null, CancellationToken ct = default)
        {
            string root = SourceSync.SourceRoot;
            // restore 失败不在这里拦：真正的错误由 build 给出，但指纹会退化成「没有包闭包」
            await Iteration.ExecAsync("dotnet",
                ["restore", "OpenLoop.slnx", "--nologo", "-v", "q"], root, ct);

            var state = VersionState.Read();
            var planned = VersionState.ReadFile(VersionState.PendingPath);
            var scan = new VersionScan();
            var pending = new Dictionary<string, NodeState>(StringComparer.OrdinalIgnoreCase);

            var plans = Decide(
                VersionScan.ActiveNodes(),
                state,
                planned,
                DeclaredVersion,
                (node, declared) => CurrentVersion(node.Id, declared, state, shellExe),
                scan.Fingerprint,
                (node, version) => VersionBump.WriteVersion(node.Id, null, version),
                (node, nodeState) => pending[node.Id] = nodeState);

            VersionState.SavePending(pending);

            // I 要更新就必须重启一次 A（A 才能接手），A 本身不一定重编
            if (plans.FirstOrDefault(p => p.Role == BusRoles.I)?.Update == true)
            {
                int index = plans.FindIndex(p => p.Role == BusRoles.A);
                if (index >= 0 && !plans[index].Update)
                    plans[index] = plans[index] with { Note = "只为接手 I 的更新而重启" };
            }

            return plans;
        }

        /// <summary>
        /// 计划判断本身（不碰 git、不碰 dotnet）：冷启动（没有基准）时所有节点都算污染，
        /// 只升 patch；指纹或人工改过的号与基准不一致时也要重编。
        /// </summary>
        internal static List<RolePlan> Decide(
            IReadOnlyList<VersionNode> nodes,
            IReadOnlyDictionary<string, NodeState> state,
            IReadOnlyDictionary<string, NodeState> planned,
            Func<VersionNode, string> declaredOf,
            Func<VersionNode, string, string> currentOf,
            Func<VersionNode, IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>, NodeFingerprint> fingerprintOf,
            Action<VersionNode, string> write,
            Action<VersionNode, NodeState> record = null)
        {
            var plans = new List<RolePlan>();
            var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var fingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool cold = state == null || state.Count == 0;
            string at = DateTimeOffset.Now.ToString("O");

            foreach (var node in nodes ?? [])
            {
                string declared = declaredOf(node) ?? "";
                string current = currentOf(node, declared) ?? "";
                var fingerprint = fingerprintOf(node, fingerprints, versions);

                NodeState previous = null;
                bool known = !cold && state.TryGetValue(node.Id, out previous) && previous != null;
                bool polluted = !known
                                || !string.Equals(previous.Fingerprint, fingerprint.Value, StringComparison.Ordinal)
                                // 号被人手工改过（没经过上一轮计划）：按新号重编，不能悄悄跳过
                                || !SourceSync.SameVersion(previous.Version, declared);

                string target = declared;
                if (polluted)
                {
                    // 上一轮计划过、但还没发布成功：沿用同一个号重试，不再往上跳
                    bool retry = planned != null
                                 && planned.TryGetValue(node.Id, out var earlier) && earlier != null
                                 && string.Equals(earlier.Fingerprint, fingerprint.Value, StringComparison.Ordinal)
                                 && SourceSync.SameVersion(earlier.Version, declared);
                    if (!retry)
                    {
                        target = VersionBump.NextVersion(declared, current) ?? declared;
                        if (target.Length > 0 && target != declared) write?.Invoke(node, target);
                    }
                }

                versions[node.Id] = target;
                fingerprints[node.Id] = fingerprint.Value;
                record?.Invoke(node, new NodeState
                {
                    Version = target,
                    Fingerprint = fingerprint.Value,
                    Deps = fingerprint.Deps,
                    At = at
                });
                plans.Add(new RolePlan(node.Id, node.Label, current, target, polluted, NoteFor(node.Id)));
            }

            return plans;
        }

        /// <summary>节点声明的版本（版本文件里那一行）；读不到返回空串。</summary>
        private static string DeclaredVersion(VersionNode node)
        {
            try
            {
                string path = Path.IsPathRooted(node.VersionFile)
                    ? node.VersionFile
                    : Path.Combine(node.RepoRoot, node.VersionFile);
                return File.Exists(path) ? SourceSync.ParseVersion(File.ReadAllText(path, Encoding.UTF8)) : "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>现版本：A/I/壳看运行态，库与外部组件看基准；都读不到就用声明号（冷启动时它会被 patch+1）。</summary>
        private static string CurrentVersion(
            string id, string declared, IReadOnlyDictionary<string, NodeState> state, string shellExe)
        {
            string current = id switch
            {
                BusRoles.A or BusRoles.I => RoleVersion(id),
                "shell" => ExeVersion(string.IsNullOrWhiteSpace(shellExe) ? Environment.ProcessPath : shellExe),
                _ => ""
            };
            if (!string.IsNullOrWhiteSpace(current)) return current;
            if (state != null && state.TryGetValue(id, out var node) && !string.IsNullOrWhiteSpace(node?.Version))
                return node.Version;
            return declared;
        }

        private static string NoteFor(string id) => id switch
        {
            BusRoles.A => "先换 A，由它接管 I 的更新",
            BusRoles.I => "由启动后的 A 更新",
            "shell" => "最后重启，会退出界面",
            "core" or "service" => "被引用的库，随依赖方一起重编",
            _ => "组件仓库，版本号由 I 一并维护"
        };

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
            var report = new IterationReport { Workspace = workspace, Version = UpdatedVersionText(plan) };
            string VersionOf(string role) => plan.FirstOrDefault(p => p.Role == role)?.Target ?? "";
            void Add(Iteration.Step step)
            {
                report.Steps.Add(step);
                progress?.Invoke(step.ToString());
            }

            bool needA = plan.FirstOrDefault(p => p.Role == BusRoles.A)?.Update == true;
            bool needI = plan.FirstOrDefault(p => p.Role == BusRoles.I)?.Update == true;
            bool needShell = plan.FirstOrDefault(p => p.Role == "shell")?.Update == true;

            // 构建前把工作树定格成一个 tree 对象：发布后把 refs/openloop/version/… 指回它，
            // 这样每个版本目录都能追到「究竟是哪份源码编出来的」（不需要提交）
            string sourceTree = OpenLoop.Core.Safety.Snapshots.Tree(SourceSync.SourceRoot);

            var build = await Iteration.BuildAsync(ct);
            Add(build);
            if (!build.Ok) return Finish(report, false);

            if (needShell)
            {
                var publish = await PublishShellAsync(VersionOf("shell"), ct);
                Add(publish);
                if (!publish.Ok) return Finish(report, false);
                report.ShellDir = publish.Detail;
                Iteration.PinSource(publish.Detail, "shell", VersionOf("shell"), sourceTree);
            }

            if (needA || needI)
            {
                var publish = await Iteration.PublishAsync(BusRoles.A, VersionOf(BusRoles.A), ct);
                Add(publish);
                if (!publish.Ok) return Finish(report, false);
                Iteration.PinSource(publish.Detail, BusRoles.A, VersionOf(BusRoles.A), sourceTree);

                var stage = await Iteration.StageAsync(BusRoles.A, publish.Detail, VersionOf(BusRoles.A), ct);
                Add(stage);
                if (!stage.Ok) return Finish(report, false);

                // 交接单：A 起来以后自己发布 / 登记 / 重启 I
                if (needI) WriteLoopUpdate(VersionOf(BusRoles.I), sourceTree);
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

            // 整轮成功才把本轮指纹提升为基准；失败保持旧基准，下一轮按同一个号重试
            VersionState.Promote();
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

        /// <summary>报告头里的版本：本次真正要更新的角色各自的目标版本（三个组件版本可以各不相同）。</summary>
        private static string UpdatedVersionText(List<RolePlan> plan)
        {
            var versions = plan.Where(p => p.Update && !string.IsNullOrWhiteSpace(p.Target))
                .Select(p => p.Target.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return versions.Count > 0 ? string.Join(" / ", versions) : plan.FirstOrDefault()?.Target ?? "";
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
            string project = Path.Combine("src", OperatingSystem.IsWindows() ? "OpenLoop.App" : "OpenLoop.Shell");
            string temp = Path.Combine(Path.GetTempPath(), "openloop-shell-" + Guid.NewGuid().ToString("N"));
            try
            {
                var (ok, output) = await Iteration.ExecAsync("dotnet",
                    ["publish", project, "-c", "Release", "-o", temp, "--nologo", "-v", "q"],
                    root, ct);
                if (!ok) return new("publish shell", false, Iteration.Tail(output, 12));

                string exe = Path.Combine(temp, OperatingSystem.IsWindows() ? "OpenLoop.exe" : "OpenLoop");
                if (!File.Exists(exe)) return new("publish shell", false, "没有产物 " + exe);

                string dir = Path.Combine(AppPaths.VersionsDir, "shell",
                    Iteration.VersionDirName(version, VersionManifest.ComputeFilesSha256(temp)));
                Directory.CreateDirectory(dir);
                foreach (string file in Directory.EnumerateFiles(temp, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(dir, Path.GetRelativePath(temp, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, true);
                }
                return new("publish shell", true, dir);
            }
            catch (OperationCanceledException)
            {
                throw;
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
            string from = Path.GetFullPath(shell.Dir).TrimEnd('\\', '/');

            if (string.Equals(from, Path.GetFullPath(install), StringComparison.OrdinalIgnoreCase))
                return;

            PruneShell(shell.Dir);

            string exe = Path.Combine(from, OperatingSystem.IsWindows() ? "OpenLoop.exe" : "OpenLoop");
            var start = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                WorkingDirectory = from
            };
            start.ArgumentList.Add(ShellUpdate.Flag);
            start.ArgumentList.Add(install);
            start.ArgumentList.Add("--apply-shell-pid");
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            if (!string.IsNullOrWhiteSpace(workspace))
            {
                start.ArgumentList.Add("--apply-shell-workspace");
                start.ArgumentList.Add(workspace.Trim());
            }
            // 新壳的 cwd 会变成安装目录：把源码根带给它，后续迭代仍对着同一份源码
            start.Environment["OPENLOOP_SOURCE"] = SourceSync.SourceRoot;
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
        public static (string Version, string SourceTree, bool Present) TakeLoopUpdate()
        {
            try
            {
                if (!File.Exists(RequestPath)) return ("", "", false);
                var request = JsonSerializer.Deserialize<LoopUpdateRequest>(File.ReadAllBytes(RequestPath), BusProtocol.Json);
                File.Delete(RequestPath);
                return (request?.Version ?? "", request?.SourceTree ?? "", true);
            }
            catch
            {
                return ("", "", false);
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

        private static void WriteLoopUpdate(string version, string sourceTree)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.RuntimeDir);
                // 上一轮的 result 必须清掉，否则 WaitLoopUpdateAsync 会立刻读到旧报告、以为 I 已经更完
                File.Delete(ResultPath);
                File.WriteAllBytes(RequestPath,
                    JsonSerializer.SerializeToUtf8Bytes(new LoopUpdateRequest { Version = version, SourceTree = sourceTree }, BusProtocol.Json));
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

        private sealed class LoopUpdateRequest
        {
            public string Version { get; set; }
            /// <summary>本次构建的源码树（refs/openloop/version/i/&lt;版本&gt; 要指向它）。</summary>
            public string SourceTree { get; set; }
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
