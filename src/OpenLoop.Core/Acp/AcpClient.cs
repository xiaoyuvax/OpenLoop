using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace OpenLoop.Core.Acp
{
    /// <summary>
    /// ACP（Agent Client Protocol）的 stdio 客户端：一行一条 JSON-RPC。
    /// 只实现对 OpenLoop 有用的部分——initialize / session.new / set_config_option / prompt / cancel，
    /// 以及客户端侧的 <c>session/request_permission</c>（按调用方给的策略允许或拒绝）。
    /// 不实现 fs/terminal 那些可选能力：initialize 里 clientCapabilities 声明为空，
    /// 代理就自己干活，不会反过来调宿主。
    /// </summary>
    public sealed class AcpClient : IDisposable
    {
        private readonly Process _process;
        private readonly StreamWriter _stdin;
        private readonly StreamReader _stdout;
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending = [];
        private readonly object _pendingGate = new();
        private readonly Func<bool> _allow;
        private readonly Task _reader;
        private int _nextId;

        /// <summary>agent → 宿主的通知（<c>session/update</c> 等）。</summary>
        public event Action<string, JsonElement> Notification;

        public AcpClient(ProcessStartInfo startInfo, Func<bool> allow)
        {
            _allow = allow ?? (() => false);
            _process = Process.Start(startInfo)
                       ?? throw new InvalidOperationException("ACP 进程起不来");
            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;
            _reader = Task.Run(ReadLoopAsync);
        }

        public bool HasExited => _process.HasExited;

        public async Task<JsonElement> RequestAsync(string method, object payload, CancellationToken ct)
        {
            int id = Interlocked.Increment(ref _nextId);
            var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pendingGate) _pending[id] = completion;

            string body = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = payload });
            await _writeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _stdin.WriteLineAsync(body.AsMemory(), ct).ConfigureAwait(false);
                await _stdin.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }

            using var registration = ct.Register(() => completion.TrySetCanceled(ct));
            return await completion.Task.ConfigureAwait(false);
        }

        public async Task NotifyAsync(string method, object payload, CancellationToken ct)
        {
            string body = JsonSerializer.Serialize(new { jsonrpc = "2.0", method, @params = payload });
            await _writeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _stdin.WriteLineAsync(body.AsMemory(), ct).ConfigureAwait(false);
                await _stdin.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                while (await _stdout.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    JsonDocument doc;
                    try { doc = JsonDocument.Parse(line); }
                    catch (JsonException) { continue; }

                    using (doc)
                    {
                        var root = doc.RootElement.Clone();
                        bool hasId = root.TryGetProperty("id", out var idNode);
                        bool hasMethod = root.TryGetProperty("method", out var methodNode);

                        if (hasId && hasMethod)
                        {
                            await AnswerAsync(idNode, methodNode.GetString(), root).ConfigureAwait(false);
                            continue;
                        }

                        if (hasId)
                        {
                            TaskCompletionSource<JsonElement> pending;
                            lock (_pendingGate)
                            {
                                if (!_pending.Remove(idNode.GetInt32(), out pending)) continue;
                            }
                            if (root.TryGetProperty("error", out var error))
                                pending.TrySetException(new InvalidOperationException(error.ToString()));
                            else
                                pending.TrySetResult(root.TryGetProperty("result", out var result) ? result : default);
                            continue;
                        }

                        if (hasMethod) Notification?.Invoke(methodNode.GetString() ?? "", root);
                    }
                }
            }
            catch
            {
                // 进程退出：让所有等待中的请求失败
            }

            lock (_pendingGate)
            {
                foreach (var pending in _pending.Values)
                    pending.TrySetException(new InvalidOperationException("ACP 进程已退出"));
                _pending.Clear();
            }
        }

        /// <summary>代理反向请求：目前只处理权限；其余回 method not found（声明了空能力，代理不该发）。</summary>
        private async Task AnswerAsync(JsonElement id, string method, JsonElement root)
        {
            object result = null;
            if (method == "session/request_permission")
            {
                bool allow = _allow();
                result = new { outcome = PickOutcome(root, allow) };
            }

            string body = result == null
                ? JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = id.Clone(),
                    error = new { code = -32601, message = "method not found" }
                })
                : JsonSerializer.Serialize(new { jsonrpc = "2.0", id = id.Clone(), result });
            await _writeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await _stdin.WriteLineAsync(body.AsMemory()).ConfigureAwait(false);
                await _stdin.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        private static object PickOutcome(JsonElement root, bool allow)
        {
            string wanted = allow ? "allow" : "reject";
            if (root.TryGetProperty("params", out var p) &&
                p.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
            {
                object fallback = null;
                foreach (var option in options.EnumerateArray())
                {
                    string kind = option.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
                    string optionId = option.TryGetProperty("optionId", out var o) ? o.GetString() ?? "" : "";
                    if (optionId.Length == 0) continue;
                    if (kind.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
                        return new { outcome = "selected", optionId };
                    fallback ??= new { outcome = "selected", optionId };
                }
                if (fallback != null) return fallback;
            }
            return new { outcome = "cancelled" };
        }

        public void Dispose()
        {
            try { if (!_process.HasExited) _process.Kill(true); } catch { }
            try { _process.Dispose(); } catch { }
            try { _stdin.Dispose(); } catch { }
            try { _stdout.Dispose(); } catch { }
            _writeGate.Dispose();
        }
    }
}
