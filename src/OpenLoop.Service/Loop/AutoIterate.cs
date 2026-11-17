using System.Text.Json;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Loop
{
    /// <summary>
    /// 自动迭代的进度（落在 <c>runtime/auto-iterate.json</c>）：
    /// 基准是上次换版时的提交，之后攒够新提交才值得再换一版。
    /// </summary>
    public sealed class AutoIterateState
    {
        /// <summary>上次换版时的 HEAD；空表示还没记录过基准。</summary>
        public string Baseline { get; set; } = "";

        /// <summary>用户选「跳过」的那次 HEAD：没有新提交就不再提示。</summary>
        public string Skipped { get; set; } = "";

        /// <summary>上次开始换版的时间（O 格式）；失败后要等冷却。</summary>
        public string AttemptAt { get; set; } = "";

        /// <summary>最近一次检测到的 HEAD。</summary>
        public string Head { get; set; } = "";

        /// <summary>基准之后的新提交数。</summary>
        public int Commits { get; set; }

        /// <summary>现在值不值得自动换版（壳只认这个）。</summary>
        public bool Available { get; set; }

        public string Summary { get; set; }
        public string At { get; set; }
    }

    /// <summary>
    /// 自动迭代的判据：源码提交攒够、工作树干净、不在冷却里、没被用户跳过。
    /// 与演进采纳无关——演进改激活集，迭代发二进制，两条线各走各的。
    /// 判定是纯函数（<see cref="ShouldOffer"/>），git 与落盘在 <see cref="IterationService"/>。
    /// </summary>
    public static class AutoIterate
    {
        /// <summary>攒够几次提交才值得换一版；太少会频繁打断会话。</summary>
        public const int MinCommits = 3;

        /// <summary>上次开始换版后多久才再试一次（失败时不原地重试）。</summary>
        public const int CooldownMinutes = 60;

        public static string StatePath => Path.Combine(AppPaths.RuntimeDir, "auto-iterate.json");

        public static AutoIterateState Load()
        {
            try
            {
                if (!File.Exists(StatePath)) return new AutoIterateState();
                return JsonSerializer.Deserialize<AutoIterateState>(File.ReadAllBytes(StatePath), BusProtocol.Json)
                       ?? new AutoIterateState();
            }
            catch
            {
                return new AutoIterateState();
            }
        }

        public static void Save(AutoIterateState state)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.RuntimeDir);
                string temp = StatePath + ".tmp";
                File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(state, BusProtocol.Json));
                File.Move(temp, StatePath, true);
            }
            catch
            {
            }
        }

        /// <summary>该不该提示用户换版；<paramref name="reason"/> 是没提示时给人看的一句话。</summary>
        public static bool ShouldOffer(
            bool enabled, int commits, string head, AutoIterateState state, DateTimeOffset now, out string reason)
        {
            reason = null;
            if (!enabled) { reason = "自动迭代未开启"; return false; }
            if (string.IsNullOrWhiteSpace(head)) { reason = "没有 git 提交"; return false; }
            if (commits < MinCommits) { reason = $"新提交 {commits}/{MinCommits} 次"; return false; }

            state ??= new AutoIterateState();
            if (string.Equals(state.Skipped, head, StringComparison.OrdinalIgnoreCase))
            {
                reason = "用户跳过了这一次";
                return false;
            }

            if (DateTimeOffset.TryParse(state.AttemptAt, out var attempt))
            {
                var cooldown = TimeSpan.FromMinutes(CooldownMinutes);
                if (now - attempt < cooldown)
                {
                    reason = $"上次尝试在 {attempt:HH:mm}，冷却中";
                    return false;
                }
            }

            return true;
        }

        /// <summary>开始换版：记下时间，失败后走冷却。</summary>
        public static void NoteAttempt(string head)
        {
            var state = Load();
            state.AttemptAt = DateTimeOffset.Now.ToString("O");
            if (!string.IsNullOrWhiteSpace(head)) state.Head = head;
            Save(state);
        }

        /// <summary>用户选了跳过：同一次 HEAD 不再提示。</summary>
        public static void Skip(string head)
        {
            var state = Load();
            state.Skipped = head ?? "";
            Save(state);
        }
    }
}
