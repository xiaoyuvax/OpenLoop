using System.Diagnostics;
using System.Text;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Safety;

namespace OpenLoop.Core.Tools.Builtin
{
    /// <summary>argv 执行器（不经 shell）：子进程只拿环境白名单，超时杀进程树，输出截断。</summary>
    internal static class ProcessRunner
    {
        public static async Task<AgentToolResult> RunAsync(
            IReadOnlyList<string> argv, string workingDirectory, int timeoutSeconds, CancellationToken ct,
            bool inheritEnvironment = false)
        {
            var psi = new ProcessStartInfo
            {
                FileName = argv[0],
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            for (int i = 1; i < argv.Count; i++) psi.ArgumentList.Add(argv[i]);
            ChildEnvironment.Apply(psi, inheritEnvironment);

            using var process = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                return AgentToolResult.Failure($"命令启动失败：{ex.Message}");
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                if (ct.IsCancellationRequested) throw;

                return AgentToolResult.Failure(
                    $"命令超时（{timeoutSeconds}s），已终止进程树。\n{ToolText.Truncate(stdout.ToString(), 3000)}");
            }

            var sb = new StringBuilder();
            sb.Append("exit=").AppendLine(process.ExitCode.ToString());
            if (stdout.Length > 0) sb.AppendLine("--- stdout ---").AppendLine(stdout.ToString().TrimEnd());
            if (stderr.Length > 0) sb.AppendLine("--- stderr ---").AppendLine(stderr.ToString().TrimEnd());

            string output = ToolText.Truncate(sb.ToString(), 6000);
            return process.ExitCode == 0
                ? AgentToolResult.Success(output)
                : AgentToolResult.Failure(output);
        }
    }
}
