using OpenLoop.Service.Source;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>不换壳的一轮（Puppet 的无窗口迭代）不能把壳记成已发布。</summary>
    public class VersionStateTests
    {
        [Fact]
        public void Promote_keeps_the_old_shell_baseline_when_the_shell_did_not_take_part()
        {
            var pending = new Dictionary<string, NodeState>
            {
                ["a"] = new() { Version = "0.1.13", Fingerprint = "new-a" },
                ["shell"] = new() { Version = "0.1.14", Fingerprint = "new-shell" }
            };
            var old = new Dictionary<string, NodeState>
            {
                ["shell"] = new() { Version = "0.1.13", Fingerprint = "old-shell" }
            };

            VersionState.KeepOldNodes(pending, old, ["shell"]);

            Assert.Equal("0.1.13", pending["a"].Version);
            Assert.Equal("0.1.13", pending["shell"].Version);
            Assert.Equal("old-shell", pending["shell"].Fingerprint);
        }

        [Fact]
        public void Promote_drops_a_node_the_baseline_never_had()
        {
            var pending = new Dictionary<string, NodeState>
            {
                ["shell"] = new() { Version = "0.1.14", Fingerprint = "new-shell" }
            };

            VersionState.KeepOldNodes(pending, null, ["shell"]);

            Assert.False(pending.ContainsKey("shell"));
        }
    }
}
