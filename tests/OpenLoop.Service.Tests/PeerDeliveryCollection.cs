using System.Reflection;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>
    /// 私信投递链路会改全局静态（<c>PeerDelivery.Local</c>、<c>PuppetBridge.LocalHub/SelfRole</c>、
    /// 留言板 AgentBook）：这些测试放进同一个集合串行跑，否则并行时会互相踩掉对方的投递入口。
    /// </summary>
    [CollectionDefinition("peer-delivery", DisableParallelization = true)]
    public sealed class PeerDeliveryCollection
    {
    }
}
