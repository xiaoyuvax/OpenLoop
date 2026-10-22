namespace OpenLoop.Core.Messages
{
    /// <summary>最小消息类型：角色 + 纯文本。工具调用与结果都以文本形式承载。</summary>
    public sealed class LlmMessage
    {
        public LlmMessage()
        {
        }

        public LlmMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }

        public string Role { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;
    }
}
