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
        public void Forget_removes_manually_rolled_back_roles_from_the_baseline()
        {
            string path = Path.Combine(Path.GetTempPath(), "openloop-state-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                VersionState.WriteFile(path, new Dictionary<string, NodeState>
                {
                    ["a"] = new() { Version = "0.1.18", Fingerprint = "fa" },
                    ["i"] = new() { Version = "0.1.19", Fingerprint = "fi" },
                    ["service"] = new() { Version = "0.1.17", Fingerprint = "fs" }
                });

                Assert.True(VersionState.Forget(["a", "i"], path));
                var state = VersionState.ReadFile(path);
                Assert.NotNull(state);
                Assert.False(state.ContainsKey("a"));
                Assert.False(state.ContainsKey("i"));
                Assert.True(state.ContainsKey("service"));
                Assert.False(VersionState.Forget(["a"], path));
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
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
