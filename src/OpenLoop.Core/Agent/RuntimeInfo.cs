using System.Reflection;

namespace OpenLoop.Core.Agent
{
    /// <summary>运行中二进制的版本标识（用于把证据归因到某个 build）。</summary>
    public static class RuntimeInfo
    {
        public static string Version { get; } =
            typeof(AgentLoop).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(AgentLoop).Assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
