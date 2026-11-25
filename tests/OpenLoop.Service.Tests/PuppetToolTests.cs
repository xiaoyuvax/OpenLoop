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

        [Fact]
        public void CaptureAction_response_becomes_attachment_instead_of_body_text()
        {
            string b64 = new string('A', 200);
            string body = $"{{\"ok\":true,\"result\":{{\"FileName\":\"capture.png\",\"ContentType\":\"image/png\","
                          + $"\"Content\":\"{b64}\"}},\"returnType\":\"Puppet.Core.AppAgent.PuppetArtifact\"}}";

            var attachment = PuppetTool.ExtractImage(body);

            Assert.NotNull(attachment);
            Assert.Equal("image/png", attachment.MimeType);
            Assert.Equal(b64, attachment.Base64);
            Assert.StartsWith("data:image/png;base64,", attachment.DataUrl);
        }

        [Fact]
        public void Ordinary_invoke_response_is_not_treated_as_attachment()
        {
            Assert.Null(PuppetTool.ExtractImage("{\"ok\":true,\"result\":{\"ok\":true}}"));
        }
    }
}
