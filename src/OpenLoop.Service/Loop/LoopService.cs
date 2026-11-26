using OpenLoop.Core.Loop;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Skills;
using OpenLoop.Core.Store;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Config;
using OpenLoop.Service.Logging;
using OpenLoop.Service.Models;
using OpenLoop.Service.Runtime;
using OpenLoop.Service.Storage;
using OpenLoop.Service.Worker;

namespace OpenLoop.Service.Loop
{
    /// <summary>A 每次运行前向 I 要的账本投影：激活集 + 特征表 + 当前启用的技能名。</summary>
    public sealed class ActivationPayload
    {
        public ActivationRecord Activation { get; set; }
        public Dictionary<string, FeatureRecord> Features { get; set; } = new(StringComparer.Ordinal);
        public List<string> Skills { get; set; } = [];
        public List<string> Problems { get; set; } = [];
    }

    /// <summary>一次演进的状态投影（界面轮询这个）。</summary>
    public sealed class LoopEvolveState
    {
        public string Workspace { get; set; }

        /// <summary>idle / running / done / error / stopped。</summary>
        public string State { get; set; } = "idle";

        /// <summary>preparing / proposing / evaluating / finished。</summary>
        public string Phase { get; set; } = "";

        public int TaskCount { get; set; }

        /// <summary>本轮开始时刻（ISO）；界面上没有别的活动信号时靠它算已用时长。</summary>
        public string StartedAt { get; set; }

        /// <summary>评测进度：已完成运行数 / 总运行数（题目数 × 2），以及正在跑的题目。</summary>
        public int ProgressDone { get; set; }
        public int ProgressTotal { get; set; }
        public string ProgressTask { get; set; }

        public int BaselinePassed { get; set; }
        public int CandidatePassed { get; set; }
        public int? CandidateVersion { get; set; }
        public bool Adopted { get; set; }

        /// <summary>未采纳的原因 / 失败原因。</summary>
        public string Detail { get; set; }
        public string Error { get; set; }

        /// <summary>自动演进开关与当前配额/冷却的一句话说明（界面直接显示）。</summary>
        public bool AutoEnabled { get; set; }
        public string AutoHint { get; set; }

        /// <summary>自动迭代开关；与自动演进并列的另一个开关。</summary>
        public bool AutoIterateEnabled { get; set; }
    }

    /// <summary>
    /// Loop 侧的写操作（演进、回滚、登记规则）。账本的写入口收敛在这里，
    /// 界面只调用、不自己写——单写者由进程边界保证。
    /// </summary>
    public sealed partial class LoopService
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, Job> _jobs = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _autoStarting = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>换版服务（由 I 在启动时挂上）：每轮证据入账后顺带刷新「源码攒了多少提交」。</summary>
        public IterationService Iteration { get; set; }

        public LoopEvolveState GetState(string workspace)
        {
            LoopEvolveState state;
            lock (_lock)
                state = _jobs.TryGetValue(Normalize(workspace), out var job)
                    ? Clone(job.State)
                    : new LoopEvolveState { Workspace = workspace };
            DescribeAuto(workspace, state);
            return state;
        }

        /// <summary>给界面的一句话：开没开、今天跑了几轮、阈值多少。</summary>
        private static void DescribeAuto(string workspace, LoopEvolveState state)
        {
            var settings = AppSettings.Load();
            state.AutoEnabled = settings.AutoEvolve;
            state.AutoIterateEnabled = settings.AutoIterate;
            if (!settings.AutoEvolve || string.IsNullOrWhiteSpace(workspace)) return;

            var auto = AutoEvolve.Load(workspace);
            string day = DateTimeOffset.Now.ToString("yyyy-MM-dd");
            int today = auto.Day == day ? auto.Count : 0;
            state.AutoHint = $"今日 {today}/{settings.AutoEvolvePerDay} 轮 · 每攒 {settings.AutoEvolveRuns} 条待分诊观察触发 · 冷却 {settings.AutoEvolveCooldownMinutes} 分钟";
        }

        /// <summary>有演进正在跑（/bus/restart 忙时拒绝）。</summary>
        public bool IsBusy
        {
            get
            {
                lock (_lock) return _jobs.Values.Any(j => j.State.State == "running");
            }
        }

        /// <summary>同一个工作区同时只跑一个演进；已在跑就直接返回当前状态。</summary>
        public LoopEvolveState StartEvolve(string workspace, string model, string effort)
        {
            string key = Normalize(workspace);
            Job job;
            lock (_lock)
            {
                if (_jobs.TryGetValue(key, out var existing) && existing.State.State == "running")
                    return Clone(existing.State);

                job = new Job
                {
                    State = new LoopEvolveState
                    {
                        Workspace = workspace,
                        State = "running",
                        Phase = "preparing",
                        StartedAt = DateTimeOffset.Now.ToString("O")
                    }
                };
                _jobs[key] = job;
            }

            _ = Task.Run(() => RunAsync(job, model ?? "", effort ?? ""));
            return Clone(job.State);
        }

        public bool StopEvolve(string workspace)
        {
            lock (_lock)
            {
                if (!_jobs.TryGetValue(Normalize(workspace), out var job) || job.State.State != "running") return false;
                job.Cancel.Cancel();
                return true;
            }
        }

        /// <summary>退回到上一版（或指定版本），落成新版本，旧版本文件不动。</summary>
        public ActivationRecord Rollback(string workspace, int? target = null)
        {
            var ledger = Ledger.Open(workspace);
            var current = ledger.Current ?? throw new InvalidOperationException("账本还没有激活集");
            int version = target ?? current.Version - 1;
            if (version < 1 || version >= current.Version)
                throw new InvalidOperationException("没有更早的版本");
            return ledger.Rollback(version, "user", $"手动回滚到 v{version}");
        }

        public FeatureRecord AddRule(string workspace, string name, string text)
        {
            var ledger = Ledger.Open(workspace);
            ledger.EnsureInitialized();
            return ledger.AddRule(name, text);
        }

        /// <summary>
        /// 账本初始化 + 技能发现即启用，然后把 A 运行要用的那份投影给出去。
        /// A 不再自己碰账本：读这一个端点、写只写 spool。
        /// </summary>
        public ActivationPayload LoadActivation(string workspace)
        {
            var ledger = Ledger.Open(workspace);
            ledger.EnsureInitialized();
            var skills = SkillCatalog.Sync(ledger,
                SkillCatalog.Load(ledger.Workspace, AppSettings.GlobalSkillsPath), out var problems,
                SkillStore.LoadDisabled(AppSettings.GlobalSkillsStatePath));

            return new ActivationPayload
            {
                Activation = ledger.Current,
                Features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal),
                Skills = skills.Select(s => s.Name).ToList(),
                Problems = problems.ToList()
            };
        }

        /// <summary>启用/停用技能：写一个新的激活版本。</summary>
        public void SetSkill(string workspace, string name, bool enabled)
        {
            var ledger = Ledger.Open(workspace);
            ledger.EnsureInitialized();
            SkillCatalog.SetEnabled(ledger,
                SkillCatalog.Load(ledger.Workspace, AppSettings.GlobalSkillsPath), name, enabled);
        }

        /// <summary>
        /// 把 A 投进 <c>.openloop/spool/</c> 的运行记录并进账本，按 run id 幂等。
        /// 调用点：A 每轮跑完、演进开始前（补上 I 停机期间积压的）。
        /// </summary>
        public int DrainSpool(string workspace)
        {
            var ledger = Ledger.Open(workspace);
            ledger.EnsureInitialized();

            var known = ledger.Runs().Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            int drained = 0;
            foreach (var (path, record) in RunSpool.Pending(ledger.Workspace))
            {
                if (known.Add(record.Id))
                {
                    try
                    {
                        ledger.AppendRun(record);
                        drained++;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("账本写入失败", ex);
                        continue;
                    }
                }
                RunSpool.Remove(path);
            }
            if (drained > 0) TryAutoEvolve(ledger.Workspace);
            // 源码可能在这期间攒了新提交：顺手更新自动迭代的判据（壳轮询它）
            if (drained > 0 && Iteration != null) _ = Task.Run(() => Iteration.RefreshAutoIterateAsync());
            return drained;
        }

        /// <summary>
        /// 事件驱动入口：新运行记录进来时看一眼要不要自己开一轮。
        /// 开关/门槛/冷却/每日上限都在 settings 里，默认关。
        /// </summary>
        private void TryAutoEvolve(string workspace)
        {
            var settings = AppSettings.Load();
            if (!settings.AutoEvolve) return;

            string key = Normalize(workspace);
            lock (_lock)
            {
                if (_jobs.TryGetValue(key, out var job) && job.State.State == "running") return;
                if (!_autoStarting.Add(key)) return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(settings.Model))
                {
                    AppLog.Info("自动演进跳过：没有默认模型");
                    return;
                }

                var ledger = Ledger.Open(workspace);
                int evidence = FindingTriage.OpenFindings(workspace).Count;
                var state = AutoEvolve.Load(workspace);
                var now = DateTimeOffset.Now;

                if (TaskSet.Load(ledger.EvalDir).Tasks.Count == 0)
                {
                    AppLog.Info("自动演进跳过：没有考核题");
                    return;
                }

                if (!AutoEvolve.ShouldStart(
                        settings.AutoEvolve, settings.AutoEvolveRuns,
                        settings.AutoEvolveCooldownMinutes, settings.AutoEvolvePerDay,
                        state, evidence, now, out string reason))
                {
                    AppLog.Info($"自动演进暂缓：{reason}");
                    return;
                }

                string day = now.ToString("yyyy-MM-dd");
                AutoEvolve.Save(workspace, new AutoEvolveState
                {
                    Day = day,
                    Count = (state.Day == day ? state.Count : 0) + 1,
                    At = now.ToString("O"),
                    Evidence = evidence
                });
                AppLog.Info($"自动演进启动：{workspace}（{evidence} 条待分诊观察）");
                StartEvolve(workspace, settings.Model, "");
            }
            catch (Exception ex)
            {
                AppLog.Error("自动演进触发失败", ex);
            }
            finally
            {
                lock (_lock) _autoStarting.Remove(key);
            }
        }

        private async Task RunAsync(Job job, string model, string effort)
        {
            var ct = job.Cancel.Token;
            string workspace = job.State.Workspace;
            try
            {
                var settings = AppSettings.Load();
                DrainSpool(workspace);
                var ledger = Ledger.Open(workspace);
                ledger.EnsureInitialized();
                SkillCatalog.RegisterFeatures(ledger, SkillCatalog.Load(workspace, AppSettings.GlobalSkillsPath));

                var tasks = TaskSet.Load(ledger.EvalDir);
                if (tasks.Tasks.Count == 0)
                    throw new InvalidOperationException("没有考核题：先放进 " + Path.Combine(ledger.EvalDir, "tasks"));

                var features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
                var baseline = ledger.Current ?? throw new InvalidOperationException("账本还没有激活集");

                // 演进是「非即时集中分析」：把还没处置的 findings 分诊成本轮依据。
                // 幂等：本轮失败/未采纳时它们仍是 open，下一轮再来。
                string batch = "B-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var openFindings = FindingTriage.OpenFindings(workspace);
                var harnessFindings = openFindings
                    .Where(f => string.Equals(f.Target, FindingTriage.Harness, StringComparison.Ordinal))
                    .ToList();
                // 源码类与考核无关：先派单。候选失败也不该把源码待办一起卡住。
                DispatchSourceFindings(workspace, batch, openFindings);

                Update(job, s =>
                {
                    s.TaskCount = tasks.Tasks.Count;
                    s.Phase = "proposing";
                });

                var loopAgent = new LoopAgent(Reasoner(settings, model, effort, workspace, ct));
                var proposal = await loopAgent.ProposeAsync(
                    loopAgent.BuildPacket(ledger, findings: harnessFindings), baseline, features, ct);

                // A/B 评测模式：Prompt / AGENTS.md 的改动暂存在候选包里，这里叠到候选激活集上一起考核。
                var pending = PromptCatalog.PendingKinds(workspace);
                ActivationRecord candidate;
                if (proposal.Error != null)
                {
                    if (pending.Count == 0)
                    {
                        string problems = proposal.Problems.Count == 0 ? "" : "：" + string.Join("；", proposal.Problems);
                        // 模型原始输出不落盘，失败后只能靠日志复盘它到底提了什么
                        AppLog.Warn("演进候选被拒：" + proposal.Error + problems
                                    + "；原始输出：" + TrimLog(proposal.Raw));
                        Finish(job, s =>
                        {
                            s.State = "error";
                            s.Error = proposal.Error + problems;
                        });
                        return;
                    }
                    candidate = new ActivationRecord
                    {
                        Version = baseline.Version + 1,
                        At = DateTimeOffset.UtcNow.ToString("O"),
                        By = "user",
                        Note = "候选提示词包",
                        Features = baseline.Features.ToList()
                    };
                }
                else
                {
                    candidate = proposal.Candidate;
                }
                candidate = PromptCatalog.OverlayPending(ledger, workspace, candidate, features);

                Update(job, s =>
                {
                    s.Phase = "evaluating";
                    s.ProgressDone = 0;
                    s.ProgressTotal = tasks.Tasks.Count * 2;
                });
                var runner = TaskRunner(settings, workspace, ledger, features, model, effort);
                var report = await new Gate(ledger, tasks, features, runner).EvaluateAsync(
                    baseline, candidate, ct, harnessFindings.Select(f => f.Id).ToList(),
                    (done, total, task) => Update(job, s =>
                    {
                        s.ProgressDone = done;
                        s.ProgressTotal = total;
                        s.ProgressTask = task;
                    }));
                if (report.Adopted && pending.Count > 0)
                    PromptCatalog.CommitPending(workspace);
                TriageAfterGate(workspace, batch, openFindings, report);

                Finish(job, s =>
                {
                    s.State = "done";
                    s.Adopted = report.Adopted;
                    s.CandidatePassed = report.CandidatePassed;
                    s.BaselinePassed = report.BaselinePassed;
                    s.CandidateVersion = report.CandidateVersion;
                    s.TaskCount = report.Tasks.Count;
                    s.Detail = report.Adopted ? null : report.Reason;
                });

                // 演进只改激活集：二进制由自动迭代按源码提交另外换，两者不再互相触发
            }
            catch (OperationCanceledException)
            {
                Finish(job, s =>
                {
                    s.State = "stopped";
                    s.Detail = "已停止";
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("演进失败", ex);
                Finish(job, s =>
                {
                    s.State = "error";
                    s.Error = ex.Message;
                });
            }
        }

        /// <summary>
        /// 源码类 findings 与考核无关：写成派单让下一次 Loop 会话处理，并标成 dispatched；
        /// 派单只写一次（OpenFindings 不会再给已分诊的）。
        /// </summary>
        private static void DispatchSourceFindings(
            string workspace, string batch, IReadOnlyList<FindingRecord> findings)
        {
            var source = findings
                .Where(f => string.Equals(f.Target, FindingTriage.Source, StringComparison.Ordinal))
                .ToList();
            if (source.Count == 0) return;
            if (FindingTriage.WriteDispatch(workspace, batch, source) == null) return;
            foreach (var finding in source)
                FindingTriage.Append(workspace, finding.Id, FindingTriage.Dispatched, batch, "已写源码待办");
        }

        /// <summary>
        /// 门禁跑完后的分派：harness 类只有被采纳才算处置完（未采纳留在 open，下一批再试），
        /// unknown 不动。
        /// </summary>
        private static void TriageAfterGate(
            string workspace, string batch, IReadOnlyList<FindingRecord> findings, GateReport report)
        {
            if (!report.Adopted) return;
            foreach (var finding in findings)
            {
                if (string.Equals(finding.Target, FindingTriage.Harness, StringComparison.Ordinal))
                {
                    FindingTriage.Append(workspace, finding.Id, FindingTriage.Done, batch,
                        $"随 v{report.CandidateVersion} 采纳");
                }
            }
        }

        private static IReasoner Reasoner(AppSettings settings, string model, string effort, string workspace, CancellationToken ct)
        {
            var command = settings.LoopCommand;
            if (command is { Count: > 0 }) return new ProcessReasoner(command[0], command.Skip(1));
            // 宿主接线（与 Worker/Loop/旁路共用 ReasonerWiring）：演进无界面，通报落日志、停止跟随任务取消
            return ReasonerWiring.Attach(Reasoners.Create(settings, model, effort, workspace),
                new ReasonerWiring.Host
                {
                    Notice = text => { if (!string.IsNullOrEmpty(text)) OpenLoop.Service.Logging.AppLog.Info("演进：" + text); },
                    ShouldStop = () => ct.IsCancellationRequested
                });
        }

        /// <summary>
        /// 考核要考被演进的那个二进制：A 与 I 版本不一致时把题目交给
        /// <c>OpenLoop.A.exe --eval-run</c> 跑，版本一致就进程内跑（快，且不依赖 A 在跑）。
        /// </summary>
        private static ITaskRunner TaskRunner(AppSettings settings, string workspace, Ledger ledger,
            IReadOnlyDictionary<string, FeatureRecord> features, string model, string effort)
        {
            string exe = DivergedWorkerExe();
            if (exe != null)
                return new ProcessTaskRunner(exe, workspace, ledger.EvalDir, features, model, effort);

            return new AgentTaskRunner(
                _ => Reasoners.Create(settings, model, effort, workspace),
                features, workspace, ledger.EvalDir, AppSettings.GlobalAgentsPath, settings.UseAgentsMd,
                AppSettings.GlobalSkillsPath, ModelCatalog.ContextLimit(model, settings),
                extraTools: null,
                webFetch: settings.WebFetchMode,
                puppetTools: max => OpenLoop.Service.Web.PuppetTools.For(max));
        }

        private static string DivergedWorkerExe()
        {
            var worker = RuntimeInfo.TryLoad(AppPaths.RuntimeFile(BusRoles.A));
            var self = RuntimeInfo.TryLoad(AppPaths.RuntimeFile(BusRoles.I));
            if (worker is not { IsAlive: true } || string.IsNullOrWhiteSpace(worker.Dir)) return null;
            if (string.Equals(worker.Version, self?.Version, StringComparison.OrdinalIgnoreCase)) return null;

            string exe = Path.Combine(worker.Dir, OperatingSystem.IsWindows() ? "OpenLoop.A.exe" : "OpenLoop.A");
            return File.Exists(exe) ? exe : null;
        }

        private static void Update(Job job, Action<LoopEvolveState> change)
        {
            lock (job.Sync) change(job.State);
        }

        private static void Finish(Job job, Action<LoopEvolveState> change)
        {
            lock (job.Sync)
            {
                change(job.State);
                job.State.Phase = "finished";
            }
        }

        private static LoopEvolveState Clone(LoopEvolveState state)
        {
            lock (state) return new LoopEvolveState
            {
                Workspace = state.Workspace,
                State = state.State,
                Phase = state.Phase,
                TaskCount = state.TaskCount,
                StartedAt = state.StartedAt,
                ProgressDone = state.ProgressDone,
                ProgressTotal = state.ProgressTotal,
                ProgressTask = state.ProgressTask,
                BaselinePassed = state.BaselinePassed,
                CandidatePassed = state.CandidatePassed,
                CandidateVersion = state.CandidateVersion,
                Adopted = state.Adopted,
                Detail = state.Detail,
                Error = state.Error
            };
        }

        private static string Normalize(string workspace) => (workspace ?? "").Trim().ToLowerInvariant();

        private static string TrimLog(string text)
            => string.IsNullOrEmpty(text) ? "（空）" : text.Length <= 2000 ? text : text[..2000] + "…";

        private sealed class Job
        {
            public LoopEvolveState State { get; init; }
            public CancellationTokenSource Cancel { get; } = new();
            public object Sync => State;
        }
    }
}
