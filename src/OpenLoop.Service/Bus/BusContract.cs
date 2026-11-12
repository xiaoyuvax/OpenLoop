using System.Text.Json;

namespace OpenLoop.Service.Bus
{
    /// <summary>角色常量。A = Worker，I = Loop。</summary>
    public static class BusRoles
    {
        public const string A = "a";
        public const string I = "i";

        public static string Other(string role) =>
            string.Equals(role, A, StringComparison.OrdinalIgnoreCase) ? I : A;
    }

    public static class BusProtocol
    {
        /// <summary>协议号。两个进程必须相等才允许互相更新；不同则拒绝更新并提示一起升级。</summary>
        public const int Version = 1;

        public const string KeyHeader = "X-OpenLoop-Key";

        public static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>/bus/status 的响应体。</summary>
    public sealed class BusStatus
    {
        public Runtime.RuntimeInfo Self { get; set; }
        public Runtime.RuntimeInfo Peer { get; set; }
        public int Protocol { get; set; }
    }
}
