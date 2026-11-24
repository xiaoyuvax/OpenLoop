using OpenLoop.Core.Agent;
using OpenLoop.Core.Tools;
using Xunit;

namespace OpenLoop.Core.Tests
{
    public class SystemPromptBuilderTests
    {
        private sealed class NamedTool(string name) : ITool
        {
            public string Name => name;
            public string Description => "test";
            public string ParamsSchema => "";
            public bool IsReadOnly => true;
            public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
                => Task.FromResult(AgentToolResult.Success(""));
        }

        [Fact]
        public void Puppet_tool_adds_the_process_architecture_section()
        {
            string prompt = SystemPromptBuilder.Build([new NamedTool("Puppet")], 10);
            Assert.Contains("# 进程与 Puppet", prompt);
            Assert.Contains("target=self", prompt);
        }

        [Fact]
        public void Without_the_puppet_tool_there_is_no_architecture_section()
        {
            string prompt = SystemPromptBuilder.Build([new NamedTool("ReadFile")], 10);
            Assert.DoesNotContain("# 进程与 Puppet", prompt);
        }

        [Fact]
        public void Default_template_fills_every_placeholder()
        {
            string prompt = SystemPromptBuilder.Build([new NamedTool("ReadFile")], 10, workspace: ".");

            Assert.DoesNotContain("{{", prompt);
            Assert.Contains("# 任务流程", prompt);
            Assert.Contains("# 工具调用协议", prompt);
            Assert.Contains("一轮最多 4 个", prompt);
        }

        [Fact]
        public void Custom_template_that_drops_sections_still_gets_protocol_and_safety()
        {
            string prompt = SystemPromptBuilder.Build([new NamedTool("ReadFile")], 10,
                template: "只写角色。\n\n{{role}}\n");

            Assert.StartsWith("只写角色。", prompt);
            Assert.Contains("# 工具调用协议", prompt);
            Assert.Contains("# 安全与纪律", prompt);
        }
    }
}
