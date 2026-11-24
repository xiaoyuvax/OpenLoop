using System.Text.Json;

namespace OpenLoop.Core.Store
{
    /// <summary>
    /// 工作区的 Harness 生效方式（.openloop/loop/harness.json）：
    /// direct = 用户直配，改动下一个会话直接生效；
    /// eval = A/B 评测，Prompt / AGENTS.md 的改动先暂存为候选包，过考核题并严格更好才激活。
    /// </summary>
    public static class HarnessMode
    {
        public const string Direct = "direct";
        public const string Eval = "eval";

        public static string PathFor(string workspace)
            => Path.Combine(Path.GetFullPath(workspace), ".openloop", "loop", "harness.json");

        public static string Get(string workspace)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return Direct;
            try
            {
                string path = PathFor(workspace);
                if (!File.Exists(path)) return Direct;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                string mode = doc.RootElement.TryGetProperty("mode", out var value)
                              && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
                return mode == Eval ? Eval : Direct;
            }
            catch
            {
                return Direct;
            }
        }

        public static bool IsEval(string workspace) => Get(workspace) == Eval;

        public static void Set(string workspace, string mode)
        {
            if (string.IsNullOrWhiteSpace(workspace))
                throw new ArgumentException("工作区不能为空", nameof(workspace));
            string normalized = mode == Eval ? Eval : Direct;
            string path = PathFor(workspace);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,
                JsonSerializer.Serialize(new { mode = normalized }),
                new System.Text.UTF8Encoding(false));
        }
    }
}
