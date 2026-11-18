using System.Text.Json;
using OpenLoop.Service.Config;
using OpenLoop.Service.Models;
using OpenLoop.Service.Plugins;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>上下文窗口来源：插件清单声明与服务商 /models 报值。</summary>
    public class ModelWindowsTests
    {
        [Fact]
        public void ReportedWindowMatchesPrefixedModelId()
        {
            ModelWindows.Report("win-model", 1_000_000);

            Assert.Equal(1_000_000, ModelCatalog.ContextLimit("win-model", new AppSettings()));
            Assert.Equal(1_000_000, ModelCatalog.ContextLimit("a2a/win-model", new AppSettings()));
        }

        [Fact]
        public void DeclaredWindowCoversWholeProviderAndIsRevocable()
        {
            ModelWindows.Report("mw-a", 300_000);
            ModelWindows.Declare("mw/", ["mw-a", "mw-b"], 512_000);

            Assert.Equal(512_000, ModelCatalog.ContextLimit("mw/mw-a", new AppSettings()));
            Assert.Equal(512_000, ModelCatalog.ContextLimit("mw/mw-b", new AppSettings()));

            ModelWindows.Declare("mw/", ["mw-a", "mw-b"], 0);
            Assert.Equal(300_000, ModelCatalog.ContextLimit("mw/mw-a", new AppSettings()));
            Assert.Equal(OpenLoop.Core.Agent.AgentContext.DefaultContextLimit,
                ModelCatalog.ContextLimit("mw/mw-b", new AppSettings()));
        }

        [Fact]
        public void PluginJsonContextWindowReachesTheCatalog()
        {
            string root = Path.Combine(Path.GetTempPath(), "openloop-windows-" + Guid.NewGuid().ToString("N"));
            string dir = Path.Combine(root, "declared");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "plugin.json"), """
                {
                  "id": "declared",
                  "contributions": {
                    "providers": [{
                      "id": "declared", "baseUrl": "http://127.0.0.1:9/v1",
                      "modelPrefix": "dw/", "models": ["dw-static"], "contextWindow": 1000000
                    }]
                  }
                }
                """);

            try
            {
                using var catalog = PluginCatalog.Load(root, root);
                Assert.Single(catalog.Plugins);
                Assert.Equal(1_000_000, ModelCatalog.ContextLimit("dw/dw-static", new AppSettings()));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void ReadAcceptsCommonWindowFieldNames()
        {
            using var doc = JsonDocument.Parse("""{"id":"x","max_input_tokens":960000}""");
            Assert.Equal(960_000, ModelWindows.Read(doc.RootElement));

            using var contextLength = JsonDocument.Parse("""{"id":"y","context_length":"262144"}""");
            Assert.Equal(262_144, ModelWindows.Read(contextLength.RootElement));

            using var none = JsonDocument.Parse("""{"id":"z"}""");
            Assert.Equal(0, ModelWindows.Read(none.RootElement));
        }
    }
}
