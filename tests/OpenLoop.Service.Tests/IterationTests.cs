using OpenLoop.Service.Source;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>迭代轮次的纯函数部分（目录命名、日志裁剪）。</summary>
    public class IterationTests
    {
        [Fact]
        public void VersionDirName_uses_version_and_sha8()
            => Assert.Equal("0.2.0-01234567", Iteration.VersionDirName("0.2.0", "0123456789abcdef"));

        [Fact]
        public void VersionDirName_falls_back_when_version_is_unusable()
            => Assert.Equal("adhoc-01234567", Iteration.VersionDirName(" ", "0123456789abcdef"));

        [Fact]
        public void Tail_keeps_last_lines_and_skips_blanks()
            => Assert.Equal("b | c", Iteration.Tail("a\n\nb\r\nc\n", 2));
    }
}
