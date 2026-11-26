using OpenLoop.Core.Tools.Builtin;
using Xunit;

namespace OpenLoop.Core.Tests
{
    public class ProcessRunnerTests
    {
        [Fact]
        public async Task Long_output_keeps_the_tail_where_the_summary_is()
        {
            if (!OperatingSystem.IsWindows()) return;
            var argv = new[]
            {
                "powershell", "-NoProfile", "-Command",
                "1..2000 | ForEach-Object { 'line ' + $_ }; 'TAIL_MARKER_XYZ'"
            };

            var result = await ProcessRunner.RunAsync(argv, Environment.CurrentDirectory, 60, CancellationToken.None);

            Assert.True(result.Ok);
            Assert.StartsWith("exit=0", result.Data);
            Assert.Contains("省略", result.Data);
            Assert.Contains("TAIL_MARKER_XYZ", result.Data);
        }

        [Fact]
        public async Task Slow_report_stops_when_the_command_ends()
        {
            if (!OperatingSystem.IsWindows()) return;
            var notices = new List<string>();
            ProcessRunner.Progress.Value = n => { if (n != null) lock (notices) notices.Add(n); };
            try
            {
                var argv = new[] { "powershell", "-NoProfile", "-Command", "'done'" };
                var result = await ProcessRunner.RunAsync(argv, Environment.CurrentDirectory, 60, CancellationToken.None);
                Assert.True(result.Ok);
                // 越过 5 秒播报点：命令早就结束了，不该再报「仍在执行」。
                await Task.Delay(6000);
            }
            finally
            {
                ProcessRunner.Progress.Value = null;
            }

            Assert.Empty(notices);
        }

        [Fact]
        public async Task Slow_command_reports_progress()
        {
            if (!OperatingSystem.IsWindows()) return;
            var notices = new List<string>();
            ProcessRunner.Progress.Value = n => { if (n != null) lock (notices) notices.Add(n); };
            try
            {
                var argv = new[] { "powershell", "-NoProfile", "-Command", "Start-Sleep -Milliseconds 5500; 'done'" };
                var result = await ProcessRunner.RunAsync(argv, Environment.CurrentDirectory, 60, CancellationToken.None);
                Assert.True(result.Ok);
            }
            finally
            {
                ProcessRunner.Progress.Value = null;
            }

            Assert.Contains(notices, n => n.Contains("命令仍在执行"));
        }
    }
}
