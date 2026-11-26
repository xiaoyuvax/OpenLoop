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
        public void Native_tool_channel_drops_the_final_marker()
        {
            string native = SystemPromptBuilder.Build([new NamedTool("ReadFile")], 10, nativeTools: true);
            Assert.DoesNotContain("[final]", native);
            Assert.Contains("不调用工具的那一条回复就是最终回答", native);

            string text = SystemPromptBuilder.Build([new NamedTool("ReadFile")], 10);
            Assert.Contains("在最后一行单独输出 [final]", text);
        }

        [Fact]
        public void Windows_environment_note_only_on_windows()
        {
            string prompt = SystemPromptBuilder.Build([new NamedTool("ReadFile")], 10, workspace: ".");
            if (OperatingSystem.IsWindows())
                Assert.Contains("PowerShell 5.1", prompt);
            else
                Assert.DoesNotContain("PowerShell 5.1", prompt);
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

        [Fact]
        public void Default_body_defines_priority_completion_and_retry_bounds()
        {
            string prompt = SystemPromptBuilder.Build([new NamedTool("ReadFile")], 10, workspace: ".");

            Assert.Contains("# 优先级（冲突时从高到低）", prompt);
            Assert.Contains("# 验证与完成定义", prompt);
            Assert.Contains("三条同时满足才算完成", prompt);
            Assert.Contains("同一失败换参数最多再试两次", prompt);
            // 正文不再自己定义调度格式：并行上限只由协议一节说
            Assert.Equal(1, CountOccurrences(prompt, "一轮最多"));
        }

        [Fact]
        public void Section_without_trailing_newline_does_not_stick_to_previous_section()
        {
            // 注入段忘了以换行收尾时，规则不能被读成上一段的续文（多源拼接的边界必须成立）
            string template = "前言\n\n{{a}}{{b}}\n\n正文\n";
            string rendered = PromptTemplate.Render(template, new Dictionary<string, string>
            {
                ["a"] = "甲段",
                ["b"] = "- 规则一\n- 规则二"
            });

            Assert.Contains("甲段\n\n- 规则一\n- 规则二", rendered);
            Assert.DoesNotContain("甲段- 规则一", rendered);
        }

        [Fact]
        public void Empty_section_leaves_no_dangling_blank_lines()
        {
            string rendered = PromptTemplate.Render("A\n\n{{x}}{{y}}\n\nB\n", new Dictionary<string, string>
            {
                ["x"] = "",
                ["y"] = "  \n"
            });

            Assert.Equal("A\n\nB\n", rendered);
        }

        [Fact]
        public void Unknown_placeholder_stays_visible()
        {
            string rendered = PromptTemplate.Render("{{nope}}\n", new Dictionary<string, string>());
            Assert.Contains("{{nope}}", rendered);
        }

        [Fact]
        public void Inline_placeholder_is_not_wrapped_in_newlines()
        {
            string rendered = PromptTemplate.Render("一轮最多 {{max_parallel}} 个。\n",
                new Dictionary<string, string> { ["max_parallel"] = "4" });

            Assert.Equal("一轮最多 4 个。\n", rendered);
        }

        private static int CountOccurrences(string text, string needle)
        {
            int count = 0, index = 0;
            while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }
    }
}
