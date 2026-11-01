using System.Diagnostics;
using System.Text.Json;
using OpenLoop.Core.Loop;
using OpenLoop.Core.Store;
using OpenLoop.Service.Bus;

namespace OpenLoop.Service.Worker
{
    /// <summary>
    /// 在 A 的子进程里跑一题（<c>OpenLoop.A.exe --eval-run</c>）。
    /// A 与 I 版本分叉时用它，保证考的是「被演进的那个二进制」。
    /// </summary>
    public sealed class ProcessTaskRunner : ITaskRunner
    {
        private readonly string _exe;
        private readonly string _workspace;
        private readonly string _evidenceRoot;
        private readonly IReadOnlyDictionary<string, FeatureRecord> _features;
        private readonly string _model;
        private readonly string _effort;

        public ProcessTaskRunner(string exe, string workspace, string evidenceRoot,
            IReadOnlyDictionary<string, FeatureRecord> features, string model, string effort)
        {
            _exe = exe;
            _workspace = workspace;
            _evidenceRoot = evidenceRoot;
            _features = features ?? new Dictionary<string, FeatureRecord>(StringComparer.Ordinal);
            _model = model;
            _effort = effort;
        }

        public async Task<TaskOutcome> RunAsync(EvalTask task, ActivationRecord activation, CancellationToken ct)
        {
            string dir = Path.Combine(_evidenceRoot, "runs",
                $"{task.Id}-v{activation.Version}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
            Directory.CreateDirectory(dir);

            string specPath = Path.Combine(dir, "spec.json");
            File.WriteAllBytes(specPath, JsonSerializer.SerializeToUtf8Bytes(new EvalRunSpec
            {
                Workspace = _workspace,
                Model = _model,
                Effort = _effort,
                Activation = activation,
                Features = _features.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                Task = task
            }, BusProtocol.Json));

            var startInfo = new ProcessStartInfo(_exe) { UseShellExecute = false };
            startInfo.ArgumentList.Add("--eval-run");
            startInfo.ArgumentList.Add(specPath);
            startInfo.ArgumentList.Add("--out");
            startInfo.ArgumentList.Add(dir);

            using var process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException("无法启动 " + _exe);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw;
            }

            string detail = process.ExitCode switch
            {
                0 => "通过",
                1 => "未通过",
                _ => "执行失败（退出码 " + process.ExitCode + "）"
            };

            var run = ReadRun(dir);
            return new TaskOutcome
            {
                Passed = process.ExitCode == 0,
                Detail = run?.Detail ?? detail,
                Steps = run?.Steps ?? 0,
                Cost = run?.Cost,
                EvidenceDir = Path.GetRelativePath(_workspace, dir).Replace('\\', '/')
            };
        }

        private static RunJson ReadRun(string dir)
        {
            try
            {
                string path = Path.Combine(dir, "run.json");
                return File.Exists(path)
                    ? JsonSerializer.Deserialize<RunJson>(File.ReadAllBytes(path), BusProtocol.Json)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private sealed class RunJson
        {
            public bool Passed { get; set; }
            public string Detail { get; set; }
            public decimal? Cost { get; set; }
            public int Steps { get; set; }
        }
    }
}
