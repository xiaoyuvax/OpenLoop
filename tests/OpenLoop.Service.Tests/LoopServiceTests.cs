using OpenLoop.Core.Store;
using OpenLoop.Service.Loop;
using OpenLoop.Service.Storage;
using Xunit;

namespace OpenLoop.Service.Tests
{
    public class LoopServiceTests
    {
        private static string NewWorkspace()
        {
            string dir = Path.Combine(Path.GetTempPath(), "openloop-loop-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static async Task<LoopEvolveState> WaitAsync(LoopService loop, string workspace, int timeoutMs = 5000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            LoopEvolveState state = loop.GetState(workspace);
            while (DateTime.UtcNow < deadline && state.State == "running")
            {
                await Task.Delay(50);
                state = loop.GetState(workspace);
            }
            return state;
        }

        [Fact]
        public async Task Evolve_without_tasks_fails_before_calling_a_model()
        {
            string workspace = NewWorkspace();
            try
            {
                var loop = new LoopService();
                loop.StartEvolve(workspace, "m", "");
                var state = await WaitAsync(loop, workspace);

                Assert.Equal("error", state.State);
                Assert.Contains("没有考核题", state.Error);
            }
            finally
            {
                try { Directory.Delete(workspace, true); } catch { }
            }
        }

        [Fact]
        public void Rollback_needs_an_older_version()
        {
            string workspace = NewWorkspace();
            try
            {
                Ledger.Open(workspace).EnsureInitialized();
                var error = Assert.Throws<InvalidOperationException>(() => new LoopService().Rollback(workspace));
                Assert.Contains("没有更早的版本", error.Message);
            }
            finally
            {
                try { Directory.Delete(workspace, true); } catch { }
            }
        }

        [Fact]
        public void AddRule_appends_the_feature()
        {
            string workspace = NewWorkspace();
            try
            {
                var feature = new LoopService().AddRule(workspace, "rule.test", "始终先看调用点");

                Assert.False(string.IsNullOrWhiteSpace(feature.Id));
                Assert.Contains(Ledger.Open(workspace).Features(), f => f.Id == feature.Id);
            }
            finally
            {
                try { Directory.Delete(workspace, true); } catch { }
            }
        }

        [Fact]
        public void LoadActivation_initializes_the_ledger()
        {
            string workspace = NewWorkspace();
            try
            {
                var payload = new LoopService().LoadActivation(workspace);

                Assert.NotNull(payload.Activation);
                Assert.True(payload.Activation.Version >= 1);
                Assert.NotEmpty(payload.Features);
                Assert.True(Directory.Exists(Path.Combine(workspace, ".openloop")));
            }
            finally
            {
                try { Directory.Delete(workspace, true); } catch { }
            }
        }

        [Fact]
        public void Spool_drains_into_the_ledger_once()
        {
            string workspace = NewWorkspace();
            try
            {
                var record = new RunRecord
                {
                    Id = "R-test-1",
                    ActivationVersion = 1,
                    Task = "hello",
                    Outcome = "ok",
                    At = DateTimeOffset.UtcNow.ToString("O")
                };
                RunSpool.Write(workspace, record);
                RunSpool.Write(workspace, new RunRecord { Id = "R-test-2", ActivationVersion = 1, Outcome = "ok" });

                var loop = new LoopService();
                Assert.Equal(2, loop.DrainSpool(workspace));
                Assert.Equal(0, loop.DrainSpool(workspace));
                Assert.Equal(2, Ledger.Open(workspace).Runs().Count);
                Assert.Empty(Directory.GetFiles(RunSpool.Dir(workspace), "*.json"));

                // 重复投递同一个 run id 不重复记账
                RunSpool.Write(workspace, record);
                Assert.Equal(0, loop.DrainSpool(workspace));
                Assert.Equal(2, Ledger.Open(workspace).Runs().Count);
            }
            finally
            {
                try { Directory.Delete(workspace, true); } catch { }
            }
        }
    }
}
