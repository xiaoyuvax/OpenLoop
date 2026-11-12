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
    }
}
