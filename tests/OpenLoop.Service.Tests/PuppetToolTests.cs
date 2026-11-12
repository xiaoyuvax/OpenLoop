using OpenLoop.Core.Tools;
using OpenLoop.Service.Web;
using Puppet.Core;
using Puppet.Core.Web;
using Xunit;

namespace OpenLoop.Service.Tests
{
    public class PuppetToolTests
    {
        [Fact]
        public async Task Self_target_goes_through_the_in_process_hub()
        {
            var page = new WebPuppet("test-bus-key");
            PuppetKeyVault.SetKey("test-puppet-key");
            PuppetRegistry.Register(page);

            var previousHub = PuppetBridge.LocalHub;
            var previousRole = PuppetBridge.SelfRole;
            PuppetBridge.LocalHub = new PuppetWebServer().Hub;
            PuppetBridge.SelfRole = "a";
            try
            {
                var result = await new PuppetTool().ExecuteAsync(
                    new ToolArgs(new Dictionary<string, object> { ["action"] = "registry" }), CancellationToken.None);

                Assert.True(result.Ok, result.Error);
                Assert.Contains("WebUi", result.Data);
            }
            finally
            {
                PuppetBridge.LocalHub = previousHub;
                PuppetBridge.SelfRole = previousRole;
                PuppetRegistry.Unregister(WebPuppet.InstanceName);
            }
        }
    }
}
