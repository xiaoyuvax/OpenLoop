using OpenLoop.Service.Config;
using OpenLoop.Service.Models;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>dsh 通道的基础判定：没装命令就不出现。</summary>
    public class ToolExposureTests
    {
        [Fact]
        public void Dsh_provider_needs_an_installed_command()
        {
            var settings = new AppSettings { DshCommand = Path.Combine(Path.GetTempPath(), "no-dsh-" + Guid.NewGuid().ToString("N") + ".exe") };

            Assert.False(DshAcpHub.Installed(settings));
            Assert.Null(DshAcpHub.UiProvider(settings));
        }
    }
}
