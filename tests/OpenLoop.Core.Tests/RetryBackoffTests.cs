using System.Net.Sockets;
using OpenLoop.Core.OpenCode;
using OpenLoop.Core.Reasoning;
using Xunit;

namespace OpenLoop.Core.Tests
{
    public class RetryBackoffTests
    {
        [Fact]
        public void First_three_retries_wait_five_seconds_then_back_off_up_to_five_minutes()
        {
            Assert.Equal(TimeSpan.FromSeconds(5), RetryBackoff.Next(0));
            Assert.Equal(TimeSpan.FromSeconds(5), RetryBackoff.Next(2));
            Assert.Equal(TimeSpan.FromSeconds(10), RetryBackoff.Next(3));
            Assert.Equal(TimeSpan.FromSeconds(20), RetryBackoff.Next(4));
            Assert.Equal(TimeSpan.FromSeconds(40), RetryBackoff.Next(5));
            Assert.Equal(TimeSpan.FromMinutes(5), RetryBackoff.Next(20));
            Assert.Equal(TimeSpan.FromMinutes(5), RetryBackoff.Next(int.MaxValue));
        }

        [Fact]
        public void Only_connect_level_failures_are_retried()
        {
            Assert.True(OpenCodeReasoner.IsConnectFailure(
                new HttpRequestException("由于目标计算机积极拒绝，无法连接。 (127.0.0.1:28894)",
                    new SocketException((int)SocketError.ConnectionRefused))));
            Assert.False(OpenCodeReasoner.IsConnectFailure(
                new HttpRequestException("Response status code does not indicate success: 429")));
            Assert.False(OpenCodeReasoner.IsConnectFailure(new InvalidOperationException("provider.free-limit")));
            Assert.False(OpenCodeReasoner.IsConnectFailure(new TaskCanceledException()));
        }
    }
}
