using OpenLoop.Service.Config;
using OpenLoop.Service.Models;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>任务工具暴露的闸门：额度撞墙后先藏起来，没探测到也藏起来。</summary>
    public class ToolExposureTests
    {
        [Theory]
        [InlineData("This request requires more credits")]
        [InlineData("Free usage limit reached")]
        [InlineData("HTTP 402 Payment Required")]
        [InlineData("余额不足")]
        public void Quota_like_errors_close_the_gate(string message)
        {
            var gate = new QuotaGate("test", TimeSpan.FromMinutes(1));
            Assert.True(gate.Open);

            gate.Note(message);

            Assert.False(gate.Open);
        }

        [Fact]
        public void Unrelated_errors_leave_the_gate_open()
        {
            var gate = new QuotaGate("test", TimeSpan.FromMinutes(1));
            gate.Note("connection refused");

            Assert.True(gate.Open);
        }

        [Fact]
        public void Dsh_tool_is_hidden_for_a_profile_that_was_never_probed()
        {
            var settings = new AppSettings { DshProfile = "unprobed-" + Guid.NewGuid().ToString("N") };

            Assert.Empty(DshTools.For(settings, Path.GetTempPath()));
        }

        [Fact]
        public void Dsh_provider_needs_an_installed_command()
        {
            var settings = new AppSettings { DshCommand = Path.Combine(Path.GetTempPath(), "no-dsh-" + Guid.NewGuid().ToString("N") + ".exe") };

            Assert.False(DshAcpHub.Installed(settings));
            Assert.Null(DshAcpHub.UiProvider(settings));
        }
    }
}
