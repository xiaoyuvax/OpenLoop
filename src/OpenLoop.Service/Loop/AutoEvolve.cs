using System.Text.Json;
using OpenLoop.Service.Bus;

namespace OpenLoop.Service.Loop
{
    /// <summary>自动演进的冷却/配额记录，落在 <c>&lt;工作区&gt;/.openloop/loop/auto.json</c>。</summary>
    public sealed class AutoEvolveState
    {
        /// <summary>计数归属日（yyyy-MM-dd，本地时间）。</summary>
        public string Day { get; set; } = "";

        /// <summary>当天已自动触发几轮。</summary>
        public int Count { get; set; }

        /// <summary>上次自动触发时间（O 格式）。</summary>
        public string At { get; set; }

        /// <summary>上次触发时的运行记录条数，用来数「新证据」。</summary>
        public int Runs { get; set; }
    }

    /// <summary>
    /// 事件驱动：Worker 每跑完一轮就攒一条证据，够数且不在冷却/配额内就自己开一轮演进。
    /// 判据是纯函数，好测；触发与落盘在 <see cref="LoopService"/>。
    /// </summary>
    public static class AutoEvolve
    {
        public static string PathFor(string workspace)
            => Path.Combine(Path.GetFullPath(workspace), ".openloop", "loop", "auto.json");

        public static AutoEvolveState Load(string workspace)
        {
            try
            {
                string path = PathFor(workspace);
                if (!File.Exists(path)) return new AutoEvolveState();
                return JsonSerializer.Deserialize<AutoEvolveState>(File.ReadAllBytes(path), BusProtocol.Json)
                       ?? new AutoEvolveState();
            }
            catch
            {
                return new AutoEvolveState();
            }
        }

        public static void Save(string workspace, AutoEvolveState state)
        {
            string path = PathFor(workspace);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(state, BusProtocol.Json));
            File.Move(temp, path, true);
        }

        /// <summary>该不该开一轮；<paramref name="reason"/> 是没开时给人看的一句话。</summary>
        public static bool ShouldStart(
            bool enabled, int minRuns, int cooldownMinutes, int perDay,
            AutoEvolveState state, int runs, DateTimeOffset now, out string reason)
        {
            reason = null;
            if (!enabled) { reason = "自动演进未开启"; return false; }

            state ??= new AutoEvolveState();
            if (perDay <= 0) { reason = "每日上限为 0"; return false; }

            string day = now.ToString("yyyy-MM-dd");
            int today = state.Day == day ? state.Count : 0;
            if (today >= perDay)
            {
                reason = $"今天已自动跑 {today} 轮（上限 {perDay}）";
                return false;
            }

            if (DateTimeOffset.TryParse(state.At, out var last))
            {
                var cooldown = TimeSpan.FromMinutes(Math.Max(0, cooldownMinutes));
                if (now - last < cooldown)
                {
                    reason = $"冷却中，{(last + cooldown):HH:mm} 后再试";
                    return false;
                }
            }

            int fresh = runs - state.Runs;
            if (fresh < Math.Max(1, minRuns))
            {
                reason = $"新证据 {Math.Max(0, fresh)}/{Math.Max(1, minRuns)} 条";
                return false;
            }

            return true;
        }
    }
}
