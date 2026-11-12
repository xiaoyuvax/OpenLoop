using OpenLoop.Core.Agent;
using OpenLoop.Core.Messages;
using OpenLoop.Core.OpenCode;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Safety;
using OpenLoop.Core.Skills;
using OpenLoop.Core.Store;
using OpenLoop.Core.Tools;
using OpenLoop.Core.Trace;
using OpenLoop.Service.Config;
using OpenLoop.Service.Logging;
using OpenLoop.Service.Models;
using OpenLoop.Service.Plugins;
using OpenLoop.Service.Storage;

namespace OpenLoop.Service.Worker
{
    /// <summary>
    /// 一次 Worker 运行所需的全部输入。界面相关的格式化通过回调交给调用方，
    /// 服务本身不引用 WinForms / ChatFlow。
    /// </summary>
    public sealed class WorkerRunRequest
    {
        public AppSettings Settings { get; set; }
        public string Workspace { get; set; }
        public string Model { get; set; }
        public string Effort { get; set; }
        public string Tag { get; set; } = "task";
        public string Text { get; set; }

        /// <summary>不含 system；服务会在开头插入系统提示词。</summary>
        public List<LlmMessage> History { get; set; } = [];

        /// <summary>越界/风险操作的审批；完全许可时服务忽略它。</summary>
        public ApprovalHandler Approve { get; set; }

        /// <summary>探索护栏触发时询问是否继续。</summary>
        public Func<string, bool> ConfirmContinue { get; set; }

        /// <summary>软停止信号：true 时当前轮在下一个 step 前收尾（进行中的工具不打断）。</summary>
        public Func<bool> ShouldStop { get; set; }

        /// <summary>设置完成、开始调用模型前触发一次；拿 undo/checkpoint/trace 路径。</summary>
        public Action<WorkerRunContext> Started { get; set; }

        public Action<string> Delta { get; set; }
        public Action<string> Reasoning { get; set; }
        public Action ResetStream { get; set; }
        public Action<string> ToolStarted { get; set; }

        /// <summary>即时状态通报（例如「等待审批：shell …」），null = 清除。界面拿它填状态行，避免一片空白。</summary>
        public Action<string> Notice { get; set; }

        /// <summary>每次请求模型前的上下文 token 估算（本轮 prompt 大小），界面据此实时刷新占比。</summary>
        public Action<int> ContextTokens { get; set; }
        public Action<AgentStepRecord> Step { get; set; }
        public Action<OpenAiCompatibleReasoner.ReasonerProgress> ModelProgress { get; set; }

        /// <summary>空回复兜底文案由调用方决定（本地化留在界面层）。</summary>
        public Func<string, string, string> FormatAnswer { get; set; }

        /// <summary>I 给的账本投影；为空表示拿不到，本轮按全部关闭运行。</summary>
        public Loop.ActivationPayload Activation { get; set; }
    }

    public sealed class WorkerRunContext
    {
        public FileUndoLog Undo { get; init; }
        public string Checkpoint { get; init; }
        public string TraceDirectory { get; init; }
        public int ActivationVersion { get; init; }
        public int FeatureCount { get; init; }
        public int RuleCount { get; init; }
    }

    public sealed class WorkerRunResult
    {
        public string Outcome { get; set; } = "error";
        public int Steps { get; set; }
        public decimal? Cost { get; set; }
        public string FinalAnswer { get; set; } = "";
        public IReadOnlyList<LlmMessage> Transcript { get; set; }
        public bool Compressed { get; set; }
        public int? ContextTokens { get; set; }
        public string TraceDirectory { get; set; }
        public string ErrorMessage { get; set; }

        /// <summary>模型结束原因（content_filter 等），界面据此解释空回复。</summary>
        public string FinishReason { get; set; }
        public bool BudgetExhausted { get; set; }
        public bool StoppedByUser { get; set; }
        public bool StoppedByGuard { get; set; }
    }

    /// <summary>
    /// Worker 的运行管线：账本/策略 → 工具与审批 → trace → AgentLoop → 账本证据。
    /// 流式与工具行的显示由调用方通过回调决定。
    /// </summary>
    public sealed class WorkerSessionService
    {
        /// <summary>
        /// 设置阶段失败（账本、trace、配置）直接抛异常，此时还没有建立 turn；
        /// 运行阶段的失败进 <see cref="WorkerRunResult"/>，保证调用方仍能收尾这一轮。
        /// </summary>
        public async Task<WorkerRunResult> RunAsync(WorkerRunRequest request, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var settings = request.Settings ?? new AppSettings();
            string workspace = request.Workspace?.Trim() ?? "";
            bool fullAccess = settings.FullAccess;
            var approve = fullAccess
                ? (ApprovalHandler)(_ => ApprovalDecision.AllowOnce)
                : request.Approve ?? (_ => ApprovalDecision.Deny);

            var undo = new FileUndoLog();
            var scope = new WorkspaceScope(workspace, fullAccess ? [] : null, fullAccess) { Undo = undo };
            var skillCatalog = SkillCatalog.Load(workspace, AppSettings.GlobalSkillsPath);
            using var plugins = PluginCatalog.Load(workspace);
            string checkpoint = Snapshots.Create(workspace, "worker");
            if (checkpoint != null) AppLog.Info("snapshot " + checkpoint);

            var reasoner = Reasoners.Create(settings, request.Model, request.Effort, workspace, approve);
            if (request.ModelProgress != null)
                Reasoners.AttachProgress(reasoner, progress => request.ModelProgress(progress));

            var payload = request.Activation;
            var activation = payload?.Activation;
            var features = payload?.Features ?? new Dictionary<string, FeatureRecord>();
            var policy = FeatureCatalog.PolicyFrom(activation, features);
            var rules = FeatureCatalog.RulesFrom(activation, features);
            IReadOnlyList<SkillInfo> skills = [];
            IReadOnlyList<string> skillProblems = [];
            if (activation != null)
                skills = SkillCatalog.Active(activation, features, skillCatalog, out skillProblems);
            else
                AppLog.Warn("拿不到 I 的激活集，本轮按全部关闭运行：" + workspace);

            if (skillProblems.Count > 0)
                AppLog.Warn("技能不可用：" + string.Join("；", skillProblems));

            scope.Approve = approve;
            var tools = ToolRegistry.Workspace(
                scope, approve, skills, fullAccess,
                OpenCodeTools.For(workspace, approve, settings.OpenCodeAskPermissions)
                    .Concat(DshTools.For(settings, workspace, approve))
                    .Concat(plugins.Tools())
                    .Concat(OpenLoop.Service.Web.PuppetTools.For()));

            var trace = TraceWriter.Start(workspace, request.Tag, request.Text, request.Model);
            // 上一轮疑似超时/被杀留下的临时会话：顺手清掉，别一直挂在 OpenCode 上占模型
            if (reasoner is OpenCodeReasoner openCode)
            {
                _ = openCode.SweepStaleSessions();
                // 「立即」是一次调用内部的工具边界：整轮只在 RunAsync 外查停止太迟
                openCode.ShouldStop = request.ShouldStop;
            }
            // 等待审批之类的即时状态：写进界面状态行，期间不要是一片空白
            if (reasoner is INoticeObserver noticeObserver)
                noticeObserver.OnNotice = text => request.Notice?.Invoke(text);
            // 原生工具调用发生在推理通道内部：边发生边写 trace（停止/超时也留证据），同时进步骤流。
            if (reasoner is INativeToolObserver observer)
                observer.OnNativeTool = tool =>
                {
                    trace.NativeTool(tool);
                    request.Step?.Invoke(new AgentStepRecord
                    {
                        Tool = "native:" + tool.Tool,
                        ArgsSummary = Clip(tool.Input, 120),
                        ArgsFull = tool.Input,
                        Ok = tool.Ok,
                        Summary = Clip(string.IsNullOrEmpty(tool.Output) ? tool.Status : tool.Output, 160),
                        FullResult = "native-result: " + tool.Output,
                        ResultFull = tool.Output,
                        DurationMs = tool.DurationMs
                    });
                };
            var ctx = new WorkerRunContext
            {
                Undo = undo,
                Checkpoint = checkpoint,
                TraceDirectory = trace.DirectoryPath,
                ActivationVersion = activation?.Version ?? 0,
                FeatureCount = activation?.Features.Count ?? 0,
                RuleCount = rules.Count
            };

            var loop = new AgentLoop(tools, reasoner, policy,
                onStep: step =>
                {
                    request.Step?.Invoke(step);
                    trace.Step(step);
                },
                onToolStart: (name, _) => request.ToolStarted?.Invoke(name),
                confirmContinue: request.ConfirmContinue,
                skills: skills);

            string systemPrompt = loop.BuildSystemPrompt(
                null, rules,
                settings.UseAgentsMd
                    ? AgentsInstructions.Load(workspace, AppSettings.GlobalAgentsPath)
                    : null,
                workspace);
            request.History.Insert(0, new LlmMessage("system", systemPrompt));
            trace.SystemPrompt(systemPrompt);
            trace.SkillSet(skills, skillProblems);

            request.Started?.Invoke(ctx);
            trace.Message("user", $"[{request.Tag}] {request.Text}");

            var result = new WorkerRunResult { TraceDirectory = trace.DirectoryPath };
            string outcome = "error";
            IReadOnlyList<LlmMessage> lastRequest = null;
            try
            {
                int contextLimit = ModelCatalog.ContextLimit(request.Model, request.Settings);
                var run = await Task.Run(
                    () => loop.RunAsync(request.History, request.Text, ct, contextLimit,
                        () => ct.IsCancellationRequested || (request.ShouldStop?.Invoke() ?? false),
                        request.Delta, request.Reasoning, request.ResetStream,
                        (messages, tokens) =>
                        {
                            lastRequest = messages;
                            request.ContextTokens?.Invoke(tokens);
                            trace.Request(messages, tokens);
                        }),
                    ct);

                string answer = request.FormatAnswer?.Invoke(run.FinalAnswer, run.FinishReason)
                    ?? (run.FinalAnswer ?? "").Trim();

                outcome = run.BudgetExhausted ? "budget"
                    : run.StoppedByUser || run.StoppedByGuard ? "stopped"
                    : "ok";

                trace.Message("assistant", answer);
                trace.Finish(outcome, answer, run.ChatCost, run.Guards);

                result.Outcome = outcome;
                result.Steps = run.Steps.Count;
                result.Cost = run.ChatCost;
                result.FinishReason = run.FinishReason;
                result.FinalAnswer = answer;
                result.Transcript = run.Transcript;
                result.Compressed = run.Compressed;
                result.ContextTokens = run.ContextTokens;
                result.BudgetExhausted = run.BudgetExhausted;
                result.StoppedByUser = run.StoppedByUser;
                result.StoppedByGuard = run.StoppedByGuard;
            }
            catch (OperationCanceledException)
            {
                result.Outcome = "stopped";
                result.FinalAnswer = ServerNotice.Stopped;
                result.StoppedByUser = true;
                trace.Message("assistant", result.FinalAnswer);
                trace.Finish("stopped", result.FinalAnswer);
            }
            catch (Exception ex)
            {
                AppLog.Error("任务执行失败", ex);
                QuotaGate.NoteFor(request.Model, ex.Message);
                result.Outcome = "error";
                result.ErrorMessage = ex.Message;
                result.FinalAnswer = ex.Message;
                trace.Finish("error", ex.Message);
            }
            finally
            {
                // 失败/中断时 run.Transcript 拿不到：退回最后一次真正发出去的消息，
                // 否则这一轮的上下文（含前面所有历史）全都丢了，下一轮只能当新对话。
                if (result.Transcript == null && lastRequest != null)
                    result.Transcript = lastRequest.Select(m => new LlmMessage(m.Role, m.Content)).ToList();

                try
                {
                    RunSpool.Write(workspace, new RunRecord
                    {
                        Id = Ledger.NewId("R"),
                        ActivationVersion = activation?.Version ?? 0,
                        Task = request.Text.Length <= 200 ? request.Text : request.Text[..200],
                        Outcome = outcome,
                        Steps = result.Steps,
                        Cost = result.Cost,
                        TraceDir = trace.DirectoryPath,
                        At = DateTimeOffset.UtcNow.ToString("O")
                    });
                }
                catch (Exception ex)
                {
                    AppLog.Error("运行证据投递失败", ex);
                    result.ErrorMessage ??= "运行证据投递失败：" + ex.Message;
                }

                trace.Dispose();
                // 这一轮用完就清场：本进程残留的临时会话（收尾删除失败的）当场删掉
                if (reasoner is OpenCodeReasoner done) _ = done.SweepStaleSessions();
            }

            return result;
        }

        /// <summary>步骤流一行文本，超长截断。</summary>
        private static string Clip(string text, int max)
            => string.IsNullOrEmpty(text) || text.Length <= max ? text ?? "" : text[..max] + "…";
    }
}
