using OpenLoop.Service.OpenUI;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>
    /// 会话边界的结构约束（docs/DESIGN-OPENUI.md §9）：寻址用的实例名由 owner 拼出，
    /// 页面与模型都没有「换一个 owner」的表达方式。
    ///
    /// 页面能调的 action 全部定义在壳的 <c>BrowserView</c>（WinForms 项目，测试项目不引用它），
    /// 那边靠签名自查 + 设计文档 §9.2 约束，这里钉住 agent 侧的这一半。
    /// </summary>
    public sealed class LoopAppBoundaryTests
    {
        [Fact]
        public void The_instance_name_is_derived_from_the_owner_only()
        {
            Assert.Equal("LoopApp-abc12345", ShellLoopAppHost.InstanceName("abc12345"));
            Assert.Equal("LoopApp-abc12345", ShellLoopAppHost.InstanceName("  abc12345  "));
            Assert.Equal("LoopApp-unknown", ShellLoopAppHost.InstanceName(""));
            Assert.Equal("LoopApp-unknown", ShellLoopAppHost.InstanceName(null));
        }

        [Fact]
        public void Two_sessions_never_share_an_instance()
        {
            Assert.NotEqual(ShellLoopAppHost.InstanceName("sess0001"), ShellLoopAppHost.InstanceName("sess0002"));
        }
    }
}
