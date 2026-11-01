using OpenLoop.Service.Runtime;
using Xunit;

namespace OpenLoop.Service.Tests
{
    public class RestartGuardTests
    {
        [Fact]
        public void Allows_up_to_max_then_refuses()
        {
            var guard = new RestartGuard(max: 2, window: TimeSpan.FromMilliseconds(50));

            Assert.True(guard.Allow("a"));
            Assert.True(guard.Allow("a"));
            Assert.False(guard.Allow("a"));
            Assert.True(guard.Allow("i"));
        }

        [Fact]
        public void Allows_again_after_the_window()
        {
            var guard = new RestartGuard(max: 1, window: TimeSpan.FromMilliseconds(30));

            Assert.True(guard.Allow("a"));
            Assert.False(guard.Allow("a"));
            Thread.Sleep(80);
            Assert.True(guard.Allow("a"));
        }
    }
}
