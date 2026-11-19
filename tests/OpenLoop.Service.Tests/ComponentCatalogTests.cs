using OpenLoop.Service.Source;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>组件清单（components.json）：解析、内置兜底与依赖顺序。</summary>
    public class ComponentCatalogTests
    {
        [Fact]
        public void Missing_file_falls_back_to_official_components()
        {
            var list = ComponentCatalog.Load(Path.Combine(
                Path.GetTempPath(), "ol-nocatalog-" + Guid.NewGuid().ToString("N"), ComponentCatalog.FileName));

            Assert.Contains(list, c => c.Id == "puppetcore" && c.Url == SourceSync.PuppetCoreUrl);
            Assert.Contains(list, c => c.Id == "chatflow" && c.Url == SourceSync.ChatFlowUrl);
            // fctb 是临时组件：没有清单时不硬编码兜底（ChatFlow 走包引用）
            Assert.DoesNotContain(list, c => c.Id == "fctb");
        }

        [Fact]
        public void Unparsable_file_falls_back_to_official_components()
        {
            var list = ComponentCatalog.Merge(ComponentCatalog.Parse("not json"));

            Assert.Contains(list, c => c.Id == "puppetcore");
            Assert.Contains(list, c => c.Id == "chatflow");
        }

        [Fact]
        public void Declared_fields_override_defaults_and_missing_ones_are_filled()
        {
            var list = ComponentCatalog.Merge(ComponentCatalog.Parse("""
                { "components": [ { "id": "chatflow", "url": "https://github.com/me/ChatFlow.git" } ] }
                """));

            var chatflow = list.Single(c => c.Id == "chatflow");
            Assert.Equal("https://github.com/me/ChatFlow.git", chatflow.Url);
            Assert.Equal("ChatFlow/ChatFlow.csproj", chatflow.VersionFile);
            Assert.Contains(chatflow.Consumers, c => c.Repo == "openloop");
            // 清单漏掉的内置组件按官方默认项补回，不会因为少写一条就消失
            Assert.Equal(SourceSync.PuppetCoreUrl, list.Single(c => c.Id == "puppetcore").Url);
        }

        [Fact]
        public void Components_are_ordered_before_the_components_that_reference_them()
        {
            var list = ComponentCatalog.Merge(ComponentCatalog.Parse("""
                { "components": [
                    { "id": "chatflow", "url": "https://github.com/xiaoyuvax/ChatFlow.git" },
                    { "id": "fctb", "url": "https://github.com/me/FastColoredTextBox.git",
                      "dir": "FastColoredTextBox",
                      "consumers": [ { "repo": "ChatFlow", "project": "ChatFlow" } ] } ] }
                """)).ToList();

            Assert.True(list.FindIndex(c => c.Id == "fctb") < list.FindIndex(c => c.Id == "chatflow"));
        }

        [Fact]
        public void Package_fallback_fields_are_read_from_the_catalog()
        {
            var list = ComponentCatalog.Merge(ComponentCatalog.Parse("""
                { "components": [ { "id": "fctb", "packageId": "Vax-FCTB", "packageVersion": "2.19.0.233" } ] }
                """));

            var fctb = list.Single(c => c.Id == "fctb");
            Assert.Equal("Vax-FCTB", fctb.PackageId);
            Assert.Equal("2.19.0.233", fctb.PackageVersion);
        }

        [Fact]
        public void Project_reference_is_recognised_by_package_id_too()
        {
            // 项目引用的库名取 PackageId（Vax-FCTB），不是程序集名（FastColoredTextBox）
            var fctb = new ComponentSpec
            {
                Id = "fctb", Assembly = "FastColoredTextBox", PackageId = "Vax-FCTB"
            };

            Assert.True(VersionScan.IsProjectAssembly(fctb, "FastColoredTextBox"));
            Assert.True(VersionScan.IsProjectAssembly(fctb, "Vax-FCTB"));
            Assert.False(VersionScan.IsProjectAssembly(fctb, "ChatFlow"));
        }

        [Fact]
        public void SameUrl_ignores_case_trailing_slash_and_git_suffix()
        {
            Assert.True(ComponentCatalog.SameUrl(
                "https://github.com/xiaoyuvax/ChatFlow.git", "https://github.com/xiaoyuvax/chatflow/"));
            Assert.False(ComponentCatalog.SameUrl("https://github.com/me/ChatFlow.git", SourceSync.ChatFlowUrl));
            Assert.False(ComponentCatalog.SameUrl("", ""));
        }
    }
}
