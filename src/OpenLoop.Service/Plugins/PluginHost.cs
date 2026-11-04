using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenLoop.Core.Agent;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Logging;

namespace OpenLoop.Service.Plugins
{
    /// <summary>
    /// 一个插件进程的句柄。工具调用走 stdio 的一行 JSON，
    /// 进程崩了最多重启一次；再崩就只摘掉它的贡献，宿主照常跑。
    /// </summary>
    public sealed class PluginHost : IDisposable
    {
        private const int MaxRestarts = 1;
        private const int MaxResultChars = 8000;

        private readonly PluginManifest _manifest;
        private readonly Func<IPluginTransport> _factory;
        private readonly TimeSpan _timeout;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private IPluginTransport _transport;
        private int _restarts;
        private int _nextId;
        private bool _dead;
        private bool _disposed;

        public PluginHost(PluginManifest manifest) : this(manifest, null)
        {
        }

        internal PluginHost(PluginManifest manifest, Func<IPluginTransport> factory)
        {
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            var external = factory;
            _factory = external ?? (() => new ProcessPluginTransport(manifest));
            int ms = manifest.Entry?.TimeoutMs > 0 ? manifest.Entry.TimeoutMs : 60000;
            _timeout = TimeSpan.FromMilliseconds(ms);
        }

        public string Id => _manifest.Id;

        public async Task<AgentToolResult> CallToolAsync(
            string tool, IReadOnlyDictionary<string, object> args, string workspace, CancellationToken ct)
        {
            if (_dead) return AgentToolResult.Failure($"插件 {_manifest.Id} 已停止");

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                try
                {
                    return await SendAsync(tool, args, workspace, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // 不重放本次调用：工具可能有副作用，重放等于执行两次。
                    // 只把进程丢掉，下一次调用时重新拉起（整个宿主生命周期最多一次）。
                    Drop();

                    if (_restarts < MaxRestarts)
                    {
                        _restarts++;
                        AppLog.Warn($"插件 {_manifest.Id} 失败，下次调用重试：{Describe(ex)}");
                    }
                    else
                    {
                        _dead = true;
                        AppLog.Warn($"插件 {_manifest.Id} 已停止：{Describe(ex)}");
                    }

                    return AgentToolResult.Failure($"插件 {_manifest.Id} 失败：{Describe(ex)}");
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<AgentToolResult> SendAsync(
            string tool, IReadOnlyDictionary<string, object> args, string workspace, CancellationToken ct)
        {
            var transport = _transport ??= _factory();
            int id = ++_nextId;
            string request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id,
                method = "tool.call",
                @params = new { tool, args, workspace }
            }, BusProtocol.Json);

            string reply = await transport.RequestAsync(request, _timeout, ct).ConfigureAwait(false);
            return Parse(reply);
        }

        private static AgentToolResult Parse(string reply)
        {
            try
            {
                using var doc = JsonDocument.Parse(reply);
                var root = doc.RootElement;

                if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                    return AgentToolResult.Failure(Text(error));

                if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
                    return AgentToolResult.Failure("插件返回缺少 result");

                if (result.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
                    return AgentToolResult.Failure(
                        result.TryGetProperty("error", out var reason) ? Text(reason) : "插件返回失败");

                string data = result.TryGetProperty("data", out var payload) ? Text(payload) : "";
                return AgentToolResult.Success(data.Length <= MaxResultChars
                    ? data
                    : data[..MaxResultChars] + $"…（截断，共 {data.Length} 字符）");
            }
            catch (JsonException ex)
            {
                return AgentToolResult.Failure("插件返回不是 JSON：" + ex.Message);
            }
        }

        private static string Text(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => element.GetRawText()
        };

        private static string Describe(Exception ex)
            => ex.Message.Length <= 300 ? ex.Message : ex.Message[..300] + "…";

        private void Drop()
        {
            try { _transport?.Dispose(); } catch { }
            _transport = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Drop();
            _gate.Dispose();
        }
    }

    /// <summary>把一行请求送进插件，取回一行应答。单独抽出来是为了能测崩溃/超时而不真起进程。</summary>
    internal interface IPluginTransport : IDisposable
    {
        Task<string> RequestAsync(string json, TimeSpan timeout, CancellationToken ct);
    }

    /// <summary>
    /// 生产用的传输：stdin/stdout 各一行 JSON；stderr 当日志。
    /// 超时就杀进程（协议已经不同步，继续读只会拿到错位的应答）。
    /// </summary>
    internal sealed class ProcessPluginTransport : IPluginTransport
    {
        private readonly Process _process;
        private readonly string _id;

        public ProcessPluginTransport(PluginManifest manifest)
        {
            _id = manifest.Id;
            var startInfo = new ProcessStartInfo(manifest.Entry.Command)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = manifest.Root
            };
            foreach (string argument in manifest.Entry.Args ?? [])
                startInfo.ArgumentList.Add(argument);

            _process = new Process { StartInfo = startInfo };
            if (!_process.Start())
                throw new InvalidOperationException("插件进程无法启动：" + manifest.Entry.Command);

            _process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data)) AppLog.Warn($"[plugin:{_id}] {e.Data}");
            };
            _process.BeginErrorReadLine();
        }

        public async Task<string> RequestAsync(string json, TimeSpan timeout, CancellationToken ct)
        {
            if (_process.HasExited) throw new IOException("插件已退出");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                await _process.StandardInput.WriteLineAsync(json.AsMemory(), cts.Token).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync(cts.Token).ConfigureAwait(false);

                while (true)
                {
                    string line = await _process.StandardOutput.ReadLineAsync()
                        .WaitAsync(timeout, ct).ConfigureAwait(false);
                    if (line == null) throw new IOException("插件已退出");

                    string trimmed = line.Trim();
                    if (trimmed.Length == 0) continue;
                    if (LooksLikeReply(trimmed)) return trimmed;

                    AppLog.Warn($"[plugin:{_id}] {Trim(trimmed)}");
                }
            }
            catch (TimeoutException)
            {
                Kill();
                throw new TimeoutException("插件响应超时");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Kill();
                throw new TimeoutException($"插件 {_id} 超过 {timeout.TotalSeconds:0.#}s 未响应");
            }
        }

        private static bool LooksLikeReply(string line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                return doc.RootElement.ValueKind == JsonValueKind.Object &&
                       doc.RootElement.TryGetProperty("id", out _);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static string Trim(string text) => text.Length <= 500 ? text : text[..500] + "…";

        private void Kill()
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { }
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.StandardInput.Close();
                    if (!_process.WaitForExit(1000)) Kill();
                }
            }
            catch
            {
                Kill();
            }
            finally
            {
                _process.Dispose();
            }
        }
    }
}
