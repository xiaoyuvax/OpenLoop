using OpenLoop.Core.Messages;
using Xunit;

namespace OpenLoop.Core.Tests
{
    public class ServerNoticeTests
    {
        [Theory]
        [InlineData("执行失败：provider.free-limit：Free usage limit reached.", true)]
        [InlineData("执行失败：HTTP 429 Too Many Requests", true)]
        [InlineData("执行失败：HTTP 402 Payment Required", true)]
        [InlineData("执行失败：余额不足", true)]
        [InlineData("执行失败：This request requires more credits", true)]
        [InlineData("429 是 Too Many Requests 的状态码", false)]
        [InlineData("执行失败：provider.context_length_exceeded：Input is too long", false)]
        [InlineData("执行失败：connection refused", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Quota_failure_needs_failed_prefix_and_quota_signal(string text, bool expected)
            => Assert.Equal(expected, ServerNotice.IsQuotaFailure(text));
    }
}
