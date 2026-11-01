using System.Text.Json;
using OpenLoop.Core.Loop;
using OpenLoop.Core.Store;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Config;
using OpenLoop.Service.Models;

namespace OpenLoop.Service.Worker
{
    /// <summary>一次性考核：<c>OpenLoop.A.exe --eval-run spec.json --out dir</c> 的输入。</summary>
    public sealed class EvalRunSpec
    {
        public string Workspace { get; set; }
        public string Model { get; set; }
        public string Effort { get; set; }
        public ActivationRecord Activation { get; set; }
        public Dictionary<string, FeatureRecord> Features { get; set; } = new(StringComparer.Ordinal);
        public EvalTask Task { get; set; }
    }

    /// <summary>
    /// 让「被演进的那个二进制」自己跑一题：I 只出题与判据，执行发生在 A 里。
    /// 退出码：0 = 通过，1 = 没通过，2 = 出错。
    /// </summary>
    public static class EvalRun
    {
        public static bool Wants(string[] args) => Arg(args, "--eval-run") != null;

        public static int Execute(string[] args)
        {
            string specPath = Arg(args, "--eval-run");
            string outDir = Arg(args, "--out");

            try
            {
                var spec = JsonSerializer.Deserialize<EvalRunSpec>(File.ReadAllBytes(specPath), BusProtocol.Json);
                if (spec?.Task == null || string.IsNullOrWhiteSpace(spec.Workspace))
                    throw new InvalidOperationException("spec 缺 workspace 或 task");

                string workspace = Path.GetFullPath(spec.Workspace);
                var settings = AppSettings.Load();
                var features = spec.Features is { Count: > 0 }
                    ? spec.Features
                    : FeatureCatalog.SeedFeatures().ToDictionary(f => f.Id, StringComparer.Ordinal);
                var activation = spec.Activation ?? FeatureCatalog.SeedActivation();
                var runner = new AgentTaskRunner(
                    dir => Reasoners.Create(settings, spec.Model, spec.Effort, dir),
                    features,
                    workspace, Path.Combine(workspace, ".openloop", "eval"),
                    AppSettings.GlobalAgentsPath, settings.UseAgentsMd,
                    AppSettings.GlobalSkillsPath, ModelCatalog.ContextLimit(spec.Model));

                var outcome = runner.RunAsync(spec.Task, activation, CancellationToken.None)
                    .GetAwaiter().GetResult();

                if (!string.IsNullOrWhiteSpace(outDir))
                    CopyEvidence(workspace, outcome.EvidenceDir, outDir);

                Console.WriteLine($"[eval] {spec.Task.Id} passed={outcome.Passed} steps={outcome.Steps} {outcome.Detail}");
                return outcome.Passed ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[eval] " + ex);
                return 2;
            }
        }

        private static void CopyEvidence(string workspace, string evidenceDir, string outDir)
        {
            if (string.IsNullOrWhiteSpace(evidenceDir)) return;
            string source = Path.GetFullPath(Path.Combine(workspace, evidenceDir));
            Directory.CreateDirectory(outDir);
            foreach (string name in new[] { "final.md", "run.json" })
            {
                string file = Path.Combine(source, name);
                if (File.Exists(file)) File.Copy(file, Path.Combine(outDir, name), true);
            }
        }

        private static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
