using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Tools;
using OpenLoop.Service.Logging;

namespace OpenLoop.Service.Plugins
{
    /// <summary>
    /// MCP 客户端：工具由 <c>tools/list</c> 发现、<c>tools/call</c> 调用。
    /// 传输二选一——Streamable HTTP（POST JSON-RPC，无会话头、无 SSE 长连接，SSE 响应取最后一帧），
    /// 或本地 stdio 子进程（每行一条 JSON-RPC，stderr 当日志）。
    /// </summary>
    public sealed class McpClient : IDisposable
    {
        private static readonly HttpClient Http = new();

        private readonly string _url;
        private readonly string _key;
        private readonly string _command;
        private readonly List<string> _args;
        private readonly string _cwd;
        private readonly TimeSpan _timeout;
        private readonly SemaphoreSlim _stdioGate = new(1, 1);
        private int _nextId;
        private bool _initialized;

        private Process _process;
        private StreamWriter _stdin;
        private StreamReader _stdout;

        public McpClient(PluginMcpContribution config, string workspace = null)
        {
            _url = config?.Url?.Trim() ?? "";
            _key = config?.ResolveKey() ?? "";
            _command = string.IsNullOrWhiteSpace(_url) ? config?.Command?.Trim() ?? "" : "";
            _args = config?.Args ?? [];
            _cwd = workspace;
            _timeout = TimeSpan.FromMilliseconds(config?.TimeoutMs is > 0 and var ms ? ms : 60000);
        }

        public string Url => string.IsNullOrWhiteSpace(_url) ? _command : _url;

        public void Dispose() => DropStdio();

        /// <summary>stdio 会话已经不同步：杀掉子进程，下次请求重新起。</summary>
        private void DropStdio()
        {
            try { _process?.Kill(true); } catch { }
            try { _process?.Dispose(); } catch { }
            _process = null;
            _stdin = null;
            _stdout = null;
        }

        /// <summary>拉一次工具清单；失败返回空并记一条日志，不抛。</summary>
        public IReadOnlyList<McpToolSpec> ListTools()
        {
            if (string.IsNullOrWhiteSpace(_url) && string.IsNullOrWhiteSpace(_command)) return [];
            try
            {
                Initialize();
                using var doc = Post("tools/list", new { });
                if (!doc.RootElement.TryGetProperty("result", out var result)
                    || !result.TryGetProperty("tools", out var tools)
                    || tools.ValueKind != JsonValueKind.Array)
                    return [];

                var specs = new List<McpToolSpec>();
                foreach (var tool in tools.EnumerateArray())
                {
                    string name = Str(tool, "name");
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    specs.Add(new McpToolSpec(
                        name,
                        Str(tool, "description"),
                        DescribeParams(tool.TryGetProperty("inputSchema", out var schema) ? schema : default)));
                }
                return specs;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"MCP 工具清单拉取失败：{Url} {ex.Message}");
                return [];
            }
        }

        public async Task<AgentToolResult> CallAsync(
            string tool, IReadOnlyDictionary<string, object> args, CancellationToken ct)
        {
            try
            {
                Initialize();
                using var doc = await PostAsync("tools/call",
                    new { name = tool, arguments = args ?? new Dictionary<string, object>() }, ct);

                if (!doc.RootElement.TryGetProperty("result", out var result))
                    return AgentToolResult.Failure(ErrorOf(doc.RootElement) ?? "MCP 没有返回 result");

                string text = ReadContent(result);
                bool isError = result.TryGetProperty("isError", out var flag)
                               && flag.ValueKind == JsonValueKind.True;
                return isError ? AgentToolResult.Failure(text) : AgentToolResult.Success(text);
            }
            catch (Exception ex)
            {
                return AgentToolResult.Failure($"MCP 调用失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 有些服务端要求先 initialize 才认 tools/list；Puppet 不要求，失败也继续。
        /// </summary>
        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                using var _ = Post("initialize", new
                {
                    protocolVersion = "2025-06-18",
                    capabilities = new { },
                    clientInfo = new { name = "OpenLoop", version = "1.0" }
                });
            }
            catch
            {
                // 初始化不是所有服务端的硬要求，真正的错误留给 tools/list 报
            }
        }

        private JsonDocument Post(string method, object parameters)
            => PostAsync(method, parameters, CancellationToken.None).GetAwaiter().GetResult();

        private async Task<JsonDocument> PostAsync(string method, object parameters, CancellationToken ct)
        {
            int id = Interlocked.Increment(ref _nextId);
            string body = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters });

            var doc = string.IsNullOrWhiteSpace(_command)
                ? await PostHttpAsync(body, ct).ConfigureAwait(false)
                : await PostStdioAsync(id, body, ct).ConfigureAwait(false);

            if (doc.RootElement.TryGetProperty("error", out _))
            {
                string message = ErrorOf(doc.RootElement) ?? "JSON-RPC error";
                doc.Dispose();
                throw new InvalidOperationException(message);
            }
            return doc;
        }

        /// <summary>stdio：一行写请求，读到 id 对上的那一行；通知（无 id）与日志行丢弃。</summary>
        private async Task<JsonDocument> PostStdioAsync(int id, string body, CancellationToken ct)
        {
            await _stdioGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                StartStdio();

                // 注意：StreamWriter 的 *Async* 写入在匿名管道上不会真正到达子进程（实测），这里用同步写。
                _stdin.WriteLine(body);
                _stdin.Flush();

                while (true)
                {
                    var reading = _stdout.ReadLineAsync();
                    string line = _timeout > TimeSpan.Zero
                        ? await reading.WaitAsync(_timeout, ct).ConfigureAwait(false)
                        : await reading.WaitAsync(ct).ConfigureAwait(false);
                    if (line == null) throw new InvalidOperationException("MCP 子进程已退出");

                    line = line.Trim();
                    if (line.Length == 0 || line[0] != '{') continue;

                    JsonDocument doc;
                    try { doc = JsonDocument.Parse(line); }
                    catch (JsonException) { continue; }

                    if (!doc.RootElement.TryGetProperty("id", out var reply)
                        || reply.ValueKind != JsonValueKind.Number
                        || reply.GetInt32() != id)
                    {
                        doc.Dispose();
                        continue;
                    }
                    return doc;
                }
            }
            catch
            {
                DropStdio();
                throw;
            }
            finally
            {
                _stdioGate.Release();
            }
        }

        private void StartStdio()
        {
            if (_process != null) return;

            var startInfo = new ProcessStartInfo(_command)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                // 不能直接用 Encoding.UTF8：它会先往子进程 stdin 写 BOM，MCP 服务端解析首行 JSON 就失败
                StandardInputEncoding = new UTF8Encoding(false),
                CreateNoWindow = true
            };
            foreach (string arg in _args) startInfo.ArgumentList.Add(arg);
            if (!string.IsNullOrWhiteSpace(_cwd) && Directory.Exists(_cwd))
                startInfo.WorkingDirectory = _cwd;

            _process = Process.Start(startInfo)
                       ?? throw new InvalidOperationException($"MCP 子进程启动失败：{_command}");
            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;
            _process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data)) AppLog.Info($"MCP[{_command}] {e.Data}");
            };
            _process.BeginErrorReadLine();
        }

        private async Task<JsonDocument> PostHttpAsync(string body, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (!string.IsNullOrWhiteSpace(_key))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (_timeout > TimeSpan.Zero) cts.CancelAfter(_timeout);
            using var response = await Http.SendAsync(request, cts.Token).ConfigureAwait(false);
            string payload = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"HTTP {(int)response.StatusCode} {Trim(payload, 200)}");

            return JsonDocument.Parse(Unwrap(payload));
        }

        /// <summary>SSE 响应取最后一个 data: 帧；普通 JSON 原样返回。</summary>
        private static string Unwrap(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return "{}";
            string text = payload.TrimStart();
            if (text.StartsWith('{')) return text;

            string last = null;
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("data:", StringComparison.Ordinal))
                    last = trimmed[5..].Trim();
            }
            return string.IsNullOrWhiteSpace(last) ? "{}" : last;
        }

        private static string ErrorOf(JsonElement root)
            => root.TryGetProperty("error", out var error)
               && error.ValueKind == JsonValueKind.Object
               && error.TryGetProperty("message", out var message)
               && message.ValueKind == JsonValueKind.String
                ? message.GetString()
                : null;

        /// <summary>MCP 结果里 content 是「{type,text} 数组」，拼成一段文本给模型。</summary>
        private static string ReadContent(JsonElement result)
        {
            if (!result.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                return result.ValueKind == JsonValueKind.Object ? result.ToString() : "";

            var sb = new StringBuilder();
            foreach (var item in content.EnumerateArray())
            {
                if (item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    sb.AppendLine(text.GetString());
                else
                    sb.AppendLine(item.ToString());
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>把 JSON Schema 压成一行「名字: 类型，说明; …」，和内置工具的写法一致。</summary>
        private static string DescribeParams(JsonElement schema)
        {
            if (schema.ValueKind != JsonValueKind.Object
                || !schema.TryGetProperty("properties", out var properties)
                || properties.ValueKind != JsonValueKind.Object)
                return "";

            var parts = new List<string>();
            foreach (var property in properties.EnumerateObject())
            {
                string type = property.Value.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString()
                    : "any";
                string desc = Str(property.Value, "description");
                parts.Add(string.IsNullOrWhiteSpace(desc) ? $"{property.Name}: {type}" : $"{property.Name}: {type}，{desc}");
            }
            return Trim(string.Join("; ", parts), 600);
        }

        private static string Str(JsonElement node, string name)
            => node.ValueKind == JsonValueKind.Object
               && node.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : "";

        private static string Trim(string text, int max)
            => string.IsNullOrEmpty(text) || text.Length <= max ? text : text[..max] + "…";
    }

    public sealed record McpToolSpec(string Name, string Description, string Params);

    /// <summary>把一个 MCP 工具接进 OpenLoop 的工具集；只读性未知，一律按可写处理。</summary>
    public sealed class McpTool : ITool
    {
        private readonly McpClient _client;
        private readonly McpToolSpec _spec;

        public McpTool(McpClient client, McpToolSpec spec)
        {
            _client = client;
            _spec = spec;
        }

        public string Name => _spec.Name;
        public string Description => _spec.Description;
        public string ParamsSchema => _spec.Params;
        public bool IsReadOnly => false;

        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
            => _client.CallAsync(_spec.Name, args?.Raw, ct);
    }
}
