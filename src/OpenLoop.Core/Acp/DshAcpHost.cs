using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;

namespace OpenLoop.Core.Acp
{
    /// <summary>ACP 里的一个可选模型：界面上用的 id 与协议里的原值各一份。</summary>
    public sealed record AcpModel(string Id, string Value, string Name, string Group);

    /// <summary>
    /// dsh 的 ACP 宿主（<c>dsh --profile acp</c>）：一个进程、按工作区缓存会话。
    /// 模型调用发生在 dsh 里，宿主只发 prompt、收 <c>session/update</c>；
    /// 权限按构造时给的策略回答（跟随 OpenLoop 的 FullAccess）。
    /// </summary>
    public sealed class DshAcpHost : IDisposable
    {
        private readonly AcpClient _client;
        private readonly Func<bool> _allow;
        private readonly object _gate = new();
        private readonly Dictionary<string, string> _sessionIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, AcpModel>> _models = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _appliedModel = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Func<AcpPermissionRequest, bool>> _permissionBySession = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Run> _runs = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;
        private volatile bool _initialized;

        public DshAcpHost(string command, IReadOnlyList<string> prefixArgs, string profile, Func<bool> allow)
        {
            _allow = allow ?? (() => false);
            var startInfo = new ProcessStartInfo(command)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (string arg in prefixArgs ?? []) startInfo.ArgumentList.Add(arg);
            if (!string.IsNullOrWhiteSpace(profile))
            {
                startInfo.ArgumentList.Add("--profile");
                startInfo.ArgumentList.Add(profile);
            }

            _client = new AcpClient(startInfo, _allow);
            _client.OnPermission = DecidePermission;
            _client.Notification += OnNotification;
        }

        /// <summary>权限按会话分流：任务工具那段用「逐次问用户」，模型那路仍是 FullAccess 策略。</summary>
        private bool DecidePermission(AcpPermissionRequest request)
        {
            if (!string.IsNullOrEmpty(request.SessionId))
            {
                Func<AcpPermissionRequest, bool> decide;
                lock (_gate) _permissionBySession.TryGetValue(request.SessionId, out decide);
                if (decide != null) return decide(request);
            }
            return _allow();
        }

        public async Task InitializeAsync(CancellationToken ct)
        {
            if (_initialized) return;
            await _client.RequestAsync("initialize",
                new { protocolVersion = 1, clientCapabilities = new { } }, ct).ConfigureAwait(false);
            _initialized = true;
        }

        /// <summary>当前工作区的可选模型（来自 <c>session/new</c> 的 configOptions）。</summary>
        public async Task<IReadOnlyList<AcpModel>> ModelsAsync(string workspace, CancellationToken ct)
        {
            string sessionId = await EnsureSessionAsync(workspace, ct).ConfigureAwait(false);
            lock (_gate)
                return _models.TryGetValue(sessionId, out var map) ? map.Values.ToList() : [];
        }

        public async Task<CompletionResult> RunAsync(
            string workspace, string modelId, IReadOnlyList<LlmMessage> messages,
            Action<string> onDelta, Action<string> onReasoning, CancellationToken ct)
        {
            string sessionId = await EnsureSessionAsync(workspace, ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(modelId))
            {
                string value = ValueOf(modelId);
                string applied;
                lock (_gate) _appliedModel.TryGetValue(sessionId, out applied);
                if (value != null && value != applied)
                {
                    await _client.RequestAsync("session/set_config_option",
                        new { sessionId, configId = "model", value }, ct).ConfigureAwait(false);
                    lock (_gate) _appliedModel[sessionId] = value;
                }
            }

            var run = new Run(onDelta, onReasoning);
            lock (_gate) _runs[sessionId] = run;

            using var cancel = ct.Register(() =>
            {
                // 取消：告诉代理停下（通知，不等应答）
                _ = _client.NotifyAsync("session/cancel", new { sessionId }, CancellationToken.None);
            });

            try
            {
                await _client.RequestAsync("session/prompt",
                    new { sessionId, prompt = new[] { new { type = "text", text = Prompt(messages) } } }, ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                lock (_gate) _runs.Remove(sessionId);
            }

            return new CompletionResult
            {
                Content = run.Text.ToString(),
                Reasoning = run.Reasoning.Length == 0 ? null : run.Reasoning.ToString(),
                FinishReason = "stop"
            };
        }

        /// <summary>
        /// 一件任务交给 dsh：新开一个会话（不污染模型那条缓存会话），跑完关掉。
        /// <paramref name="decide"/> 是这次任务的权限策略（逐次问用户），返回 false 即拒绝。
        /// </summary>
        public async Task<CompletionResult> RunTaskAsync(
            string workspace, string modelId, string task,
            Func<AcpPermissionRequest, bool> decide, CancellationToken ct)
        {
            await InitializeAsync(ct).ConfigureAwait(false);

            var created = await _client.RequestAsync("session/new",
                new { cwd = workspace, mcpServers = Array.Empty<object>() }, ct).ConfigureAwait(false);

            string sessionId = created.TryGetProperty("sessionId", out var id) ? id.GetString() ?? "" : "";
            if (sessionId.Length == 0) throw new InvalidOperationException("ACP 没有给 sessionId");

            lock (_gate)
            {
                _models[sessionId] = ParseModels(created);
                if (decide != null) _permissionBySession[sessionId] = decide;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(modelId))
                {
                    string value = ValueOf(modelId);
                    if (value != null)
                    {
                        await _client.RequestAsync("session/set_config_option",
                            new { sessionId, configId = "model", value }, ct).ConfigureAwait(false);
                        lock (_gate) _appliedModel[sessionId] = value;
                    }
                }

                var run = new Run(null, null);
                lock (_gate) _runs[sessionId] = run;
                using var cancel = ct.Register(() =>
                    _ = _client.NotifyAsync("session/cancel", new { sessionId }, CancellationToken.None));

                try
                {
                    await _client.RequestAsync("session/prompt",
                        new { sessionId, prompt = new[] { new { type = "text", text = task } } }, ct)
                        .ConfigureAwait(false);
                }
                finally
                {
                    lock (_gate) _runs.Remove(sessionId);
                }

                return new CompletionResult
                {
                    Content = run.Text.ToString(),
                    Reasoning = run.Reasoning.Length == 0 ? null : run.Reasoning.ToString(),
                    FinishReason = "stop"
                };
            }
            finally
            {
                lock (_gate) _permissionBySession.Remove(sessionId);
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await _client.RequestAsync("session/close", new { sessionId }, closeCts.Token)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // 有的代理不支持 close：进程随宿主退出，不影响正确性
                }
            }
        }

        private async Task<string> EnsureSessionAsync(string workspace, CancellationToken ct)
        {
            lock (_gate)
            {
                if (_sessionIds.TryGetValue(workspace, out string existing)) return existing;
            }

            var result = await _client.RequestAsync("session/new",
                new { cwd = workspace, mcpServers = Array.Empty<object>() }, ct).ConfigureAwait(false);

            string sessionId = result.TryGetProperty("sessionId", out var id) ? id.GetString() ?? "" : "";
            if (sessionId.Length == 0) throw new InvalidOperationException("ACP 没有给 sessionId");

            var map = ParseModels(result);

            lock (_gate)
            {
                _sessionIds[workspace] = sessionId;
                _models[sessionId] = map;
            }
            return sessionId;
        }

        private static Dictionary<string, AcpModel> ParseModels(JsonElement result)
        {
            var map = new Dictionary<string, AcpModel>(StringComparer.OrdinalIgnoreCase);
            if (!result.TryGetProperty("configOptions", out var options) || options.ValueKind != JsonValueKind.Array)
                return map;

            foreach (var option in options.EnumerateArray())
            {
                if (!option.TryGetProperty("id", out var optionId) ||
                    optionId.GetString() != "model") continue;
                if (!option.TryGetProperty("options", out var groups) || groups.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var group in groups.EnumerateArray())
                {
                    string groupName = group.TryGetProperty("group", out var g) ? g.GetString() ?? "" : "";
                    if (!group.TryGetProperty("options", out var entries) || entries.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var entry in entries.EnumerateArray())
                    {
                        string value = entry.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";
                        string name = entry.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (value.Length == 0) continue;

                        // 值形如 ["provider","model"]；界面 id 用 dsh/provider/model
                        string raw = value;
                        try
                        {
                            using var doc = JsonDocument.Parse(value);
                            if (doc.RootElement.ValueKind == JsonValueKind.Array &&
                                doc.RootElement.GetArrayLength() == 2)
                            {
                                string provider = doc.RootElement[0].GetString() ?? "";
                                string model = doc.RootElement[1].GetString() ?? "";
                                raw = $"{DshAcpIds.Prefix}{provider}/{model}";
                            }
                        }
                        catch (JsonException)
                        {
                        }

                        map[raw] = new AcpModel(raw, value, name, groupName);
                    }
                }
            }

            return map;
        }

        private string ValueOf(string modelId)
        {
            lock (_gate)
            {
                foreach (var map in _models.Values)
                    if (map.TryGetValue(modelId, out var model)) return model.Value;
            }
            return null;
        }

        private void OnNotification(string method, JsonElement root)
        {
            if (method != "session/update") return;
            if (!root.TryGetProperty("params", out var p)) return;

            string sessionId = p.TryGetProperty("sessionId", out var s) ? s.GetString() ?? "" : "";
            if (!p.TryGetProperty("update", out var update)) return;

            string kind = update.TryGetProperty("sessionUpdate", out var k) ? k.GetString() ?? "" : "";
            if (kind != "agent_message_chunk" && kind != "agent_thought_chunk") return;
            if (!update.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("text", out var text) ||
                text.ValueKind != JsonValueKind.String) return;

            Run run;
            lock (_gate)
            {
                if (!_runs.TryGetValue(sessionId, out run)) return;
            }

            string piece = text.GetString();
            if (kind == "agent_message_chunk") run.Text.Append(piece);
            else run.Reasoning.Append(piece);
            if (kind == "agent_message_chunk") run.OnDelta?.Invoke(piece);
            else run.OnReasoning?.Invoke(piece);
        }

        /// <summary>把历史压成一条 prompt：ACP 的会话有自己的历史，宿主只负责把这次要说的话送进去。</summary>
        private static string Prompt(IReadOnlyList<LlmMessage> messages)
        {
            var sb = new StringBuilder();
            foreach (var message in messages)
            {
                if (string.IsNullOrWhiteSpace(message?.Content)) continue;
                sb.Append('[').Append(message.Role).Append("] ").AppendLine(message.Content);
            }
            return sb.ToString().TrimEnd();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _client.Dispose();
        }

        private sealed class Run(Action<string> onDelta, Action<string> onReasoning)
        {
            public Action<string> OnDelta { get; } = onDelta;
            public Action<string> OnReasoning { get; } = onReasoning;
            public StringBuilder Text { get; } = new();
            public StringBuilder Reasoning { get; } = new();
        }
    }

    /// <summary>模型 id 的命名空间：<c>dsh/&lt;provider&gt;/&lt;model&gt;</c>。</summary>
    public static class DshAcpIds
    {
        public const string Prefix = "dsh/";

        public static bool IsDsh(string model) =>
            model != null && model.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

        public static string Model(string model) =>
            IsDsh(model) ? model[Prefix.Length..] : model;
    }
}
