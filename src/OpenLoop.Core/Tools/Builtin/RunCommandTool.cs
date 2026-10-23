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
        private readonly bool _inheritEnvironment;

        public RunCommandTool(WorkspaceScope scope, Func<CommandRequest, bool> approve = null,
            bool inheritEnvironment = false)
        {
            _scope = scope;
            _approve = approve;
            _inheritEnvironment = inheritEnvironment;
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
            return await ProcessRunner.RunAsync(argv, workingDir, timeoutSeconds, ct, _inheritEnvironment);
        }
    }
}
