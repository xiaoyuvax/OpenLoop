using System.Text.Json;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Runtime;
using OpenLoop.Service.Shell;
using OpenLoop.Service.Source;

namespace OpenLoop.Service.Loop
{
    /// <summary>迭代计划：谁要升版、现版本 → 目标版本，附一段变更摘要。</summary>
    public sealed class IterationPlanResult
    {
        public string Workspace { get; set; }
        public List<RolePlan> Plan { get; set; } = [];
        public string Summary { get; set; } = "";
    }

    /// <summary>一轮迭代的投影（壳只轮询这个）。</summary>
    public sealed class IterationState
    {
        public string Workspace { get; set; }

        /// <summary>idle / running / done / error。</summary>
        public string State { get; set; } = "idle";

        public List<string> Steps { get; set; } = [];
        public string Version { get; set; } = "";

        /// <summary>壳的新发布目录；有值就说明最后还需要壳自己重启。</summary>
        public string ShellDir { get; set; }

        public bool Ok { get; set; }
        public bool RolledBack { get; set; }
        public string Error { get; set; }
        public IterationReport Report { get; set; }
    }

    /// <summary>
    /// 迭代由 I 主控：出计划、写版本号、按 state.json 判断污染、执行换版、失败回滚。
    /// 壳只轮询状态并在需要时重启自己（I 不能换自己，也不能替换运行中的壳）。
    /// 状态落盘是因为「A 换 I」那一步会把本进程杀掉，新 I 起来后要能接上结论。
    /// </summary>
    public sealed class IterationService
    {
        public static string StatePath => Path.Combine(AppPaths.RuntimeDir, "iteration.json");

        private readonly object _sync = new();
        private PersistedJob _job;
        private CancellationTokenSource _cts;
        private bool _resumed;

        public async Task<IterationPlanResult> PlanAsync(string workspace, CancellationToken ct = default)
        {
            lock (_sync)
            {
                if (_job?.State.State == "running")
                    throw new InvalidOperationException("已有迭代在跑");
            }

            var plan = await IterationRunner.PlanAsync(ct: ct);
            string summary = await IterationRunner.ChangeSummaryAsync(ct);
            var job = new PersistedJob
            {
                State = new IterationState { Workspace = workspace },
                Plan = plan,
                Before = new VersionStore().Read()
            };
            lock (_sync)
            {
                _job = job;
                Save(job);
            }

            return new IterationPlanResult { Workspace = workspace, Plan = plan, Summary = summary };
        }

        public IterationState Start(string workspace, bool includeShell)
        {
            PersistedJob job;
            lock (_sync)
            {
                if (_job?.State.State == "running") return Clone(_job.State);
                _job ??= Load();
                job = _job;
                if (job == null || job.Plan.Count == 0)
                    throw new InvalidOperationException("还没有计划：先算一次计划");

                job.IncludeShell = includeShell;
                job.StartedAt = DateTimeOffset.Now.ToString("O");
                job.State = new IterationState
                {
                    Workspace = workspace,
                    State = "running",
                    Steps =
                    [
                        "plan " + string.Join(" | ", job.Plan.Select(p =>
                            $"{p.Label} {p.Current}→{p.Target} {(p.Update ? "升版" : "不变")}"))
                    ]
                };
                _cts = new CancellationTokenSource();
                Save(job);
            }

            var token = _cts.Token;
            _ = Task.Run(() => RunAsync(job, token));
            return Clone(job.State);
        }

        public IterationState GetState(string workspace)
        {
            lock (_sync)
            {
                _job ??= Load();
                if (_job != null && _job.State.State == "running" && !_resumed)
                {
                    // 上一轮在「换 I」那步被杀了：本进程接手补结论
                    _resumed = true;
                    _ = Task.Run(ResumeAsync);
                }
                return Clone(_job?.State ?? new IterationState { Workspace = workspace });
            }
        }

        public bool Busy
        {
            get { lock (_sync) return _job?.State.State == "running"; }
        }

        public bool Stop()
        {
            lock (_sync)
            {
                _cts?.Cancel();
                return _job?.State.State == "running";
            }
        }

        private async Task RunAsync(PersistedJob job, CancellationToken ct)
        {
            void Progress(string line) => AppendStep(job, line);
            try
            {
                var plan = job.IncludeShell
                    ? job.Plan
                    : job.Plan.Select(p => p.Role == "shell" ? p with { Update = false } : p).ToList();

                var report = await IterationRunner.RunAsync(plan, job.State.Workspace, Progress, ct);
                if (!report.Ok)
                {
                    Progress("失败，回退到迭代前的版本…");
                    var rollback = await IterationRunner.RollbackAsync(
                        job.Before ?? new Dictionary<string, VersionEntry>(), Progress, ct);
                    report.Steps.AddRange(rollback);
                    report.RolledBack = rollback.Count > 0 && rollback.All(s => s.Ok);
                }

                lock (_sync)
                {
                    job.State.State = report.Ok ? "done" : "error";
                    job.State.Ok = report.Ok;
                    job.State.RolledBack = report.RolledBack;
                    job.State.Version = report.Version;
                    job.State.ShellDir = report.Ok ? report.ShellDir : null;
                    job.State.Report = report;
                    Save(job);
                }
            }
            catch (OperationCanceledException)
            {
                Finish(job, "error", "已取消");
            }
            catch (Exception ex)
            {
                Finish(job, "error", ex.Message);
            }
            finally
            {
                lock (_sync)
                {
                    _cts?.Dispose();
                    _cts = null;
                }
            }
        }

        /// <summary>
        /// 换 I 时本进程被杀：等 A 把「更新 I」的结果写出来，再补上这一轮的结论；
        /// 失败时按计划阶段记下的旧版本回退。
        /// </summary>
        private async Task ResumeAsync()
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);
            while (DateTime.UtcNow < deadline && !FreshResultExists(GetJob()))
                await Task.Delay(1000);

            PersistedJob job;
            lock (_sync) job = _job;
            if (job == null || job.State.State != "running") return;

            var (ok, detail, version) = ReadLoopResult();
            if (ok == true)
            {
                VersionState.Promote();
                AppendStep(job, "OK update i " + detail);
                // 换 I 的结果里只有 I 的版本；报告头用的是这一轮壳的目标版本
                string shellVersion = job.Plan.FirstOrDefault(p => p.Role == "shell")?.Target;
                lock (_sync) job.State.Version = string.IsNullOrWhiteSpace(shellVersion) ? version : shellVersion;
                Finish(job, "done", null);
                return;
            }

            AppendStep(job, (ok == false ? "FAIL update i " : "FAIL update i 没有结果 ") + detail);
            if (job.Before is { Count: > 0 })
            {
                var rollback = await IterationRunner.RollbackAsync(job.Before, line => AppendStep(job, line));
                AppendStep(job, "回退：" + (rollback.All(s => s.Ok) ? "完成" : "未全部成功"));
                lock (_sync) job.State.RolledBack = rollback.All(s => s.Ok);
            }
            Finish(job, "error", "换 I 后没拿到成功结果");
        }

        private PersistedJob GetJob()
        {
            lock (_sync) return _job;
        }

        /// <summary>结果文件必须是本轮开跑之后写的，否则会拿到上一次迭代的旧结论。</summary>
        private static bool FreshResultExists(PersistedJob job)
        {
            try
            {
                if (job == null || !File.Exists(IterationRunner.ResultPath)) return false;
                return !DateTimeOffset.TryParse(job.StartedAt, out var started)
                       || File.GetLastWriteTimeUtc(IterationRunner.ResultPath) >= started.UtcDateTime;
            }
            catch
            {
                return false;
            }
        }

        private static (bool? Ok, string Detail, string Version) ReadLoopResult()
        {
            try
            {
                if (!File.Exists(IterationRunner.ResultPath)) return (null, "", "");
                var result = JsonSerializer.Deserialize<LoopResult>(
                    File.ReadAllBytes(IterationRunner.ResultPath), BusProtocol.Json);
                return (result?.Ok, result?.Detail ?? "", result?.Version ?? "");
            }
            catch
            {
                return (null, "", "");
            }
        }

        private void AppendStep(PersistedJob job, string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (_sync)
            {
                job.State.Steps.Add(line);
                // 壳的发布目录是重启壳的唯一依据：换 I 时本进程会被杀，必须落盘
                const string shellStep = "OK publish shell ";
                if (line.StartsWith(shellStep, StringComparison.Ordinal))
                {
                    job.State.ShellDir = line[shellStep.Length..].Trim();
                    string target = job.Plan.FirstOrDefault(p => p.Role == "shell")?.Target;
                    if (!string.IsNullOrWhiteSpace(target)) job.State.Version = target;
                }
                Save(job);
            }
        }

        private void Finish(PersistedJob job, string state, string error, string version = null)
        {
            lock (_sync)
            {
                job.State.State = state;
                job.State.Ok = state == "done";
                job.State.Error = error;
                if (!string.IsNullOrWhiteSpace(version)) job.State.Version = version;
                job.State.Report ??= new IterationReport { Workspace = job.State.Workspace, Version = job.State.Version };
                job.State.Report.Ok = job.State.Ok;
                job.State.Report.Version = job.State.Version;
                job.State.ShellDir = state == "done" ? job.State.ShellDir : null;
                Save(job);
            }
        }

        private static IterationState Clone(IterationState state) => new()
        {
            Workspace = state.Workspace,
            State = state.State,
            Steps = [.. state.Steps],
            Version = state.Version,
            ShellDir = state.ShellDir,
            Ok = state.Ok,
            RolledBack = state.RolledBack,
            Error = state.Error,
            Report = state.Report
        };

        private static void Save(PersistedJob job)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.RuntimeDir);
                string temp = StatePath + ".tmp";
                File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(job, BusProtocol.Json));
                File.Move(temp, StatePath, true);
            }
            catch
            {
            }
        }

        private static PersistedJob Load()
        {
            try
            {
                if (!File.Exists(StatePath)) return null;
                return JsonSerializer.Deserialize<PersistedJob>(File.ReadAllBytes(StatePath), BusProtocol.Json);
            }
            catch
            {
                return null;
            }
        }

        private sealed class PersistedJob
        {
            public IterationState State { get; set; } = new();
            public List<RolePlan> Plan { get; set; } = [];
            public Dictionary<string, VersionEntry> Before { get; set; }
            public bool IncludeShell { get; set; } = true;
            public string StartedAt { get; set; }
        }

        private sealed class LoopResult
        {
            public bool Ok { get; set; }
            public string Detail { get; set; }
            public string Version { get; set; }
        }
    }
}
