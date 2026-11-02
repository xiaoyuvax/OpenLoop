using OpenLoop.Core.Agent;
using OpenLoop.Core.Tools;
using OpenLoop.Service.Plugins;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>
    /// 真进程回归：用 samples/plugins/echo 跑一次 stdio 往返，再打崩它看宿主是否还活着。
    /// 没装 Node 的机器直接跳过（CI 上不为了示例插件强加运行时）。
    /// </summary>
    public class EchoPluginTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "openloop-echo-" + Guid.NewGuid().ToString("N"));
        private readonly string _workspace;

        public EchoPluginTests()
        {
            _workspace = Path.Combine(_root, "ws");
            Directory.CreateDirectory(_workspace);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string Install(string source = null)
        {
            string target = Path.Combine(_workspace, ".openloop", "plugins", "echo");
            Directory.CreateDirectory(target);
            string from = source ?? Path.Combine(RepoRoot() ?? "", "samples", "plugins", "echo");
            foreach (string file in Directory.GetFiles(from))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            return target;
        }

        private static ITool EchoTool(PluginCatalog catalog)
            => catalog.Tools().Single(t => t.Name == "Echo");

        private static Task<AgentToolResult> Call(ITool tool, string text)
            => tool.ExecuteAsync(new ToolArgs(new Dictionary<string, object> { ["text"] = text }),
                CancellationToken.None);

        [Fact]
        public async Task Echoes_text_over_stdio()
        {
            if (!HasNode()) return;
            Install();

            using var catalog = PluginCatalog.Load(_workspace);
            var result = await Call(EchoTool(catalog), "hi");

            Assert.True(result.Ok, result.Error);
            Assert.Equal("hi", result.Data);
        }

        [Fact]
        public async Task Crash_is_isolated_and_the_plugin_restarts_once()
        {
            if (!HasNode()) return;
            Install();

            using var catalog = PluginCatalog.Load(_workspace);
            var tool = EchoTool(catalog);

            var crash = await Call(tool, "__crash__");
            Assert.False(crash.Ok);

            var after = await Call(tool, "alive");
            Assert.True(after.Ok, after.Error);
            Assert.Equal("alive", after.Data);
        }

        [Fact]
        public async Task Timeout_returns_a_failure_and_does_not_hang()
        {
            if (!HasNode()) return;
            string dir = Install();
            string manifest = Path.Combine(dir, "plugin.json");
            File.WriteAllText(manifest,
                File.ReadAllText(manifest).Replace("\"timeoutMs\": 30000", "\"timeoutMs\": 300"));

            using var catalog = PluginCatalog.Load(_workspace);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = await Call(EchoTool(catalog), "__sleep__");
            watch.Stop();

            Assert.False(result.Ok);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4), "超时没有按 300ms 生效");
        }

        private static bool HasNode()
        {
            string name = OperatingSystem.IsWindows() ? "node.exe" : "node";
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                if (File.Exists(Path.Combine(dir.Trim('"'), name))) return true;
            }
            return false;
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OpenLoop.slnx"))) dir = dir.Parent;
            return dir?.FullName;
        }
    }
}
