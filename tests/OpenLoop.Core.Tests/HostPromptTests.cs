using OpenLoop.Core.Agent;
using Xunit;

namespace OpenLoop.Core.Tests
{
    /// <summary>宿主拼在用户消息前面的说明块：切出前缀，正文只留用户自己写的那段。</summary>
    public class HostPromptTests
    {
        [Fact]
        public void Split_target_directive()
        {
            string text = HostPrompt.TargetLead + "用户正看着左栏「AGENTS.md」面板\r\n路径：C:\\a.md\r\n\r\n检查不必要的项目";
            var (host, body) = HostPrompt.Split(text);
            Assert.Equal(HostPrompt.TargetLead + "用户正看着左栏「AGENTS.md」面板\r\n路径：C:\\a.md", host);
            Assert.Equal("检查不必要的项目", body);
        }

        [Fact]
        public void Split_skill_lead_then_target_directive()
        {
            string skill = HostPrompt.SkillLead + "ponytail」：先调用 ReadSkill 读取该技能的做法全文，再按技能执行。";
            string target = HostPrompt.TargetLead + "说明";
            var (host, body) = HostPrompt.Split(skill + "\n\n" + target + "\n\n帮我改这段");
            Assert.Equal(skill + "\n\n" + target, host);
            Assert.Equal("帮我改这段", body);
        }

        [Fact]
        public void Split_plain_text_is_untouched()
        {
            var (host, body) = HostPrompt.Split("普通问题\n\n带一个空行");
            Assert.Equal("", host);
            Assert.Equal("普通问题\n\n带一个空行", body);
        }

        [Fact]
        public void Split_without_user_body_keeps_text_as_is()
        {
            string text = HostPrompt.TargetLead + "只有宿主说明";
            var (host, body) = HostPrompt.Split(text);
            Assert.Equal("", host);
            Assert.Equal(text, body);
        }
    }
}
