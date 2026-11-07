using OpenLoop.Core.Agent;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Safety;
using OpenLoop.Core.Tools;

namespace OpenLoop.Core.Acp
{
    /// <summary>
    /// 把一件任务交给 dsh 的 agent 执行（ACP 新会话）。与 <c>OpenCodeTask</c> 对称：
    /// 工具、会话、上下文都在对方那边，OpenLoop 只在每次要权限时替用户拍板。
    /// </summary>
    public sealed class DshTaskTool : ITool
    {
        private readonly Func<string, string, Func<AcpPermissionRequest, bool>, CancellationToken, Task<CompletionResult>> _run;
        private readonly string _workspace;
        private readonly ApprovalHandler _approve;
        private readonly Action<string> _onFailure;

        public DshTaskTool(
            Func<string, string, Func<AcpPermissionRequest, bool>, CancellationToken, Task<CompletionResult>> run,
            string workspace,
            ApprovalHandler approve = null,
            Action<string> onFailure = null)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _workspace = workspace ?? "";
            _approve = approve;
            _onFailure = onFailure;
        }

        public string Name => "DshTask";

        public string Description =>
            "把一件任务交给 dsh（DeepSeek Harness）的 agent 执行：它有自己的会话、工具和模型，适合需要 dsh 插件生态" +
            "（例如 our-free-model 的免费/EAC 模型）或 dsh 特有工具的任务。它会直接改当前工作区的文件，每次权限都会转给你确认。" +
            "只做一次问答或单文件小改就别用它，用你自己的工具更快；需要 OpenCode 的会话/agent 时用 OpenCodeTask。";

        public string ParamsSchema =>
            "task: string（任务描述，直接作为 dsh 的 prompt）；" +
            "model: string（可选，dsh/<provider>/<model>，缺省用 dsh 自己配置的模型）；" +
            "timeoutSec: number（可选，默认 600，范围 30-3600）";

        public bool IsReadOnly => false;

        public async Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            string task = args.Str("task");
            if (string.IsNullOrWhiteSpace(task)) return AgentToolResult.Failure("缺少参数 task");
            if (!Directory.Exists(_workspace))
                return AgentToolResult.Failure($"工作目录不存在：{_workspace}");

            string model = args.Str("model", "");
            if (model.Length > 0 && !DshAcpIds.IsDsh(model))
                return AgentToolResult.Failure($"模型 id 应形如 dsh/<provider>/<model>，收到 {model}");

            int timeout = Math.Clamp(args.Int("timeoutSec", 600), 30, 3600);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeout));

            Func<AcpPermissionRequest, bool> decide = request =>
            {
                if (_approve == null) return false;
                var decision = _approve(new ApprovalRequest(KindFor(request), Name, Describe(request), "dsh"));
                return decision != ApprovalDecision.Deny;
            };

            string checkpoint = Snapshots.Create(_workspace, "dsh");
            try
            {
                var result = await _run(task, model, decide, cts.Token);
                string text = result?.Content ?? "";
                if (text.Trim().Length == 0)
                    return AgentToolResult.Failure("dsh 结束了但没给文本（可能只调了工具）");

                return AgentToolResult.Success(text + "\n\n---\n[dsh task "
                    + (model.Length > 0 ? model : "default") + CheckpointText(checkpoint) + "]");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return AgentToolResult.Failure($"dsh 任务 {timeout} 秒未完成");
            }
            catch (Exception ex)
            {
                _onFailure?.Invoke(ex.Message);
                return AgentToolResult.Failure("dsh 执行失败：" + ex.Message);
            }
        }

        private static string CheckpointText(string checkpoint)
            => string.IsNullOrEmpty(checkpoint)
                ? ""
                : " snapshot " + (checkpoint.Length <= 8 ? checkpoint : checkpoint[..8]);

        private ApprovalKind KindFor(AcpPermissionRequest request)
        {
            foreach (string path in request.Paths ?? [])
            {
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) continue;
                if (!IsInsideWorkspace(path)) return ApprovalKind.OutsideWorkspace;
            }
            return ApprovalKind.SensitiveAction;
        }

        private bool IsInsideWorkspace(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_workspace)) return false;
                string root = Path.GetFullPath(_workspace).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(path);
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string Describe(AcpPermissionRequest request)
        {
            string title = string.IsNullOrWhiteSpace(request.Title) ? "dsh 请求权限" : request.Title;
            if (!string.IsNullOrWhiteSpace(request.Kind)) title += $"（{request.Kind}）";
            if (request.Paths is { Count: > 0 }) title += "：" + string.Join("；", request.Paths.Take(4));
            return title;
        }
    }
}
