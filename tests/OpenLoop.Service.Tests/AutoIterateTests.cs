using OpenLoop.Service.Loop;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>自动迭代的判据：开关、提交数门槛、跳过、冷却。</summary>
    public class AutoIterateTests
    {
        private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.FromHours(8));

        [Fact]
        public void Off_never_offers()
            => Assert.False(AutoIterate.ShouldOffer(false, 99, "head", null, Now, out _));

        [Fact]
        public void Below_threshold_waits()
        {
            Assert.False(AutoIterate.ShouldOffer(true, AutoIterate.MinCommits - 1, "head", null, Now, out string reason));
            Assert.Contains("新提交", reason);
        }

        [Fact]
        public void Enough_commits_offers()
            => Assert.True(AutoIterate.ShouldOffer(true, AutoIterate.MinCommits, "head", null, Now, out _));

        [Fact]
        public void Skipped_head_is_not_offered_again()
            => Assert.False(AutoIterate.ShouldOffer(true, 9, "head",
                new AutoIterateState { Skipped = "head" }, Now, out _));

        [Fact]
        public void New_head_after_skip_is_offered()
            => Assert.True(AutoIterate.ShouldOffer(true, 9, "new-head",
                new AutoIterateState { Skipped = "old-head" }, Now, out _));

        [Fact]
        public void Recent_attempt_cools_down()
        {
            var state = new AutoIterateState { AttemptAt = Now.AddMinutes(-5).ToString("O") };
            Assert.False(AutoIterate.ShouldOffer(true, 9, "head", state, Now, out string reason));
            Assert.Contains("冷却", reason);
        }

        [Fact]
        public void Old_attempt_no_longer_cools_down()
            => Assert.True(AutoIterate.ShouldOffer(true, 9, "head",
                new AutoIterateState { AttemptAt = Now.AddMinutes(-AutoIterate.CooldownMinutes).ToString("O") },
                Now, out _));
    }
}
