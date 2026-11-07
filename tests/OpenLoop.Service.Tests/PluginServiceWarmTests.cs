using System.Diagnostics;
using OpenLoop.Service.Plugins;
using OpenLoop.Service.Runtime;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>
    /// 服务型插件的地址解析不能阻塞调用方：界面刷新（提供商/模型下拉）走的是这条路径，
    /// 一旦在这里等 stdout 握手，UI 线程会僵住整个 timeoutMs（实测最长 30s）。
    /// </summary>
    public class PluginServiceWarmTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "openloop-svc-" + Guid.NewGuid().ToString("N"));
        private readonly string _id = "svctest" + Guid.NewGuid().ToString("N")[..6];

        public PluginServiceWarmTests()
        {
            string dir = Path.Combine(_root, ".openloop", "plugins", _id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "plugin.json"), $$"""
            {
              "id": "{{_id}}",
              "name": "FakeService",
              "version": "1.0.0",
              "service": {
                "command": "node",
                "args": ["{root}/svc.mjs"],
                "urlPattern": "(https?://127\\.0\\.0\\.1:\\d+)",
                "timeoutMs": 15000
              },
              "contributions": {
                "providers": [
                  {
                    "id": "fakesvc",
                    "name": "FakeSvc",
                    "baseUrl": "auto",
                    "modelsUrl": "auto/v1/models",
                    "modelPrefix": "fakesvc/"
                  }
                ]
              }
            }
            """);
            File.WriteAllText(Path.Combine(dir, "svc.mjs"), Service);
        }

        public void Dispose()
        {
            try
            {
                string handshake = Path.Combine(AppPaths.RuntimeDir, "plugin-" + _id + ".json");
                if (File.Exists(handshake))
                {
                    string json = File.ReadAllText(handshake);
                    var match = System.Text.RegularExpressions.Regex.Match(json, "\"pid\"\\s*:\\s*(\\d+)");
                    if (match.Success)
                    {
                        try { Process.GetProcessById(int.Parse(match.Groups[1].Value)).Kill(true); } catch { }
                    }
                    try { File.Delete(handshake); } catch { }
                }
            }
            catch
            {
            }
            try { Directory.Delete(_root, true); } catch { }
        }

        [Fact]
        public void Listing_does_not_wait_for_the_service_to_print_its_url()
        {
            if (!HasNode()) return;

            using var catalog = PluginCatalog.Load(_root);
            var watch = Stopwatch.StartNew();
            var first = catalog.ProviderGroups();
            watch.Stop();

            Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(500),
                $"列模型时等了 {watch.ElapsedMilliseconds}ms（服务要 800ms 才打印地址）");
            Assert.DoesNotContain(first, g => g.Name == "FakeSvc");

            // 后台预热完成后应该能列出来，不用再等谁去拉起
            var ready = Stopwatch.StartNew();
            IReadOnlyList<(string Name, List<string> Models)> groups = [];
            while (ready.Elapsed < TimeSpan.FromSeconds(15))
            {
                using var next = PluginCatalog.Load(_root);
                groups = next.ProviderGroups();
                if (groups.Any(g => g.Name == "FakeSvc")) break;
                Thread.Sleep(200);
            }

            var group = Assert.Single(groups, g => g.Name == "FakeSvc");
            Assert.Contains("fakesvc/m1", group.Models);
        }

        private static bool HasNode()
        {
            string name = OperatingSystem.IsWindows() ? "node.exe" : "node";
            return (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator)
                .Any(dir => !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir.Trim('"'), name)));
        }

        private const string Service = """
        import http from 'node:http';

        setTimeout(() => {
          const server = http.createServer((req, res) => {
            if (req.url.endsWith('/v1/models')) {
              res.setHeader('content-type', 'application/json');
              res.end(JSON.stringify({ data: [{ id: 'm1' }] }));
              return;
            }
            res.statusCode = 404;
            res.end('{}');
          });
          server.listen(0, '127.0.0.1', () => {
            console.log('API: http://127.0.0.1:' + server.address().port + '/v1');
          });
          setTimeout(() => process.exit(0), 20000);
        }, 800);
        """;
    }
}
