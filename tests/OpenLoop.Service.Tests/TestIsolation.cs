using System.Runtime.CompilerServices;
using OpenLoop.Service.Storage;
using Puppet.Core;

namespace OpenLoop.Service.Tests
{
    /// <summary>
    /// 会话落盘默认写到 <c>%APPDATA%\OpenLoop\chats</c>：测试不换根目录的话，
    /// 每跑一次都会往真实索引里塞假会话（还会被后台任务在 Dispose 之后补写）。
    /// </summary>
    internal static class TestIsolation
    {
        /// <summary>进程级静态看板（AgentBook）：上一个测试登记的会话残留到下一个测试，
        /// 会被 AnnouncePeers 当成「同工作区新 peer」多插一条协作提示。模块加载时清一次。</summary>
        [ModuleInitializer]
        internal static void Init()
        {
            ChatHistory.Root = Path.Combine(Path.GetTempPath(),
                "openloop-test-chats-" + Environment.ProcessId);
            AgentBook.Reset();
        }
    }
}
