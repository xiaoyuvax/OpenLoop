using System.Net;
using System.Net.Sockets;
using System.Text;
using OpenLoop.Core.Messages;
using OpenLoop.Service.Config;
using OpenLoop.Service.Models;
using OpenLoop.Service.Plugins;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>
    /// 提供商贡献的真链路：清单里的 baseUrl/models/keyEnv → Reasoners.Create → 一次补全。
    /// 用本机回环上的假 OpenAI 服务，不碰任何外部模型。
    /// </summary>
    public class PluginProviderTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "openloop-provider-" + Guid.NewGuid().ToString("N"));
        private readonly string _workspace;
        private readonly string _global;
        private readonly StubOpenAi _stub = new();

        public PluginProviderTests()
        {
            _workspace = Path.Combine(_root, "ws");
            _global = Path.Combine(_root, "global");
            Directory.CreateDirectory(_global);
            Directory.CreateDirectory(Path.Combine(_workspace, ".openloop", "plugins", "stub"));
            File.WriteAllText(Path.Combine(_workspace, ".openloop", "plugins", "stub", "plugin.json"), $$"""
            {
              "id": "stub",
              "entry": { "command": "noop" },
              "contributions": {
                "providers": [{
                  "id": "stub",
                  "name": "stub",
                  "baseUrl": "{{_stub.BaseUrl}}",
                  "models": ["stub-model"],
                  "keyEnv": "OPENLOOP_STUB_KEY"
                }]
              }
            }
            """);
        }

        public void Dispose()
        {
            _stub.Dispose();
            try { Directory.Delete(_root, true); } catch { }
        }

        [Fact]
        public async Task Plugin_provider_is_used_with_its_model_and_key()
        {
            Environment.SetEnvironmentVariable("OPENLOOP_STUB_KEY", "sk-test");
            try
            {
                var reasoner = Reasoners.Create(new AppSettings(), "stub-model", "", _workspace);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                var result = await reasoner.CompleteAsync(
                    [new LlmMessage("user", "ping")], cts.Token);

                Assert.Equal("pong", result.Content);
                Assert.Contains("stub-model", _stub.LastBody);
                Assert.Equal("Bearer sk-test", _stub.LastAuthorization);
            }
            finally
            {
                Environment.SetEnvironmentVariable("OPENLOOP_STUB_KEY", null);
            }
        }

        /// <summary>模型清单来自 /v1/models，密钥来自本地文件：dsh 网关那种形态。</summary>
        [Fact]
        public async Task Provider_models_can_come_from_modelsUrl_and_key_from_file()
        {
            string keyPath = Path.Combine(_root, "gateway-key");
            File.WriteAllText(keyPath, "file-key\n");
            File.WriteAllText(Path.Combine(_workspace, ".openloop", "plugins", "stub", "plugin.json"), $$"""
            {
              "id": "stub",
              "contributions": {
                "providers": [{
                  "id": "stub",
                  "baseUrl": "{{_stub.BaseUrl}}",
                  "modelsUrl": "{{_stub.BaseUrl}}/models",
                  "keyFile": "{{keyPath.Replace("\\", "/")}}"
                }]
              }
            }
            """);

            using (var catalog = PluginCatalog.Load(_workspace, _global))
            {
                Assert.True(catalog.TryProvider("our-free-model/stub-model", out _));
                Assert.Equal(["our-free-model/stub-model"], catalog.Models());
            }

            var reasoner = Reasoners.Create(new AppSettings(), "our-free-model/stub-model", "", _workspace);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await reasoner.CompleteAsync([new LlmMessage("user", "ping")], cts.Token);

            Assert.Equal("pong", result.Content);
            Assert.Equal("Bearer file-key", _stub.LastAuthorization);
        }

        [Fact]
        public void Provider_only_manifest_needs_no_entry_command()
        {
            string dir = Path.Combine(_workspace, ".openloop", "plugins", "noentry");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "plugin.json"), """
            {"id":"noentry","contributions":{"providers":[{"id":"p","baseUrl":"http://127.0.0.1:9/v1","models":["m"]}]}}
            """);

            using var catalog = PluginCatalog.Load(_workspace, _global);
            Assert.Contains(catalog.Plugins, p => p.Id == "noentry");
            Assert.True(catalog.TryProvider("m", out _));
        }

        /// <summary>只认一次请求的最小 OpenAI 兼容端点（HTTP/1.1 + Content-Length）。</summary>
        private sealed class StubOpenAi : IDisposable
        {
            private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
            private readonly CancellationTokenSource _cts = new();

            public StubOpenAi()
            {
                _listener.Start();
                BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/v1";
                _ = Task.Run(AcceptAsync);
            }

            public string BaseUrl { get; }
            public string LastBody { get; private set; } = "";
            public string LastAuthorization { get; private set; } = "";
            public string LastMethod { get; private set; } = "";

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

                    int length = 0;
                    string line;
                    LastMethod = (await reader.ReadLineAsync() ?? "").Split(' ')[0];
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

                    byte[] body = Encoding.UTF8.GetBytes(LastMethod == "GET"
                        ? """{"object":"list","data":[{"id":"our-free-model/stub-model"}]}"""
                        : """{"choices":[{"message":{"content":"pong"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""");
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
