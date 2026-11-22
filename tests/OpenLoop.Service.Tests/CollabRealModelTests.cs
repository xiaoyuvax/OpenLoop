using System.Text;
using OpenLoop.Service.Config;
using OpenLoop.Service.Storage;
using OpenLoop.Service.Worker;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>
    /// 协作的真实模型端到端：发起方自己分解、起协作会话、监测汇报进队列。
    /// 默认不跑（会花额度）：设 OPENLOOP_COLLAB_REAL=1 才执行。
    /// 报告写到 %TEMP%\openloop-collab-real-report.txt，便于事后看模型到底怎么走的。
    /// </summary>
    [Collection("chat-index")]
    public class CollabRealModelTests : IDisposable
    {
        private const string Model = "deepseek-v4.1-flash";
        private const string InitiatorId = "init-real";

        private readonly string _chats = Path.Combine(Path.GetTempPath(), "openloop-collab-real-chats-" + Guid.NewGuid().ToString("N")[..8]);
        private readonly string _workspace = Path.Combine(Path.GetTempPath(), "openloop-collab-real-ws-" + Guid.NewGuid().ToString("N")[..8]);
        private readonly string _previousRoot;
        private readonly string _report = Path.Combine(Path.GetTempPath(), "openloop-collab-real-report.txt");

        public CollabRealModelTests()
        {
            _previousRoot = ChatHistory.Root;
            ChatHistory.Root = _chats;
            Directory.CreateDirectory(_workspace);
        }

        public void Dispose()
        {
            ChatHistory.Root = _previousRoot;
            try { Directory.Delete(_chats, true); } catch { }
        }

        [Fact]
        public async Task Initiator_decomposes_spawns_children_and_monitors_them()
        {
            if (Environment.GetEnvironmentVariable("OPENLOOP_COLLAB_REAL") != "1") return;

            string key = File.ReadAllText(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OpenLoop", "plugins", "agent2api", "key.txt")).Trim();

            var settings = new AppSettings
            {
                AutoApprove = true,
                EnableCollab = true,
                CollabPollSeconds = 5,
                CollabReuseContext = false,
                WebFetchMode = "off",
                UseAgentsMd = false,
                Model = Model,
                Providers =
                [
                    new ProviderConfig
                    {
                        Name = "Agent2API",
                        Kind = "custom",
                        BaseUrl = "http://127.0.0.1:3065/v1",
                        RuntimeKey = key,
                        Models = [Model]
                    }
                ]
            };

            // 不传 runner：走真实的 WorkerSessionService（含账本、trace、工具表、协作工具）
            var host = new WorkerSessionHost();
            var initiator = host.Create(_workspace, Model, "", () => settings, InitiatorId);

            host.Send(InitiatorId, "task",
                "这个目录里要产出两个文件：alpha.txt（内容 ALPHA）、beta.txt（内容 BETA）。"
                + "这两件事互不依赖。请用 CollabStart 把它们拆给两个协作会话各自完成，"
                + "然后调用 CollabMonitor 监测它们。你自己不要写文件、不要替它们干活。");

            var deadline = DateTime.UtcNow.AddMinutes(10);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(2000);
                var rows = host.List();
                var kids = rows.Where(r => r.Id != InitiatorId).ToList();
                bool busy = rows.Any(r => r.State == WorkerSessionHost.Running);
                if (!busy && kids.Count > 0) break;
            }

            var all = host.List();
            var children = all.Where(r => r.Id != InitiatorId).ToList();
            var snapshot = host.Get(InitiatorId);

            var report = new StringBuilder();
            report.AppendLine($"workspace: {_workspace}");
            report.AppendLine($"initiator: {snapshot?.State} outcome={snapshot?.Outcome} steps={snapshot?.Steps}");
            foreach (var turn in snapshot?.Turns ?? [])
            {
                report.AppendLine("--- user ---").AppendLine(Cut(turn.User, 400));
                if (!string.IsNullOrWhiteSpace(turn.ToolLines))
                    report.AppendLine("--- tools ---").AppendLine(Cut(turn.ToolLines, 1200));
                report.AppendLine("--- assistant ---").AppendLine(Cut(turn.Assistant, 600));
            }
            report.AppendLine($"children: {children.Count}");
            foreach (var child in children)
            {
                report.AppendLine($"- {child.Id} {child.State} outcome={child.Outcome} steps={child.Steps}");
                foreach (var turn in child.Turns ?? [])
                    report.AppendLine("   " + Cut(turn.Assistant, 200).Replace("\n", " "));
            }
            report.AppendLine("files: " + string.Join(", ", Directory.EnumerateFiles(_workspace).Select(Path.GetFileName)));
            File.WriteAllText(_report, report.ToString(), new UTF8Encoding(false));

            Assert.True(children.Count >= 1, "没有启动协作会话：\n" + report);
            Assert.True(File.Exists(Path.Combine(_workspace, "alpha.txt"))
                        || File.Exists(Path.Combine(_workspace, "beta.txt")),
                "协作会话没有产出文件：\n" + report);
            Assert.Contains(snapshot?.Turns ?? [], t => t.User.Contains("协作监测"));
        }

        private static string Cut(string text, int max)
            => string.IsNullOrEmpty(text) || text.Length <= max ? text ?? "" : text[..max] + "…";
    }
}
