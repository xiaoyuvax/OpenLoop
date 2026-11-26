using System.Diagnostics;
using System.Text;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Safety;

namespace OpenLoop.Core.Tools.Builtin
{
    /// <summary>argv 执行器（不经 shell）：子进程只拿环境白名单，超时杀进程树，输出截断。</summary>
    public static class ProcessRunner
    {
        /// <summary>长命令进度通报（参数为状态文本，null = 清除）；宿主接到状态行。</summary>
        public static event Action<string> SlowCommand;

        /// <summary>
        /// 当前异步流上的长命令进度接收者（null = 清除）。Shell 事件只覆盖同进程的壳内会话；
        /// Worker 在 A 进程里跑，宿主按会话在这里接线，才能把「已 N 秒」送到界面上。
        /// </summary>
        public static readonly AsyncLocal<Action<string>> Progress = new();

        public static async Task<AgentToolResult> RunAsync(
            IReadOnlyList<string> argv, string workingDirectory, int timeoutSeconds, CancellationToken ct,
            bool inheritEnvironment = false)
        {
            // PATH 里的 npm 垫片（ocr、npx…）只补扩展名找不到，CreateProcess 直接报「找不到文件」
            var (fileName, prefix, resolveError) = ExecutableResolver.Resolve(
                argv[0], argv.Skip(1).ToList());
            if (resolveError != null) return AgentToolResult.Failure("拒绝执行：" + resolveError);

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string arg in prefix) psi.ArgumentList.Add(arg);
            for (int i = 1; i < argv.Count; i++) psi.ArgumentList.Add(argv[i]);
            ChildEnvironment.Apply(psi, inheritEnvironment);

            using var process = new Process { StartInfo = psi };
            var stdout = new MemoryStream();
            var stderr = new MemoryStream();
            Task outTask = Task.CompletedTask, errTask = Task.CompletedTask;

            try
            {
                process.Start();
                //收字节不收字符：子进程可能按系统码页（936）输出，也可能被 agent 自己设成 UTF-8，
                //在这里固定一种解码必然把另一种弄成乱码；退出后再按 BOM → 严格 UTF-8 → ANSI 判定。
                outTask = process.StandardOutput.BaseStream.CopyToAsync(stdout);
                errTask = process.StandardError.BaseStream.CopyToAsync(stderr);
            }
            catch (Exception ex)
            {
                return AgentToolResult.Failure($"命令启动失败：{ex.Message}");
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            // 长命令给用户交代：超过 5 秒未返回就把「仍在执行」报给状态行，
            // 免得用户把长命令当成界面卡死（命令在后台线程跑，UI 停顿另有原因，但两者同时出现时分不清）。
            var slow = Task.Run(async () =>
            {
                await Task.Delay(5000, linked.Token);
                int waited = 5;
                while (!linked.IsCancellationRequested)
                {
                    string note = $"命令仍在执行…（已 {waited} 秒）";
                    SlowCommand?.Invoke(note);
                    Progress.Value?.Invoke(note);
                    await Task.Delay(10000, linked.Token);
                    waited += 10;
                }
            }, linked.Token);

            try
            {
                await process.WaitForExitAsync(linked.Token);
                //WaitForExit 只保证进程退出，管道里可能还有没读完的字节
                try { await Task.WhenAll(outTask, errTask).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                if (ct.IsCancellationRequested) throw;

                return AgentToolResult.Failure(
                    $"命令超时（{timeoutSeconds}s），已终止进程树。\n{ToolText.HeadTail(Decode(stdout), 3000)}");
            }
            finally
            {
                // 命令结束就停掉播报：不取消的话它会继续按 10 秒节拍报「仍在执行」，
                // 把早已结束的命令状态留在界面/会话上（正常退出时 timeout 也不会触发）。
                linked.Cancel();
                SlowCommand?.Invoke(null);   // 命令结束/超时/取消：清掉「仍在执行」状态
                Progress.Value?.Invoke(null);
                try { await slow.WaitAsync(TimeSpan.FromSeconds(1)); } catch { }
            }

            string outText = Decode(stdout);
            string errText = Decode(stderr);
            var sb = new StringBuilder();
            sb.Append("exit=").AppendLine(process.ExitCode.ToString());
            if (outText.Length > 0) sb.AppendLine("--- stdout ---").AppendLine(outText.TrimEnd());
            if (errText.Length > 0) sb.AppendLine("--- stderr ---").AppendLine(errText.TrimEnd());

            string output = ToolText.HeadTail(sb.ToString(), 6000);
            return process.ExitCode == 0
                ? AgentToolResult.Success(output)
                : AgentToolResult.Failure(output);
        }

        /// <summary>子进程原始字节 → 文本：BOM / 严格 UTF-8 / 系统 ANSI 依次判定。</summary>
        private static string Decode(MemoryStream stream)
            => stream == null || stream.Length == 0 ? "" : TextCodec.Decode(stream.ToArray()).Text;
    }
}
