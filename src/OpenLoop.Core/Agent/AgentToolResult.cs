namespace OpenLoop.Core.Agent
{
    /// <summary>工具执行结果：成功时 Data 为任意文本（模型可读），失败时 Error 非空。</summary>
    public sealed class AgentToolResult
    {
        public bool Ok { get; init; }
        public string Data { get; init; }
        public string Error { get; init; }

        public static AgentToolResult Success(string data) => new() { Ok = true, Data = data };

        public static AgentToolResult Failure(string error) => new() { Ok = false, Error = error };
    }
}
