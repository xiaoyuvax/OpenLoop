using System.Diagnostics;
using System.Text;
using OpenLoop.Core.Messages;

namespace OpenLoop.Core.Reasoning
{
    /// <summary>
    /// 把一次补全交给外部进程：提示词写 stdin，取 stdout 当回答。
    /// 用于把 Loop 的构思外包给别人（规范 §1：参与者可以来自外部）。
    /// 代价：没有多轮结构，只适合一次性构思/评审；成本未知。
    /// </summary>
    public sealed class ProcessReasoner : IReasoner
    {
        private readonly string _fileName;
        private readonly string[] _arguments;
        private readonly TimeSpan _timeout;

        public ProcessReasoner(
            string fileName, IEnumerable<string> arguments = null, TimeSpan? timeout = null)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("外部命令不能为空", nameof(fileName));
            _fileName = fileName;
            _arguments = arguments?.ToArray() ?? [];
            _timeout = timeout ?? TimeSpan.FromMinutes(10);
        }

        public async Task<CompletionResult> CompleteAsync(
            IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        {
            var info = new ProcessStartInfo
            {
                FileName = _fileName,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (string argument in _arguments) info.ArgumentList.Add(argument);

            using var process = new Process { StartInfo = info };
            if (!process.Start()) throw new InvalidOperationException("外部进程无法启动");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_timeout);
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);

            await process.StandardInput.WriteAsync(Render(messages));
            process.StandardInput.Close();

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                throw;
            }

            string output = await outputTask;
            string error = await errorTask;
            if (process.ExitCode != 0)
            {
                string detail = string.IsNullOrWhiteSpace(error) ? output : error;
                throw new InvalidOperationException(
                    $"外部进程退出码 {process.ExitCode}：{(detail.Length <= 500 ? detail : detail[..500] + "…")}");
            }

            return new CompletionResult
            {
                Content = output.Trim(),
                Cost = null,
                FinishReason = "stop"
            };
        }

        private static string Render(IReadOnlyList<LlmMessage> messages)
            => string.Join("\n\n", (messages ?? []).Select(m => m.Content ?? ""));
    }
}
