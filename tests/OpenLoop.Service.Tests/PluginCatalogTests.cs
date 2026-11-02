using OpenLoop.Core.Reasoning;
using OpenLoop.Service.Config;
using OpenLoop.Service.Models;
using OpenLoop.Service.Plugins;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>清单发现：工作区优先、无效清单跳过、提供商按模型名解析。</summary>
    public class PluginCatalogTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "openloop-plugins-" + Guid.NewGuid().ToString("N"));
        private readonly string _workspace;
        private readonly string _global;

        public PluginCatalogTests()
        {
            _workspace = Path.Combine(_root, "ws");
            _global = Path.Combine(_root, "global");
            Directory.CreateDirectory(_workspace);
            Directory.CreateDirectory(_global);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string Write(string root, string id, string json)
        {
            string dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "plugin.json"), json);
            return dir;
        }

        private static string Manifest(string id, string model = "local-model", string baseUrl = "http://127.0.0.1:9000/v1")
            => $$"""
            {
              "id": "{{id}}",
              "entry": { "command": "node", "args": ["plugin.mjs"] },
              "contributions": {
                "tools": [{ "name": "Echo", "description": "echo", "params": "text: string" }],
                "providers": [{ "id": "{{id}}", "name": "n", "baseUrl": "{{baseUrl}}", "models": ["{{model}}"] }]
              }
            }
            """;

        private PluginCatalog Load()
            => PluginCatalog.Load(_workspace, _global);

        [Fact]
        public void Missing_folders_mean_no_plugins()
        {
            using var catalog = Load();

            Assert.Empty(catalog.Plugins);
            Assert.Empty(catalog.Tools());
            Assert.False(catalog.TryProvider("local-model", out _));
        }

        [Fact]
        public void Workspace_and_global_manifests_are_discovered()
        {
            Write(Path.Combine(_workspace, ".openloop", "plugins"), "ws", Manifest("ws"));
            Write(_global, "global", Manifest("global", "other-model"));

            using var catalog = Load();

            Assert.Equal(2, catalog.Plugins.Count);
            Assert.Equal(2, catalog.Tools().Count());
        }

        [Fact]
        public void Workspace_plugin_wins_on_a_duplicate_id()
        {
            Write(Path.Combine(_workspace, ".openloop", "plugins"), "dup", Manifest("dup", baseUrl: "http://ws/v1"));
            Write(_global, "dup", Manifest("dup", baseUrl: "http://global/v1"));

            using var catalog = Load();

            Assert.Single(catalog.Plugins);
            Assert.True(catalog.TryProvider("local-model", out var provider));
            Assert.Equal("http://ws/v1", provider.BaseUrl);
        }

        [Fact]
        public void Invalid_manifest_is_skipped()
        {
            Write(_global, "broken", """{"id":"broken"}""");

            using var catalog = Load();

            Assert.Empty(catalog.Plugins);
        }

        [Fact]
        public void Provider_lookup_is_case_insensitive_and_wires_the_reasoner()
        {
            Write(_global, "p", Manifest("p", model: "Local-Model"));

            using var catalog = PluginCatalog.Load(_workspace, _global);
            Assert.True(catalog.TryProvider("local-model", out var provider));
            Assert.Equal("Local-Model", provider.Models[0]);

            var settings = new AppSettings();
            var reasoner = Reasoners.Create(settings, "LOCAL-MODEL", "", _workspace);
            Assert.IsType<OpenAiCompatibleReasoner>(reasoner);
        }
    }
}
