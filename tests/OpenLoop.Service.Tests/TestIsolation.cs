using System.Runtime.CompilerServices;
using OpenLoop.Service.Storage;

namespace OpenLoop.Service.Tests
{
    internal static class TestIsolation
    {
        /// <summary>
        /// 会话落盘默认写到 <c>%APPDATA%\OpenLoop\chats</c>：测试不换根目录的话，
        /// 每跑一次都会往真实索引里塞假会话（还会被后台任务在 Dispose 之后补写）。
        /// </summary>
        [ModuleInitializer]
        internal static void Init()
            => ChatHistory.Root = Path.Combine(Path.GetTempPath(),
                "openloop-test-chats-" + Environment.ProcessId);
    }
}
