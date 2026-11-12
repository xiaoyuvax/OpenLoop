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
        private readonly Action<string> _onFailure;

        public OpenCodeTaskTool(Func<OpenCodeClient> client, string workspace,
            Func<string, OpenCodeModelRef?> resolve = null, ApprovalHandler approve = null,
            bool askAllPermissions = true, Action<string> onFailure = null)
        {
            _client = client;
            _workspace = workspace;
            _resolve = resolve;
            _approve = approve;
            _askAllPermissions = askAllPermissions;
            _onFailure = onFailure;
        }

        public string Name => "OpenCodeTask";

        public string Description =>
            "把一件完整任务交给本机 OpenCode agent（它有独立工具，会直接改当前工作目录里的文件）。" +
            "OpenCode 的权限请求会转给用户审批；返回最终答复、会话 ID、花费与改动文件。" +
            "适合需要多步探索和编辑的独立任务；简单问答不要用它。" +
            "需要 dsh 的插件生态（如 our-free-model 的免费模型）或 dsh 专属工具时改用它旁边的 DshTask。";

        public string ParamsSchema =>
            "task: string（必填，交代完整目标与验收标准）；model: string 可选（opencode/<模型>@<提供商>，缺省用 OpenCode 默认模型）；" +
            "agent: string 可选（OpenCode 的 agent 名，如 build/plan）；" +
            "timeoutSec: number 可选（默认 600，30-3600；连续这么久没有任何输出才判挂住，不是总时长上限）";

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
                    title, _workspace, model, args.Str("agent", ""), _askAllPermissions ? "ask" : null, ct);
                if (string.IsNullOrWhiteSpace(sessionID))
                    return AgentToolResult.Failure("OpenCode 没有返回会话 ID");

                await client.PromptAsync(sessionID, task, ct);

                int idleSeconds = Math.Clamp(args.Int("timeoutSec", 600), 30, 3600);
                var idleLimit = TimeSpan.FromSeconds(idleSeconds);
                // 同 RunTurnAsync：不设总时限，只看「多久没有新输出」。
                var lastOutput = DateTime.UtcNow;
                string progress = "";
                var answered = new HashSet<string>(StringComparer.Ordinal);
                while (true)
                {
                    await Task.Delay(1000, ct);

                    bool interacted = false;
                    foreach (var permission in await client.GetPermissionsAsync(sessionID, ct))
                    {
                        if (!answered.Add(permission.ID)) continue;
                        interacted = true;
                        if (_approve == null)
                            return AgentToolResult.Failure(
                                $"OpenCode 请求权限但当前没有审批通道（会话 {sessionID}）：{OpenCodePermissions.Describe(permission)}");

                        var decision = _approve(new ApprovalRequest(
                            OpenCodePermissions.KindFor(_workspace, permission), Name,
                            OpenCodePermissions.Describe(permission), "OpenCode"));
                        if (decision == ApprovalDecision.Deny)
                        {
                            await client.ReplyPermissionAsync(sessionID, permission.ID, "reject", "用户拒绝", ct);
                            return AgentToolResult.Failure(
                                $"用户拒绝 OpenCode 的权限请求：{OpenCodePermissions.Describe(permission)}");
                        }

                        await client.ReplyPermissionAsync(sessionID, permission.ID,
                            decision == ApprovalDecision.AllowAlways ? "always" : "once", null, ct);
                    }
                    // 等人审批（最长 60 秒）也是「在动」，别把它当成挂住。
                    if (interacted) lastOutput = DateTime.UtcNow;

                    var messages = await client.GetMessagesAsync(sessionID, ct);
                    string now = OpenCodeClient.Progress(messages);
                    if (!string.Equals(now, progress, StringComparison.Ordinal))
                    {
                        progress = now;
                        lastOutput = DateTime.UtcNow;
                    }

                    var snapshot = await client.GetSessionAsync(sessionID, ct);
                    if (snapshot.Idle)
                        return Build(sessionID, snapshot, messages,
                            await client.GetDiffAsync(sessionID, ct),
                            checkpoint);

                    if (DateTime.UtcNow - lastOutput > idleLimit)
                    {
                        await SafeInterruptAsync(client, sessionID);
                        return AgentToolResult.Failure(
                            $"OpenCode 连续 {idleSeconds} 秒没有任何输出，已判定挂住（会话 {sessionID}{CheckpointText(checkpoint)}）");
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
                _onFailure?.Invoke(ex.Message);
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

    /// <summary>OpenCode 权限 → OpenLoop 审批的映射（模型位和任务工具共用一套判定）。</summary>
    internal static class OpenCodePermissions
    {
        /// <summary>没有审批通道就返回 null（调用方据此一律拒绝）。</summary>
        public static Func<OpenCodePermission, bool> Decider(string workspace, ApprovalHandler approve)
            => approve == null
                ? null
                : permission => approve(new ApprovalRequest(
                    KindFor(workspace, permission), "OpenCode", Describe(permission), "OpenCode"))
                    != ApprovalDecision.Deny;

        public static ApprovalKind KindFor(string workspace, OpenCodePermission permission)
        {
            foreach (string resource in permission.Resources ?? [])
            {
                if (!Path.IsPathRooted(resource)) continue;
                if (!IsInsideWorkspace(workspace, resource)) return ApprovalKind.OutsideWorkspace;
            }
            return ApprovalKind.SensitiveAction;
        }

        public static bool IsInsideWorkspace(string workspace, string resource)
        {
            try
            {
                int wildcard = resource.IndexOfAny(['*', '?']);
                string path = wildcard >= 0 ? resource[..wildcard] : resource;
                if (path.Length == 0) return true;

                string root = Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar);
                string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                return full.Equals(root, StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string Describe(OpenCodePermission permission)
        {
            string resources = permission.Resources is { Count: > 0 }
                ? " " + string.Join(", ", permission.Resources)
                : "";
            string message = string.IsNullOrWhiteSpace(permission.Message) ? "" : $"（{permission.Message}）";
            return permission.Action + resources + message;
        }
    }
}
