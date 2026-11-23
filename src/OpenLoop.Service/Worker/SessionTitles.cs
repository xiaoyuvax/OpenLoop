using System.Text;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Safety;
using OpenLoop.Service.Config;
using OpenLoop.Service.Models;

namespace OpenLoop.Service.Worker
{
    /// <summary>
    /// 会话标题的总结：新会话首轮成功后自动起标题、界面上手动重算，以及 Web / WinForms 两侧，
    /// 都用这一套提示词、输入拼装与清洗——别在界面里再抄一份。
    /// </summary>
    public static class SessionTitles
    {
        /// <summary>标题模型只吐标题本身：12 个汉字以内，不带引号与解释。</summary>
        public const string Prompt = "你给对话起标题。只输出标题本身：不超过 12 个汉字，不加引号、不加句号、不解释。";

        /// <summary>参与总结的轮数上限与每段截断长度：够判断主题即可，别把整段历史送进去。</summary>
        public const int MaxTurns = 4;
        public const int MaxChars = 300;

        /// <summary>清洗后的标题长度上限（汉字）。</summary>
        public const int MaxLength = 20;

        /// <summary>给标题模型的完整请求：系统提示词 + 最近几轮问答。</summary>
        public static List<LlmMessage> Messages(IReadOnlyList<(string User, string Assistant)> turns) =>
        [
            new("system", Prompt),
            new("user", Input(turns))
        ];

        /// <summary>给标题模型的输入：最近几轮问答，助手正文截断，够判断主题即可。</summary>
        public static string Input(IReadOnlyList<(string User, string Assistant)> turns)
        {
            if (turns == null || turns.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var (user, assistant) in turns.Skip(Math.Max(0, turns.Count - MaxTurns)))
            {
                sb.Append("用户：").AppendLine(Clip(user));
                sb.Append("助手：").AppendLine(Clip(assistant));
            }
            return sb.ToString();
        }

        public static string Clip(string text, int max = MaxChars)
            => string.IsNullOrEmpty(text) || text.Length <= max ? text ?? "" : text[..max] + "…";

        /// <summary>取第一行、去引号/井号/星号、去句末标点，超长截断。</summary>
        public static string Clean(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string line = raw.Replace("\r", "\n").Split('\n').Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0) ?? "";
            line = line.Trim('"', '\'', '“', '”', '‘', '’', '「', '」', '#', '*', ' ');
            line = line.TrimEnd('。', '.', '：', ':');
            return line.Length <= MaxLength ? line : line[..MaxLength];
        }

        /// <summary>
        /// 调一次模型拿标题：渠道与聊天一致（dsh / opencode / OpenAI 兼容），所以会话换模型时
        /// 标题也能跟着换。失败或没给出可用标题时返回空串，由调用方决定要不要提示。
        /// </summary>
        public static async Task<string> SummarizeAsync(AppSettings settings, string workspace, string model,
            string effort, IReadOnlyList<(string User, string Assistant)> turns, CancellationToken ct = default)
        {
            if (settings == null || string.IsNullOrWhiteSpace(model) || turns == null || turns.Count == 0)
                return "";
            var reasoner = Reasoners.Create(settings, model, effort, workspace, _ => ApprovalDecision.Deny);
            var completion = await reasoner.CompleteAsync(Messages(turns), ct).ConfigureAwait(false);
            return Clean(completion?.Content);
        }
    }

    /// <summary>标题接口的响应：空串 = 这次没生成（已有标题且 force=false，或模型没给出可用标题）。</summary>
    public sealed class WorkerTitleResult
    {
        public string Title { get; set; } = "";
    }

    /// <summary>标题通道的输入：测试替换通道实现时用，不必真调模型。</summary>
    public sealed class WorkerTitleRequest
    {
        public AppSettings Settings { get; init; }
        public string Workspace { get; init; }
        public string Model { get; init; }
        public string Effort { get; init; }
        public IReadOnlyList<(string User, string Assistant)> Turns { get; init; }
    }
}
