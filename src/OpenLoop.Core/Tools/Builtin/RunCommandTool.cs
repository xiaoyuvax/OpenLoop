using System.Diagnostics;
using System.Text;
using OpenLoop.Core.Agent;

namespace OpenLoop.Core.Tools.Builtin
{
    /// <summary>
    /// 在工作目录内执行命令。v0 明示**无沙箱**：只应在自己信任的目录里使用。
    /// </summary>
    public sealed class RunCommandTool : ITool
    {
        private readonly WorkspaceScope _scope;

        public RunCommandTool(WorkspaceScope scope) => _scope = scope;

        public string Name => "run_command";
        public string Description => "在工作目录内执行一条命令，返回退出码与输出。无沙箱。";
        public string ParamsSchema => "command: string; cwd: string 可选（默认工作目录根）; timeoutSeconds: number 可选（默认 60，上限 600）";
        public bool IsReadOnly => false;

        public async Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            string command = args.Str("command");
            if (string.IsNullOrWhiteSpace(command))
                return AgentToolResult.Failure("缺少参数 command");

            if (!_scope.TryResolve(args.Str("cwd"), out string workingDir, out string error))
                return AgentToolResult.Failure(error);

            if (!Directory.Exists(workingDir))
                return AgentToolResult.Failure($"工作目录不存在：{args.Str("cwd")}");

            int timeoutSeconds = Math.Clamp(args.Int("timeoutSeconds", 60), 1, 600);

            var psi = new ProcessStartInfo
            {
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            if (OperatingSystem.IsWindows())
            {
                psi.FileName = "cmd.exe";
                psi.Arguments = "/c " + command;
            }
            else
            {
                psi.FileName = "/bin/sh";
                psi.Arguments = "-c " + command;
            }

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
                return AgentToolResult.Failure($"启动命令失败：{ex.Message}");
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
                    $"命令超时（{timeoutSeconds}s），已终止。部分输出：\n{ToolText.Truncate(stdout.ToString(), 3000)}");
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
