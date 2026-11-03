using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using OpenLoop.Core.Tools;
using OpenLoop.Service.Plugins;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>
    /// MCP 客户端只走 Streamable HTTP：initialize → tools/list → tools/call。
    /// 这里用最小 JSON-RPC 桩验「发现 + 调用 + 带密钥」，不联网。
    /// </summary>
    public class McpClientTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "openloop-mcp-" + Guid.NewGuid().ToString("N"));
        private readonly string _workspace;
        private readonly StubMcp _stub = new();

        public McpClientTests()
        {
            _workspace = Path.Combine(_root, "ws");
            string dir = Path.Combine(_workspace, ".openloop", "plugins", "puppet-mcp");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "plugin.json"), $$"""
            {
              "id": "puppet-mcp",
              "mcp": { "url": "{{_stub.Url}}", "keyEnv": "OPENLOOP_MCP_KEY", "timeoutMs": 5000 }
            }
            """);
        }

        public void Dispose()
        {
            _stub.Dispose();
            try { Directory.Delete(_root, true); } catch { }
        }

        [Fact]
        public async Task Mcp_tools_are_discovered_and_called_with_the_key()
        {
            Environment.SetEnvironmentVariable("OPENLOOP_MCP_KEY", "mcp-key");
            try
            {
                using var catalog = PluginCatalog.Load(_workspace);
                var tools = catalog.Tools().ToList();
                var tool = Assert.Single(tools.OfType<McpTool>());

                Assert.Equal("puppet_get", tool.Name);
                Assert.Equal("read one property", tool.Description);
                Assert.Equal("instance: string，界面实例; path: string", tool.ParamsSchema);
                Assert.False(tool.IsReadOnly);

                var result = await tool.ExecuteAsync(new ToolArgs(new Dictionary<string, object>
                {
                    ["instance"] = "MainForm",
                    ["path"] = "Text"
                }), CancellationToken.None);

                Assert.True(result.Ok, result.Error);
                Assert.Equal("value=42", result.Data);
                Assert.Equal("Bearer mcp-key", _stub.LastAuthorization);
                Assert.Contains("puppet_get", _stub.LastBody);
            }
            finally
            {
                Environment.SetEnvironmentVariable("OPENLOOP_MCP_KEY", null);
            }
        }

        [Fact]
        public void Unreachable_mcp_server_yields_no_tools_and_no_crash()
        {
            string ws = Path.Combine(_root, "dead-ws");
            string dir = Path.Combine(ws, ".openloop", "plugins", "dead");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "plugin.json"),
                """{"id":"dead","mcp":{"url":"http://127.0.0.1:9/mcp","timeoutMs":1000}}""");

            using var catalog = PluginCatalog.Load(ws);
            Assert.Contains(catalog.Plugins, p => p.Id == "dead");
            Assert.Empty(catalog.Tools());
        }

        /// <summary>最小 MCP 服务端：一个工具 + 固定返回文本，记录密钥与最后一次请求体。</summary>
        private sealed class StubMcp : IDisposable
        {
            private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
            private readonly CancellationTokenSource _cts = new();

            public StubMcp()
            {
                _listener.Start();
                Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/mcp";
                _ = Task.Run(AcceptAsync);
            }

            public string Url { get; }
            public string LastAuthorization { get; private set; } = "";
            public string LastBody { get; private set; } = "";

            private async Task AcceptAsync()
            {
                while (!_cts.IsCancellationRequested)
                {
                    TcpClient client;
                    try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
                    catch { return; }
                    _ = Task.Run(() => HandleAsync(client));
                }
            }

            private async Task HandleAsync(TcpClient client)
            {
                using (client)
                {
                    var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);

                    await reader.ReadLineAsync();
                    int length = 0;
                    string line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(line[(line.IndexOf(':') + 1)..].Trim(), out length);
                        else if (line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                            LastAuthorization = line[(line.IndexOf(':') + 1)..].Trim();
                    }

                    var buffer = new char[length];
                    int read = 0;
                    while (read < length)
                    {
                        int chunk = await reader.ReadAsync(buffer.AsMemory(read, length - read));
                        if (chunk == 0) break;
                        read += chunk;
                    }
                    LastBody = new string(buffer, 0, read);

                    using var request = JsonDocument.Parse(LastBody);
                    var root = request.RootElement;
                    string method = root.GetProperty("method").GetString();
                    string id = root.GetProperty("id").GetRawText();

                    string result = method switch
                    {
                        "initialize" => """{"protocolVersion":"2025-06-18","capabilities":{"tools":{}},"serverInfo":{"name":"stub"}}""",
                        "tools/list" => """
                            {"tools":[{"name":"puppet_get","description":"read one property",
                              "inputSchema":{"type":"object","properties":{
                                "instance":{"type":"string","description":"界面实例"},
                                "path":{"type":"string"}}}}]}
                            """,
                        _ => """{"content":[{"type":"text","text":"value=42"}],"isError":false}"""
                    };

                    byte[] body = Encoding.UTF8.GetBytes(
                        $$"""{"jsonrpc":"2.0","id":{{id}},"result":{{result}}}""");
                    byte[] head = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " +
                        body.Length + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(head);
                    await stream.WriteAsync(body);
                }
            }

            public void Dispose()
            {
                _cts.Cancel();
                _listener.Stop();
                _cts.Dispose();
            }
        }
    }
}
