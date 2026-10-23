using System.Diagnostics;
using System.Text;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Safety;

namespace OpenLoop.Core.Tools.Builtin
{
    public sealed class CommandRequest
    {
        public CommandRequest(IReadOnlyList<string> argv, string workingDirectory, string reason)
        {
            Argv = argv;
            WorkingDirectory = workingDirectory;
            Reason = reason;
        }

        public IReadOnlyList<string> Argv { get; }
        public string WorkingDirectory { get; }
        public string Reason { get; }
        public string Line => string.Join(" ", Argv);
    }

    /// <summary>
    /// 在工作目录内以 argv 方式执行命令（不经 shell）。
    /// 危险命令直接拒绝；风险命令必须经审批回调；子进程只拿到环境白名单。
    /// </summary>
    public sealed class RunCommandTool : ITool
    {
        private readonly WorkspaceScope _scope;
        private readonly Func<CommandRequest, bool> _approve;

        public RunCommandTool(WorkspaceScope scope, Func<CommandRequest, bool> approve = null)
        {
            _scope = scope;
            _approve = approve;
        }

        public string Name => "run_command";
        public string Description => "在工作目录内以 argv 方式执行命令（不经 shell）。危险命令被拒绝，风险命令需要用户批准；子进程不继承密钥。";
        public string ParamsSchema => "argv: string[]（必需；argv[0] 是可执行文件，例如 [\"dotnet\",\"build\"]）; cwd: string 可选，默认工作目录; timeoutSeconds: number 可选，默认 60，最大 600";
        public bool IsReadOnly => false;

        public async Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            var argv = args.StrList("argv");
            if (argv.Count == 0)
                return AgentToolResult.Failure(
                    "缺少参数 argv（字符串数组）。旧式 command 字符串已停用：请改为 [\"可执行文件\", \"参数\", ...]，不要依赖 shell。");

            var verdict = CommandPolicy.Classify(argv);
            if (verdict.Risk == CommandRisk.Denied)
                return AgentToolResult.Failure($"拒绝执行（{verdict.Reason}）：{string.Join(" ", argv)}");

            if (!_scope.TryResolve(args.Str("cwd"), out string workingDir, out string error))
                return AgentToolResult.Failure(error);

            if (!Directory.Exists(workingDir))
                return AgentToolResult.Failure($"工作目录不存在：{args.Str("cwd")}");

            if (verdict.Risk == CommandRisk.Risky)
            {
                var request = new CommandRequest(argv, workingDir, verdict.Reason);
                if (_approve == null)
                    return AgentToolResult.Failure($"需要用户批准才能执行（{verdict.Reason}）：{request.Line}");
                if (!_approve(request))
                    return AgentToolResult.Failure($"用户拒绝执行：{request.Line}");
            }

            int timeoutSeconds = Math.Clamp(args.Int("timeoutSeconds", 60), 1, 600);

            var psi = new ProcessStartInfo
            {
                FileName = argv[0],
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            for (int i = 1; i < argv.Count; i++) psi.ArgumentList.Add(argv[i]);
            ChildEnvironment.Apply(psi);

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
