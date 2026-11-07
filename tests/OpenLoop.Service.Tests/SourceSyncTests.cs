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

        [Fact]
        public void Loop_reads_outside_its_roots_but_does_not_write_there()
        {
            var (read, write) = LoopScope.Create();
            string outside = Path.Combine(Path.GetTempPath(), "loop-scope-" + Guid.NewGuid().ToString("N"));

            Assert.True(read.TryResolve(outside, out _, out _));
            Assert.False(write.TryResolve(outside, out _, out _));
        }
    }
}
