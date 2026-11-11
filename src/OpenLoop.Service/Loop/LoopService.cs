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
    }

    /// <summary>
    /// Loop 侧的写操作（演进、回滚、登记规则）。账本的写入口收敛在这里，
    /// 界面只调用、不自己写——单写者由进程边界保证。
    /// </summary>
    public sealed class LoopService
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, Job> _jobs = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _autoStarting = new(StringComparer.OrdinalIgnoreCase);

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
            if (!settings.AutoEvolve || string.IsNullOrWhiteSpace(workspace)) return;

            var auto = AutoEvolve.Load(workspace);
            string day = DateTimeOffset.Now.ToString("yyyy-MM-dd");
            int today = auto.Day == day ? auto.Count : 0;
            state.AutoHint = $"今日 {today}/{settings.AutoEvolvePerDay} 轮 · 每攒 {settings.AutoEvolveRuns} 条新证据触发 · 冷却 {settings.AutoEvolveCooldownMinutes} 分钟";
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

                job = new Job { State = new LoopEvolveState { Workspace = workspace, State = "running", Phase = "preparing" } };
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
                SkillCatalog.Load(ledger.Workspace, AppSettings.GlobalSkillsPath), out var problems);

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
                int runs = ledger.Runs().Count;
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
                        state, runs, now, out string reason))
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
                    Runs = runs
                });
                AppLog.Info($"自动演进启动：{workspace}（{runs} 条运行记录）");
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
                Update(job, s =>
                {
                    s.TaskCount = tasks.Tasks.Count;
                    s.Phase = "proposing";
                });

                var loopAgent = new LoopAgent(Reasoner(settings, model, effort, workspace));
                var proposal = await loopAgent.ProposeAsync(loopAgent.BuildPacket(ledger), baseline, features, ct);
                if (proposal.Error != null)
                {
                    string problems = proposal.Problems.Count == 0 ? "" : "：" + string.Join("；", proposal.Problems);
                    Finish(job, s =>
                    {
                        s.State = "error";
                        s.Error = proposal.Error + problems;
                    });
                    return;
                }

                Update(job, s => s.Phase = "evaluating");
                var runner = TaskRunner(settings, workspace, ledger, features, model, effort);
                var report = await new Gate(ledger, tasks, features, runner).EvaluateAsync(baseline, proposal.Candidate, ct);

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

        private static IReasoner Reasoner(AppSettings settings, string model, string effort, string workspace)
        {
            var command = settings.LoopCommand;
            if (command is { Count: > 0 }) return new ProcessReasoner(command[0], command.Skip(1));
            return Reasoners.Create(settings, model, effort, workspace);
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
                AppSettings.GlobalSkillsPath, ModelCatalog.ContextLimit(model, settings));
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
                BaselinePassed = state.BaselinePassed,
                CandidatePassed = state.CandidatePassed,
                CandidateVersion = state.CandidateVersion,
                Adopted = state.Adopted,
                Detail = state.Detail,
                Error = state.Error
            };
        }

        private static string Normalize(string workspace) => (workspace ?? "").Trim().ToLowerInvariant();

        private sealed class Job
        {
            public LoopEvolveState State { get; init; }
            public CancellationTokenSource Cancel { get; } = new();
            public object Sync => State;
        }
    }
}
