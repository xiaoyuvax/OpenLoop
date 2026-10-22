using System.Text;
using System.Text.Json;

namespace OpenLoop.Core.Store
{
    /// <summary>
    /// 账本：append-only 的 JSONL + 一次写定的版本文件。
    /// 三条契约在此落地——id 不复用、证据不重写、顺序不重排。
    /// ponytail: 单进程访问；同一目录被多进程同时写时再加文件锁。
    /// </summary>
    public sealed class Ledger
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };

        private static readonly JsonSerializerOptions LineJson = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };

        private readonly object _gate = new();

        private Ledger(string workspace) => Workspace = workspace;

        public static Ledger Open(string workspace)
        {
            if (string.IsNullOrWhiteSpace(workspace)) throw new ArgumentException("工作目录不能为空", nameof(workspace));
            return new Ledger(Path.GetFullPath(workspace));
        }

        public string Workspace { get; }
        public string Root => Path.Combine(Workspace, ".openloop");
        public string LedgerPath => Path.Combine(Root, "ledger.jsonl");
        public string DefsDir => Path.Combine(Root, "defs");
        public string EvalDir => Path.Combine(Root, "eval");

        public void EnsureInitialized()
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Root);
                Directory.CreateDirectory(DefsDir);
                Directory.CreateDirectory(EvalDir);

                foreach (var feature in FeatureCatalog.SeedFeatures())
                    if (FindFeature(feature.Id) == null) AppendFeature(feature);

                if (Activations().Count == 0) WriteActivation(FeatureCatalog.SeedActivation());
            }
        }

        public IReadOnlyList<FeatureRecord> Features()
        {
            var list = new List<FeatureRecord>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in ReadEntries())
            {
                if (entry.Kind != "feature") continue;
                var record = entry.Body.Deserialize<FeatureRecord>(Json);
                if (record?.Id == null || !seen.Add(record.Id)) continue;
                list.Add(record);
            }
            return list;
        }

        public FeatureRecord FindFeature(string id)
            => Features().FirstOrDefault(f => f.Id == id);

        public IReadOnlyList<ActivationRecord> Activations()
        {
            if (!Directory.Exists(DefsDir)) return [];
            var list = new List<ActivationRecord>();
            foreach (string file in Directory.GetFiles(DefsDir, "v*.json"))
            {
                ActivationRecord record;
                try
                {
                    record = JsonSerializer.Deserialize<ActivationRecord>(File.ReadAllText(file, Encoding.UTF8), Json);
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException($"版本文件无法解析：{file}", ex);
                }
                if (record != null) list.Add(record);
            }
            list.Sort((a, b) => a.Version.CompareTo(b.Version));
            return list;
        }

        public ActivationRecord Current => Activations().LastOrDefault();

        public void AppendFeature(FeatureRecord feature)
        {
            if (feature == null || string.IsNullOrWhiteSpace(feature.Id))
                throw new ArgumentException("特征必须有 id", nameof(feature));
            lock (_gate)
            {
                if (FindFeature(feature.Id) != null)
                    throw new InvalidOperationException($"特征 id 已被使用：{feature.Id}（id 永不复用，改内容请新增版本 id）");
                Append("feature", feature);
            }
        }

        public ActivationRecord Activate(
            IReadOnlyList<FeatureRef> features, string by, string note,
            IReadOnlyList<string> evidence = null)
        {
            if (features == null || features.Count == 0)
                throw new ArgumentException("激活集不能为空", nameof(features));

            lock (_gate)
            {
                var known = Features().Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var feature in features)
                    if (feature?.Id == null || !known.Contains(feature.Id))
                        throw new InvalidOperationException($"激活引用了不存在的特征：{feature?.Id}");

                var current = Current;
                var record = new ActivationRecord
                {
                    Version = (current?.Version ?? 0) + 1,
                    At = Now(),
                    By = by ?? "user",
                    Note = note,
                    Features = features.ToList()
                };
                WriteActivation(record);
                return record;
            }
        }

        public ActivationRecord Rollback(
            int version, string by = "user", string note = null,
            IReadOnlyList<string> evidence = null)
        {
            lock (_gate)
            {
                var target = Activations().FirstOrDefault(a => a.Version == version)
                    ?? throw new ArgumentException($"账本里没有 v{version}", nameof(version));
                var current = Current;

                var record = new ActivationRecord
                {
                    Version = current.Version + 1,
                    At = Now(),
                    By = by,
                    Note = note ?? $"回滚到 v{version}",
                    Features = target.Features
                };
                WriteActivation(record);

                AppendVerdict(new VerdictRecord
                {
                    Id = NewId("V"),
                    Decision = "rolled-back",
                    BaselineVersion = current.Version,
                    ResultVersion = record.Version,
                    Judge = by,
                    Note = record.Note,
                    Evidence = evidence?.ToList() ?? [],
                    At = Now()
                });
                return record;
            }
        }

        public void AppendVerdict(VerdictRecord verdict)
        {
            if (verdict == null) throw new ArgumentNullException(nameof(verdict));
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(verdict.Id))
                    throw new ArgumentException("裁决记录必须有 id", nameof(verdict));
                if (Verdicts().Any(v => v.Id == verdict.Id))
                    throw new InvalidOperationException($"裁决 id 已被使用：{verdict.Id}");
                Append("verdict", verdict);
            }
        }

        public IReadOnlyList<VerdictRecord> Verdicts()
        {
            var list = new List<VerdictRecord>();
            foreach (var entry in ReadEntries())
            {
                if (entry.Kind != "verdict") continue;
                var record = entry.Body.Deserialize<VerdictRecord>(Json);
                if (record != null) list.Add(record);
            }
            return list;
        }

        public void AppendRun(RunRecord run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(run.Id))
                    throw new ArgumentException("运行记录必须有 id", nameof(run));
                Append("run", run);
            }
        }

        public IReadOnlyList<RunRecord> Runs()
        {
            var list = new List<RunRecord>();
            foreach (var entry in ReadEntries())
            {
                if (entry.Kind != "run") continue;
                var record = entry.Body.Deserialize<RunRecord>(Json);
                if (record != null) list.Add(record);
            }
            return list;
        }

        /// <summary>落盘一个未启用的候选（休眠）：不通过也留证据，但不影响当前版本。</summary>
        public string SaveCandidate(ActivationRecord candidate)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            string dir = Path.Combine(EvalDir, "candidates");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"C-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(candidate, Json), new UTF8Encoding(false));
            return Path.GetRelativePath(Workspace, path).Replace('\\', '/');
        }

        public static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

        private void WriteActivation(ActivationRecord record)
        {
            string path = Path.Combine(DefsDir, $"v{record.Version}.json");
            if (File.Exists(path))
                throw new InvalidOperationException($"版本 v{record.Version} 已存在，不能覆盖");
            Directory.CreateDirectory(DefsDir);
            File.WriteAllText(path, JsonSerializer.Serialize(record, Json), new UTF8Encoding(false));
        }

        private void Append(string kind, object body)
        {
            Directory.CreateDirectory(Root);
            string line = JsonSerializer.Serialize(new { kind, body }, LineJson);
            File.AppendAllText(LedgerPath, line + "\n", new UTF8Encoding(false));
        }

        private IEnumerable<(string Kind, JsonElement Body)> ReadEntries()
        {
            if (!File.Exists(LedgerPath)) yield break;
            foreach (string line in File.ReadAllLines(LedgerPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                string kind = root.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String
                    ? k.GetString()
                    : null;
                if (kind == null || !root.TryGetProperty("body", out var body)) continue;
                yield return (kind, body.Clone());
            }
        }

        private static string Now() => DateTimeOffset.UtcNow.ToString("O");
    }
}
