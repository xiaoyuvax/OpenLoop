using OpenLoop.Core.Agent;

namespace OpenLoop.Core.Messages
{
    /// <summary>最小消息类型：角色 + 纯文本。工具调用与结果以文本形式承载；
    /// 图片走 <see cref="Images"/>（线上翻译成附件 content part，不进正文、不计截断）。</summary>
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

        /// <summary>本条消息附带的图片（当前只用于 tool-result 行；清空即丢弃图片）。</summary>
        public List<ToolImage> Images { get; set; }
    }
}
