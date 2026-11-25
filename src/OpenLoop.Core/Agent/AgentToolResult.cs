namespace OpenLoop.Core.Agent
{
    /// <summary>一张随工具结果返回的图片（内存 base64，不落盘）。</summary>
    public sealed class ToolImage
    {
        public string MimeType { get; init; } = "image/png";
        public string Base64 { get; init; }

        /// <summary>数据 URL（线上消息的 image_url 用）。</summary>
        public string DataUrl => "data:" + MimeType + ";base64," + Base64;
    }

    public sealed class AgentToolResult
    {
        public bool Ok { get; init; }
        public string Data { get; init; }
        public string Error { get; init; }

        /// <summary>随结果返回的图片（走模型附件通道，不进正文；未接附件通道的模型拿占位文本）。</summary>
        public IReadOnlyList<ToolImage> Images { get; init; }

        public static AgentToolResult Success(string data) => new() { Ok = true, Data = data };

        public static AgentToolResult Success(string data, IReadOnlyList<ToolImage> images)
            => new() { Ok = true, Data = data, Images = images };

        public static AgentToolResult Failure(string error) => new() { Ok = false, Error = error };
    }
}
