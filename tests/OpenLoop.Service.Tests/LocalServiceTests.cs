using OpenLoop.Service.Plugins;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>服务型插件：从子进程 stdout 认地址，provider 用 auto 接上去。</summary>
    public class LocalServiceTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "openloop-svc-" + Guid.NewGuid().ToString("N"));

        public LocalServiceTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private static bool HasNode()
        {
            string name = OperatingSystem.IsWindows() ? "node.exe" : "node";
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                if (File.Exists(Path.Combine(dir.Trim('"'), name))) return true;
            }
            return false;
        }

        [Fact]
        public void Base_url_comes_from_the_child_stdout()
        {
            if (!HasNode()) return;
            var spec = new PluginServiceContribution
            {
                Command = OperatingSystem.IsWindows() ? "node.exe" : "node",
                Args =
                [
                    "-e",
                    "console.log('API: http://127.0.0.1:45678/v1'); setInterval(() => {}, 1000);"
                ],
                TimeoutMs = 15000
            };

            string url = LocalServices.BaseUrl("svc-test", spec, _root);
            Assert.Equal("http://127.0.0.1:45678", url);

            // 同一个插件只起一份
            Assert.Equal(url, LocalServices.BaseUrl("svc-test", spec, _root));
        }

        [Fact]
        public void Provider_with_auto_url_uses_the_running_service()
        {
            if (!HasNode()) return;
            string dir = Path.Combine(_root, "ws", ".openloop", "plugins", "ofm");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "plugin.json"), """
            {
              "id": "ofm",
              "name": "OFM",
              "service": {
                "command": "node",
                "args": ["-e", "console.log('API: http://127.0.0.1:45679/v1'); setInterval(() => {}, 1000);"],
                "timeoutMs": 15000
              },
              "contributions": {
                "providers": [{ "id": "ofm", "name": "OFM", "baseUrl": "auto", "models": ["kilo-auto/free"], "modelPrefix": "ofm/" }]
              }
            }
            """);

            using var catalog = PluginCatalog.Load(Path.Combine(_root, "ws"));
            var groups = catalog.ProviderGroups();

            Assert.Contains(groups, g => g.Name == "OFM" && g.Models.Contains("ofm/kilo-auto/free"));
            Assert.True(catalog.TryProvider("ofm/kilo-auto/free", out var provider));
            Assert.Equal("http://127.0.0.1:45679/v1", provider.BaseUrl);
            Assert.Equal("kilo-auto/free", provider.WireModel("ofm/kilo-auto/free"));
        }

        [Fact]
        public void Key_can_come_from_a_json_field()
        {
            string file = Path.Combine(_root, "settings.json");
            File.WriteAllText(file, """{ "forwardKey": "k-123", "other": 1 }""");

            Assert.Equal("k-123", PluginSecret.Resolve(null, file, "forwardKey"));
            Assert.Equal("", PluginSecret.Resolve(null, file, "missing"));
            Assert.StartsWith("{", PluginSecret.Resolve(null, file));
        }
    }
}
