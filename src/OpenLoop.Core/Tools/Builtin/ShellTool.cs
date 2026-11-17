using OpenLoop.Core.Agent;
using OpenLoop.Core.Safety;

namespace OpenLoop.Core.Tools.Builtin
{
    /// <summary>
    /// 在工作目录内以 argv 方式执行命令（不经 shell）。
    /// 危险命令直接拒绝；风险命令必须经审批回调；子进程只拿到环境白名单。
    /// </summary>
    public sealed class ShellTool : ITool
    {
        private readonly WorkspaceScope _scope;
        private readonly ApprovalHandler _approve;
        private readonly bool _inheritEnvironment;

        public ShellTool(WorkspaceScope scope, ApprovalHandler approve = null,
            bool inheritEnvironment = false)
        {
            _scope = scope;
            _approve = approve;
            _inheritEnvironment = inheritEnvironment;
        }

        public string Name => "Shell";
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

            if (!_scope.TryResolve(args.Str("cwd"), out string workingDir, out string error, Name))
                return AgentToolResult.Failure(error);

            if (!Directory.Exists(workingDir))
                return AgentToolResult.Failure($"工作目录不存在：{args.Str("cwd")}");

            // cwd 受限不等于参数受限：findstr /s x C:\other\* 之类能读工作目录外的文件。
            // 越界的绝对路径参数一律按风险处理（无审批通道就不执行）。
            string outside = FirstOutsideArgument(argv);
            if (verdict.Risk == CommandRisk.Risky || outside != null)
            {
                string reason = verdict.Risk == CommandRisk.Risky
                    ? verdict.Reason
                    : $"参数引用工作目录之外：{outside}";
                string line = string.Join(" ", argv);
                if (_approve == null)
                    return AgentToolResult.Failure($"需要用户批准才能执行（{reason}）：{line}");
                var decision = _approve(new ApprovalRequest(ApprovalKind.SensitiveAction, Name, line, reason));
                if (decision == ApprovalDecision.Deny)
                    return AgentToolResult.Failure($"用户拒绝执行：{line}");
            }

            int timeoutSeconds = Math.Clamp(args.Int("timeoutSeconds", 60), 1, 600);
            return await ProcessRunner.RunAsync(argv, workingDir, timeoutSeconds, ct, _inheritEnvironment);
        }

        /// <summary>参数里第一个越界的绝对路径；没有就返回 null。</summary>
        private string FirstOutsideArgument(IReadOnlyList<string> argv)
        {
            if (_scope.Unrestricted) return null;
            foreach (string arg in argv.Skip(1))
            {
                foreach (string candidate in PathCandidates(arg))
                {
                    if (IsRooted(candidate) && !_scope.Contains(candidate)) return candidate;
                }
            }
            return null;
        }

        /// <summary>一个参数里可能藏路径的写法：自身，以及 <c>--out=C:\x</c> 的等号右侧。</summary>
        private static IEnumerable<string> PathCandidates(string arg)
        {
            yield return arg;
            int eq = arg.IndexOf('=');
            if (eq > 0 && eq < arg.Length - 1) yield return arg[(eq + 1)..];
        }

        private static bool IsRooted(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 2) return false;
            if (value.StartsWith(@"\\", StringComparison.Ordinal)) return true;
            if (char.IsLetter(value[0]) && value[1] == ':' && value.Length > 2
                && (value[2] == '\\' || value[2] == '/')) return true;
            return !OperatingSystem.IsWindows() && value[0] == '/';
        }
    }
}
