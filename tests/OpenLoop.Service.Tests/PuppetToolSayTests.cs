using OpenLoop.Core.Agent;
using OpenLoop.Core.Tools;
using OpenLoop.Service.Web;
using Puppet.Core;
using Puppet.Core.Web;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>
    /// Puppet 工具的 say：带 to 的留言既要写进留言板（只有双方可见），
    /// 也要作为「协作提示」主动投递到目标会话队列——广播要对方自己来读，错过就看不到。
    /// </summary>
    [Collection("chat-index")]
    public class PuppetToolSayTests
    {
        [Fact]
        public async Task Say_with_to_writes_the_board_and_delivers_to_the_target()
        {
            var previousHub = PuppetBridge.LocalHub;
            var previousRole = PuppetBridge.SelfRole;
            var previousLocal = PeerDelivery.Local;
            var delivered = new List<(string Id, string Text, string Tag)>();
            var page = new WebPuppet("test-bus-key");
            PuppetKeyVault.SetKey("test-puppet-key");
            PuppetRegistry.Register(page);
            PuppetBridge.LocalHub = new PuppetWebServer().Hub;
            PuppetBridge.SelfRole = "a";
            PeerDelivery.Local = (id, text, tag) =>
            {
                lock (delivered) delivered.Add((id, text, tag));
                return id == "worker01";
            };

            try
            {
                using var identity = AgentIdentity.Begin("I-sender", "self");
                var result = await new PuppetTool().ExecuteAsync(new ToolArgs(new Dictionary<string, object>
                {
                    ["action"] = "say",
                    ["text"] = "先别动 ChatFlowControl.cs",
                    ["to"] = "A-worker01"
                }), CancellationToken.None);

                Assert.True(result.Ok, result.Error);
                // 工具结果里明确告诉模型「已投递」，否则它会以为只是广播
                Assert.Contains("A-worker01", result.Data);

                var one = Assert.Single(delivered);
                Assert.Equal("worker01", one.Id);
                Assert.Equal("mention", one.Tag);
                Assert.Contains("先别动 ChatFlowControl.cs", one.Text);
                Assert.Contains("I-sender", one.Text);
            }
            finally
            {
                PeerDelivery.Local = previousLocal;
                PuppetBridge.LocalHub = previousHub;
                PuppetBridge.SelfRole = previousRole;
                PuppetRegistry.Unregister(WebPuppet.InstanceName);
            }
        }

        [Fact]
        public async Task Say_without_to_does_not_deliver_anything()
        {
            var previousHub = PuppetBridge.LocalHub;
            var previousRole = PuppetBridge.SelfRole;
            var previousLocal = PeerDelivery.Local;
            int calls = 0;
            var page = new WebPuppet("test-bus-key");
            PuppetKeyVault.SetKey("test-puppet-key");
            PuppetRegistry.Register(page);
            PuppetBridge.LocalHub = new PuppetWebServer().Hub;
            PuppetBridge.SelfRole = "a";
            PeerDelivery.Local = (_, _, _) =>
            {
                Interlocked.Increment(ref calls);
                return true;
            };

            try
            {
                using var identity = AgentIdentity.Begin("I-sender", "self");
                var result = await new PuppetTool().ExecuteAsync(new ToolArgs(new Dictionary<string, object>
                {
                    ["action"] = "say",
                    ["text"] = "广播一句"
                }), CancellationToken.None);

                Assert.True(result.Ok, result.Error);
                Assert.Equal(0, Volatile.Read(ref calls));
            }
            finally
            {
                PeerDelivery.Local = previousLocal;
                PuppetBridge.LocalHub = previousHub;
                PuppetBridge.SelfRole = previousRole;
                PuppetRegistry.Unregister(WebPuppet.InstanceName);
            }
        }
    }
}
