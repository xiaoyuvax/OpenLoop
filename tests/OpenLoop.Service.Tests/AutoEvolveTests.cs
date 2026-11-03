using OpenLoop.Service.Loop;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>自动演进的触发判据：开关、新证据条数、冷却、每日上限。</summary>
    public class AutoEvolveTests
    {
        private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.FromHours(8));

        private static bool Decide(
            bool enabled, int minRuns, int cooldown, int perDay, AutoEvolveState state, int runs, out string reason)
            => AutoEvolve.ShouldStart(enabled, minRuns, cooldown, perDay, state, runs, Now, out reason);

        [Fact]
        public void Disabled_never_starts()
        {
            Assert.False(Decide(false, 1, 0, 10, new AutoEvolveState(), 99, out string reason));
            Assert.Contains("未开启", reason);
        }

        [Fact]
        public void Needs_enough_new_runs()
        {
            var state = new AutoEvolveState { Runs = 3 };
            Assert.False(Decide(true, 2, 0, 10, state, 4, out string reason));
            Assert.Contains("1/2", reason);

            Assert.True(Decide(true, 2, 0, 10, state, 5, out reason));
            Assert.Null(reason);
        }

        [Fact]
        public void Cooldown_blocks_until_it_expires()
        {
            var state = new AutoEvolveState
            {
                Runs = 0,
                At = Now.AddMinutes(-30).ToString("O"),
                Day = Now.ToString("yyyy-MM-dd"),
                Count = 1
            };
            Assert.False(Decide(true, 1, 60, 10, state, 5, out string reason));
            Assert.Contains("冷却", reason);

            state.At = Now.AddMinutes(-61).ToString("O");
            Assert.True(Decide(true, 1, 60, 10, state, 5, out _));
        }

        [Fact]
        public void Daily_cap_resets_on_a_new_day()
        {
            var today = new AutoEvolveState { Day = Now.ToString("yyyy-MM-dd"), Count = 2, Runs = 0 };
            Assert.False(Decide(true, 1, 0, 2, today, 9, out string reason));
            Assert.Contains("上限 2", reason);

            var yesterday = new AutoEvolveState { Day = Now.AddDays(-1).ToString("yyyy-MM-dd"), Count = 2, Runs = 0 };
            Assert.True(Decide(true, 1, 0, 2, yesterday, 9, out _));
        }

        [Fact]
        public void Zero_daily_cap_disables_auto()
        {
            Assert.False(Decide(true, 1, 0, 0, new AutoEvolveState(), 9, out string reason));
            Assert.Contains("每日上限为 0", reason);
        }
    }
}
