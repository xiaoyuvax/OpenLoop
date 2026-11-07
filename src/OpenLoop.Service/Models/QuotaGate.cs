using OpenLoop.Service.Logging;

namespace OpenLoop.Service.Models
{
    /// <summary>
    /// 额度/可用性闸门：最近一次失败像「额度不够」就冷却一段时间，期间不把对应工具暴露给 agent。
    /// 各家没有统一错误码，只能靠错误文本启发式；宁可多留一点余量。
    /// </summary>
    internal sealed class QuotaGate(string label, TimeSpan cooldown)
    {
        private long _untilTicks;

        public bool Open => DateTimeOffset.UtcNow.UtcDateTime.Ticks >= Interlocked.Read(ref _untilTicks);

        public void Note(string message)
        {
            if (!LooksLikeQuota(message)) return;
            Interlocked.Exchange(ref _untilTicks, DateTimeOffset.UtcNow.Add(cooldown).UtcDateTime.Ticks);
            AppLog.Warn($"{label} 额度/可用性异常，{cooldown.TotalMinutes:0} 分钟内不再提供它的任务工具：{message}");
        }

        public static bool LooksLikeQuota(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            foreach (string signal in new[] { "402", "quota", "usage limit", "insufficient", "credits", "额度", "余额" })
                if (message.Contains(signal, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>按这一轮实际用的模型记失败：两条外部链路各有自己的闸门。</summary>
        public static void NoteFor(string model, string message)
        {
            if (Core.Acp.DshAcpIds.IsDsh(model)) DshTools.Gate.Note(message);
            else if (model != null && model.StartsWith(Core.OpenCode.OpenCodeModelId.Prefix, StringComparison.OrdinalIgnoreCase))
                OpenCodeTools.Gate.Note(message);
        }
    }
}
