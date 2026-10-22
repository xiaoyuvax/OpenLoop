using OpenLoop.Core.Agent;

namespace OpenLoop.Core.Tools
{
    /// <summary>
    /// 工具契约。库只认这个接口；具体工具由宿主注册。
    /// <see cref="IsReadOnly"/> 供"只读侦察"护栏使用，不再在循环里硬编码工具名单。
    /// </summary>
    public interface ITool
    {
        string Name { get; }
        string Description { get; }
        string ParamsSchema { get; }
        bool IsReadOnly { get; }
        Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct);
    }
}
