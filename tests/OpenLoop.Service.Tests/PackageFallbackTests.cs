using OpenLoop.Service.Source;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>包兜底版本策略：packageVersion 只是下限，实际取 nuget.org 上 >= 它的最高稳定版。</summary>
    public class PackageFallbackTests
    {
        [Fact]
        public void Best_takes_the_highest_version_at_or_above_the_reference()
            => Assert.Equal("2.19.0.240", PackageFallback.Best(
                ["2.19.0.233", "2.19.0.240", "2.19.0.236"], "2.19.0.233"));

        [Fact]
        public void Best_ignores_versions_below_the_reference_and_prereleases()
            => Assert.Equal("2.19.0.240", PackageFallback.Best(
                ["2.18.0.220", "2.19.0.240", "3.0.0-beta", "2.19.0.241-preview.1"], "2.19.0.233"));

        [Fact]
        public void Best_falls_back_to_the_reference_when_nothing_is_newer()
        {
            Assert.Equal("2.19.0.233", PackageFallback.Best(["2.18.0.220"], "2.19.0.233"));
            Assert.Equal("2.19.0.233", PackageFallback.Best([], "2.19.0.233"));
            Assert.Equal("", PackageFallback.Best(null, ""));
        }

        [Fact]
        public void Best_compares_three_part_versions_as_complete_numbers()
            => Assert.Equal("2.19.1", PackageFallback.Best(["2.19.0", "2.19.1"], "2.19.0"));

        [Fact]
        public void PropertyName_is_the_package_id_without_punctuation()
            => Assert.Equal("OpenLoopPkg_VaxFCTB", PackageFallback.PropertyName("Vax-FCTB"));
    }
}
