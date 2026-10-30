using System.Text;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Safety;
using OpenLoop.Core.Tools;

namespace OpenLoop.Core.OpenCode
{
    /// <summary>
    /// 把一整件任务交给本机 OpenCode 会话执行。工作区经边界校验后作为会话位置；
    /// OpenCode 的权限请求转到宿主审批回调；结束回传会话证据与该次改动。
    /// </summary>
    public sealed class OpenCodeTaskTool : ITool
    {
        private readonly Func<OpenCodeClient> _client;
        private readonly Func<string, OpenCodeModelRef?> _resolve;
        private readonly ApprovalHandler _approve;
        private readonly bool _askAllPermissions;
        private readonly string _workspace;

        public OpenCodeTaskTool(Func<OpenCodeClient> client, string workspace,
            Func<string, OpenCodeModelRef?> resolve = null, ApprovalHandler approve = null,
            bool askAllPermissions = true)
        {
            _client = client;
            _workspace = workspace;
            _resolve = resolve;
            _approve = approve;
            _askAllPermissions = askAllPermissions;
        }

        public string Name => "opencode_task";

        public string Description =>
            "把一件完整任务交给本机 OpenCode agent（它有独立工具，会直接改当前工作目录里的文件）。" +
            "OpenCode 的权限请求会转给用户审批；返回最终答复、会话 ID、花费与改动文件。适合需要多步探索和编辑的独立任务；简单问答不要用它。";

        public string ParamsSchema =>
            "task: string（必填，交代完整目标与验收标准）；model: string 可选（opencode/<模型>@<提供商>，缺省用 OpenCode 默认模型）；" +
            "agent: string 可选（OpenCode 的 agent 名，如 build/plan）；timeoutSec: number 可选（默认 600，30-3600）";

        public bool IsReadOnly => false;

        public async Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            string task = args.Str("task");
            if (string.IsNullOrWhiteSpace(task)) return AgentToolResult.Failure("缺少参数 task");
            if (!Directory.Exists(_workspace))
                return AgentToolResult.Failure($"工作目录不存在：{_workspace}");

            var client = _client?.Invoke();
            if (client == null) return AgentToolResult.Failure("OpenCode 服务未就绪：先在设置里检查服务状态");

            string modelId = args.Str("model", "");
            OpenCodeModelRef? model = null;
            if (modelId.Length > 0)
            {
                model = _resolve?.Invoke(modelId);
                if (model == null)
                    return AgentToolResult.Failure($"不认识的模型：{modelId}（应形如 opencode/<模型>@<提供商>）");
            }

            string checkpoint = Snapshots.Create(_workspace, "opencode");
            string sessionID = null;
            try
            {
                string title = task.Length <= 40 ? task : task[..40];
                sessionID = await client.CreateSessionAsync(
                    title, _workspace, model, args.Str("agent", ""), _askAllPermissions, ct);
                if (string.IsNullOrWhiteSpace(sessionID))
                    return AgentToolResult.Failure("OpenCode 没有返回会话 ID");

                await client.PromptAsync(sessionID, task, ct);

                int timeout = Math.Clamp(args.Int("timeoutSec", 600), 30, 3600);
                var deadline = DateTime.UtcNow.AddSeconds(timeout);
                var answered = new HashSet<string>(StringComparer.Ordinal);
                while (true)
                {
                    await Task.Delay(1000, ct);

                    foreach (var permission in await client.GetPermissionsAsync(sessionID, ct))
                    {
                        if (!answered.Add(permission.ID)) continue;
                        if (_approve == null)
                            return AgentToolResult.Failure(
                                $"OpenCode 请求权限但当前没有审批通道（会话 {sessionID}）：{Describe(permission)}");

                        var decision = _approve(new ApprovalRequest(
                            KindFor(permission), Name, Describe(permission), "OpenCode"));
                        if (decision == ApprovalDecision.Deny)
                        {
                            await client.ReplyPermissionAsync(sessionID, permission.ID, "reject", "用户拒绝", ct);
                            return AgentToolResult.Failure(
                                $"用户拒绝 OpenCode 的权限请求：{Describe(permission)}");
                        }

                        await client.ReplyPermissionAsync(sessionID, permission.ID,
                            decision == ApprovalDecision.AllowAlways ? "always" : "once", null, ct);
                    }

                    var snapshot = await client.GetSessionAsync(sessionID, ct);
                    if (snapshot.Idle)
                        return Build(sessionID, snapshot,
                            await client.GetMessagesAsync(sessionID, ct),
                            await client.GetDiffAsync(sessionID, ct),
                            checkpoint);

                    if (DateTime.UtcNow >= deadline)
                    {
                        await SafeInterruptAsync(client, sessionID);
                        return AgentToolResult.Failure(
                            $"OpenCode 超过 {timeout} 秒未完成（会话 {sessionID}{CheckpointText(checkpoint)}）");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (sessionID != null) await SafeInterruptAsync(client, sessionID);
                throw;
            }
            catch (Exception ex)
            {
                return AgentToolResult.Failure("OpenCode 执行失败：" + ex.Message);
            }
        }

        private static AgentToolResult Build(
            string sessionID, OpenCodeSessionSnapshot snapshot, IReadOnlyList<OpenCodeMessage> messages,
            IReadOnlyList<OpenCodeFileDiff> diffs, string checkpoint)
        {
            string text = messages?.LastOrDefault(m => !string.IsNullOrWhiteSpace(m.Text))?.Text ?? "";
            if (text.Length == 0)
                return AgentToolResult.Failure($"OpenCode 已结束但没有正文（会话 {sessionID}，可能只调用了工具）");

            var meta = new StringBuilder();
            meta.Append("\n\n---\n[opencode session ").Append(sessionID);
            if (snapshot?.Cost is { } cost) meta.Append(" cost $").Append(cost.ToString("0.######"));
            if (snapshot != null && (snapshot.TokensIn.HasValue || snapshot.TokensOut.HasValue))
                meta.Append(" tokens ").Append(snapshot.TokensIn?.ToString() ?? "?")
                    .Append('/').Append(snapshot.TokensOut?.ToString() ?? "?");

            string tools = string.Join(", ", messages.SelectMany(m => m.Tools ?? []).Distinct(StringComparer.Ordinal));
            if (tools.Length > 0) meta.Append(" tools: ").Append(tools);

            if (diffs is { Count: > 0 })
            {
                var parts = diffs.Take(8)
                    .Select(d => $"{d.File}(+{d.Additions}/-{d.Deletions})")
                    .ToList();
                if (diffs.Count > parts.Count) parts.Add($"…共 {diffs.Count} 个文件");
                meta.Append(" files: ").Append(string.Join(", ", parts));
            }

            meta.Append(CheckpointText(checkpoint)).Append(']');
            return AgentToolResult.Success(text + meta);
        }

        private ApprovalKind KindFor(OpenCodePermission permission)
        {
            foreach (string resource in permission.Resources ?? [])
            {
                if (!Path.IsPathRooted(resource)) continue;
                if (!IsInsideWorkspace(resource)) return ApprovalKind.OutsideWorkspace;
            }
            return ApprovalKind.SensitiveAction;
        }

        private bool IsInsideWorkspace(string resource)
        {
            try
            {
                int wildcard = resource.IndexOfAny(['*', '?']);
                string path = wildcard >= 0 ? resource[..wildcard] : resource;
                if (path.Length == 0) return true;

                string root = Path.GetFullPath(_workspace).TrimEnd(Path.DirectorySeparatorChar);
                string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                return full.Equals(root, StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string Describe(OpenCodePermission permission)
        {
            string resources = permission.Resources is { Count: > 0 }
                ? " " + string.Join(", ", permission.Resources)
                : "";
            string message = string.IsNullOrWhiteSpace(permission.Message) ? "" : $"（{permission.Message}）";
            return permission.Action + resources + message;
        }

        private static string CheckpointText(string checkpoint)
            => string.IsNullOrEmpty(checkpoint)
                ? ""
                : " snapshot " + (checkpoint.Length <= 8 ? checkpoint : checkpoint[..8]);

        private static async Task SafeInterruptAsync(OpenCodeClient client, string sessionID)
        {
            try { await client.InterruptAsync(sessionID, CancellationToken.None); }
            catch { }
        }
    }
}
