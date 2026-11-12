using OpenLoop.Core.Agent;
using Xunit;

namespace OpenLoop.Core.Tests
{
    /// <summary>界面步骤流的显示行：` &gt;&gt; 工具[调用内容]`，原生工具要压成「类型 + 调用内容」，
    /// 不能出现 0ms 假耗时；前缀和方括号让正则不会把正文里的函数调用误认成工具行。</summary>
    public class AgentStepRecordTests
    {
        [Fact]
        public void Normal_tool_line_is_unchanged()
        {
            var step = new AgentStepRecord
            {
                Tool = "ListDir",
                ArgsSummary = "path=., recursive=False",
                Ok = true,
                DurationMs = 10
            };

            Assert.Equal(" >> ListDir[path=., recursive=False] · 10ms", step.DisplayLine);
        }

        [Fact]
        public void Native_read_shows_type_and_short_path()
        {
            var step = new AgentStepRecord
            {
                Tool = "native:read",
                ArgsFull = """{"path":"C:\\Users\\Xiaoyu\\source\\repos\\DefocusMirror\\README.md"}""",
                Ok = true
            };

            Assert.Equal(" >> > Read[path=…\\repos\\DefocusMirror\\README.md]", step.DisplayLine);
        }

        [Fact]
        public void Native_grep_keeps_two_arguments_and_failure_mark()
        {
            var step = new AgentStepRecord
            {
                Tool = "native:grep",
                ArgsFull = """{"pattern":"FastColoredTextBox","path":"DefocusMirror"}""",
                Ok = false
            };

            Assert.Equal(" >> > Grep[pattern=FastColoredTextBox, path=DefocusMirror] · 失败", step.DisplayLine);
        }

        [Fact]
        public void Native_shell_shows_the_command_and_duration()
        {
            var step = new AgentStepRecord
            {
                Tool = "native:shell",
                ArgsFull = """{"command":"git status --short"}""",
                Ok = true,
                DurationMs = 1200
            };

            Assert.Equal(" >> > Shell[command=git status --short] · 1.2s", step.DisplayLine);
        }

        [Fact]
        public void Broken_native_arguments_stay_readable()
        {
            var step = new AgentStepRecord
            {
                Tool = "native:read",
                ArgsFull = "{\"path\":",
                Ok = false
            };

            Assert.Equal(" >> > Read[{\"path\":] · 失败", step.DisplayLine);
        }
    }
}
