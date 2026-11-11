using OpenLoop.Service.Source;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>源码版本比较与 Loop 的读写边界（章程 §2/§3）。</summary>
    public class SourceSyncTests
    {
        [Theory]
        [InlineData("0.2.0", "0.1.0", SourceState.Newer)]
        [InlineData("0.1.0", "0.1.0", SourceState.UpToDate)]
        [InlineData("0.0.9", "0.1.0", SourceState.Older)]
        [InlineData("", "0.1.0", SourceState.Unknown)]
        public void Classify_compares_versions(string source, string running, SourceState expected)
            => Assert.Equal(expected, SourceSync.Classify(source, running));

        [Fact]
        public void ParseVersion_reads_the_version_tag()
            => Assert.Equal("0.3.1", SourceSync.ParseVersion("<Project><Version>0.3.1</Version></Project>"));

        [Fact]
        public void ParseVersion_strips_build_metadata_and_prerelease()
        {
            Assert.Equal("0.1.0", SourceSync.ParseVersion("<Version>0.1.0+0636bd3</Version>"));
            Assert.Equal("1.2.3", SourceSync.ParseVersion("1.2.3-preview.4"));
        }

        [Theory]
        [InlineData("0.1.2", "0.1.0", true, "0.1.2")]   // 本地工作树：本地说了算（远端落后不算过旧）
        [InlineData("", "0.1.0", true, "0.1.0")]        // 本地没版本号才回退远端
        [InlineData("0.1.2", "0.1.5", false, "0.1.5")]  // 安装布局：比远端，看有没有新源码
        [InlineData("0.1.2", "", false, "0.1.2")]       // 远端读不到才回退本地
        public void PickSourceVersion_uses_the_local_working_tree_when_building_from_it(
            string local, string remote, bool localCheckout, string expected)
            => Assert.Equal(expected, SourceSync.PickSourceVersion(local, remote, localCheckout));

        [Theory]
        [InlineData("0.1.2", "0.1.2.0")]
        [InlineData("0.1.2.0", "0.1.2")]
        [InlineData("1.0", "1.0.0")]
        public void SameVersion_ignores_missing_trailing_parts(string left, string right)
        {
            Assert.True(SourceSync.SameVersion(left, right));
            Assert.Equal(SourceState.UpToDate, SourceSync.Classify(left, right));
        }

        [Fact]
        public void Loop_reads_outside_its_roots_but_does_not_write_there()
        {
            var (read, write) = LoopScope.Create();
            string outside = Path.Combine(Path.GetTempPath(), "loop-scope-" + Guid.NewGuid().ToString("N"));

            Assert.True(read.TryResolve(outside, out _, out _));
            Assert.False(write.TryResolve(outside, out _, out _));
        }

        [Fact]
        public void SiblingOf_places_component_next_to_the_source_root()
        {
            string dev = Path.Combine(Path.GetTempPath(), "ol-dev", "OpenLoop");
            Assert.Equal(Path.Combine(Path.GetTempPath(), "ol-dev", "ChatFlow"),
                SourceSync.SiblingOf(dev, "ChatFlow"));
            Assert.Null(SourceSync.SiblingOf(null, "ChatFlow"));
            Assert.Null(SourceSync.SiblingOf("", "ChatFlow"));
        }

        [Fact]
        public void Loop_writes_dev_component_repos_but_nothing_else_outside()
        {
            string temp = Path.Combine(Path.GetTempPath(), "loop-components-" + Guid.NewGuid().ToString("N"));
            string sibling = Path.Combine(temp, "ChatFlow");
            var (_, write) = LoopScope.Create(writableComponents: [sibling]);

            Assert.True(write.TryResolve(Path.Combine(sibling, "ChatFlow", "ChatMessageRow.cs"), out _, out _));
            Assert.False(write.TryResolve(Path.Combine(temp, "elsewhere", "a.cs"), out _, out _));
        }
    }
}
