using OpenLoop.Service.Plugins;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>服务型插件：从子进程 stdout 认地址，provider 用 auto 接上去。</summary>
    public class LocalServiceTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "openloop-svc-" + Guid.NewGuid().ToString("N"));
        // 随机插件 id 与端口：固定的 id/端口会撞上 %APPDATA%\OpenLoop\plugins 里同 id 的真实插件
        // （它自己的 service.lock 拒绝第二个实例），测试就间歇性起不来。
        private readonly string _pluginId = "loctest" + Guid.NewGuid().ToString("N")[..8];
        private readonly int _port = Random.Shared.Next(40000, 60000);

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
$"console.log('API: http://127.0.0.1:{_port}/v1'); setInterval(() => {{}}, 1000);"
                ],
                TimeoutMs = 15000
            };

            string url = LocalServices.BaseUrl(_pluginId, spec, _root);
            Assert.Equal($"http://127.0.0.1:{_port}", url);

            // 同一个插件只起一份
            Assert.Equal(url, LocalServices.BaseUrl(_pluginId, spec, _root));
        }

        [Fact]
        public void Provider_with_auto_url_uses_the_running_service()
        {
            if (!HasNode()) return;
            string dir = Path.Combine(_root, "ws", ".openloop", "plugins", _pluginId);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "plugin.json"), $$"""
            {
              "id": "{{_pluginId}}",
              "name": "OFM",
              "service": {
                "command": "node",
                "args": ["-e", "console.log('API: http://127.0.0.1:{{_port}}/v1'); setInterval(() => {}, 1000);"],
                "timeoutMs": 15000
              },
              "contributions": {
                "providers": [{ "id": "{{_pluginId}}", "name": "OFM", "baseUrl": "auto", "models": ["kilo-auto/free"], "modelPrefix": "{{_pluginId}}/" }]
              }
            }
            """);

            using var catalog = PluginCatalog.Load(Path.Combine(_root, "ws"));
            var groups = catalog.ProviderGroups();

            Assert.Contains(groups, g => g.Name == "OFM" && g.Models.Contains($"{_pluginId}/kilo-auto/free"));
            Assert.True(catalog.TryProvider($"{_pluginId}/kilo-auto/free", out var provider));
            Assert.Equal($"http://127.0.0.1:{_port}/v1", provider.BaseUrl);
            Assert.Equal("kilo-auto/free", provider.WireModel($"{_pluginId}/kilo-auto/free"));
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
