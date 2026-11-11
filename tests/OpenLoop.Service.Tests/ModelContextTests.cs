using OpenLoop.Service.Config;
using OpenLoop.Service.Models;
using Xunit;

namespace OpenLoop.Service.Tests
{
    public class ModelContextTests
    {
        [Fact]
        public void KnownTableFillsModelsWithoutProviderMetadata()
        {
            Assert.Equal(1_000_000, KnownContext.Lookup("deepseek-flash-1017"));
            Assert.Equal(1_000_000, KnownContext.Lookup("ofm/deepseek-flash-1017"));
            Assert.Equal(1_000_000, KnownContext.Lookup("kimi-k3[1M]"));
            Assert.Equal(131072, KnownContext.Lookup("some-model[128k]"));
            Assert.Equal(0, KnownContext.Lookup("unknown-model"));
        }

        [Fact]
        public void UserOverrideBeatsCatalogAndTable()
        {
            var settings = new AppSettings
            {
                ModelProps = { ["deepseek-flash-1017"] = new ModelProps { ContextLimit = 200_000 } }
            };

            Assert.Equal(200_000, ModelCatalog.ContextLimit("deepseek-flash-1017", settings));
            Assert.Equal(1_000_000, ModelCatalog.ContextLimit("deepseek-flash-1017", null));
        }

        [Fact]
        public void UnknownModelFallsBackToDefault()
        {
            Assert.Equal(OpenLoop.Core.Agent.AgentContext.DefaultContextLimit,
                ModelCatalog.ContextLimit("no-such-model", new AppSettings()));
        }

        [Fact]
        public void AliasIsReadCaseInsensitively()
        {
            var settings = new AppSettings
            {
                ModelProps = { ["DeepSeek-Flash-1017"] = new ModelProps { ContextLimit = 64_000 } }
            };

            Assert.Equal(64_000, settings.PropsFor("deepseek-flash-1017")?.ContextLimit);
        }

        [Fact]
        public void ModelPropsSurviveJsonRoundTrip()
        {
            var settings = new AppSettings
            {
                ModelProps = { ["deepseek-flash-1017"] = new ModelProps { Name = "DS 1017", ContextLimit = 1_000_000 } }
            };

            string json = System.Text.Json.JsonSerializer.Serialize(settings);
            var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.Equal("DS 1017", back.PropsFor("deepseek-flash-1017")?.Name);
            Assert.Equal(1_000_000, back.PropsFor("deepseek-flash-1017")?.ContextLimit);
        }
    }
}
