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
        public void Ledger_can_override_base_and_ceiling()
        {
            Assert.Equal(TimeSpan.FromSeconds(2), RetryBackoff.Next(0, 2, 30));
            Assert.Equal(TimeSpan.FromSeconds(2), RetryBackoff.Next(2, 2, 30));
            Assert.Equal(TimeSpan.FromSeconds(4), RetryBackoff.Next(3, 2, 30));
            Assert.Equal(TimeSpan.FromSeconds(30), RetryBackoff.Next(10, 2, 30));
            // 上限小于起点时按起点算，不减慢节奏
            Assert.Equal(TimeSpan.FromSeconds(2), RetryBackoff.Next(10, 2, 1));
        }

        [Fact]
        public void Only_connect_level_failures_are_retried()
        {
            Assert.True(OpenCodeReasoner.IsConnectFailure(
                new HttpRequestException("由于目标计算机积极拒绝，无法连接。 (127.0.0.1:28894)",
                    new SocketException((int)SocketError.ConnectionRefused))));
            Assert.True(OpenCodeReasoner.IsConnectFailure(new InvalidOperationException(
                "provider.transport：ECONNRESET: The socket connection was closed unexpectedly.")));
            // opencode 自己重启时返回 503 service_starting：与拒绝连接同类，等它起来重试。
            Assert.True(OpenCodeReasoner.IsConnectFailure(new OpenCodeHttpException(
                503, "OpenCode 返回 503：{\"code\":\"service_starting\"}")));
            // 502 是它前面代理在重启窗口里拿不到有效响应，408 是临时超时：都还有连上的希望。
            Assert.True(OpenCodeReasoner.IsConnectFailure(new OpenCodeHttpException(502, "OpenCode 返回 502：bad gateway")));
            Assert.True(OpenCodeReasoner.IsConnectFailure(new OpenCodeHttpException(408, "OpenCode 返回 408：timeout")));
            // 504 多半是这一轮本身太大，500 可能是 opencode 自己崩：都只重试两次，不按连接层一直等。
            Assert.False(OpenCodeReasoner.IsConnectFailure(new OpenCodeHttpException(504, "OpenCode 返回 504：gateway timeout")));
            Assert.False(OpenCodeReasoner.IsConnectFailure(new OpenCodeHttpException(500, "OpenCode 返回 500：{}")));
            Assert.True(OpenCodeReasoner.IsLimitedRetry(new OpenCodeHttpException(504, "OpenCode 返回 504：gateway timeout")));
            Assert.True(OpenCodeReasoner.IsLimitedRetry(new OpenCodeHttpException(500, "OpenCode 返回 500：{}")));
            Assert.False(OpenCodeReasoner.IsLimitedRetry(new OpenCodeHttpException(503, "OpenCode 返回 503：{}")));
            // 404 是目标不存在（重启后会话被清），重试无用。
            Assert.False(OpenCodeReasoner.IsConnectFailure(new OpenCodeHttpException(404, "OpenCode 返回 404：{}")));
            Assert.False(OpenCodeReasoner.IsLimitedRetry(new OpenCodeHttpException(404, "OpenCode 返回 404：{}")));
            Assert.False(OpenCodeReasoner.IsConnectFailure(
                new HttpRequestException("Response status code does not indicate success: 429")));
            Assert.False(OpenCodeReasoner.IsConnectFailure(new InvalidOperationException("provider.free-limit")));
            Assert.False(OpenCodeReasoner.IsConnectFailure(new InvalidOperationException("OpenCode 没有返回会话 ID")));
            Assert.False(OpenCodeReasoner.IsConnectFailure(new TaskCanceledException()));
        }
    }
}
