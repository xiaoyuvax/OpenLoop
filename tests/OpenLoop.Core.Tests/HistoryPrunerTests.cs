using System.Linq;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Messages;
using Xunit;

namespace OpenLoop.Core.Tests
{
    public class HistoryPrunerTests
    {
        private static LlmMessage U(string content) => new("user", content);
        private static LlmMessage A(string content) => new("assistant", content);

        private static string Call(string tool, string argsJson)
            => $"tool-call: {{\"tool\":\"{tool}\",\"args\":{argsJson}}}";

        private static string Ok(string data)
            => $"tool-result: {{\"ok\":true,\"data\":\"{data}\"}}";

        private static string Fail(string error)
            => $"tool-result: {{\"ok\":false,\"error\":\"{error}\"}}";

        private static List<LlmMessage> Messages() =>
            new() { U("任务"), A(Call("echo", "{\"x\":\"1\"}")), U(Fail("boom")),
                    A(Call("echo", "{\"x\":\"1\"}")), U(Ok("done")), A("最终答案") };

        [Fact]
        public void FailedAttempSupersededBySuccessIsRemoved()
        {
            var messages = Messages();

            int changed = HistoryPruner.Prune(messages);

            Assert.Equal(2, changed);
            Assert.Equal(4, messages.Count);
            Assert.Equal("assistant", messages[1].Role);
            Assert.Contains("\"ok\":true", messages[2].Content);
            Assert.Equal("最终答案", messages[^1].Content);
        }

        [Fact]
        public void FailedAttemptWithoutLaterSuccessIsKept()
        {
            var messages = new List<LlmMessage>
            {
                U("任务"), A(Call("echo", "{\"x\":\"1\"}")), U(Fail("boom")), A("最终答案")
            };

            HistoryPruner.Prune(messages);

            Assert.Equal(4, messages.Count);
            Assert.Contains("boom", messages[2].Content);
        }

        [Fact]
        public void KeepsOnlyLastThreeIdenticalWarnings()
        {
            const string warning = "（系统提示）上一轮正文为空。直接回答，或发 tool-call。";
            var messages = new List<LlmMessage> { U("任务") };
            for (int i = 0; i < 5; i++) messages.Add(U(warning));
            messages.Add(A("最终答案"));

            HistoryPruner.Prune(messages);

            Assert.Equal(3, messages.Count(m => m.Content == warning));
            Assert.Equal("最终答案", messages[^1].Content);
        }

        [Fact]
        public void DuplicateLargeToolResultsKeepOnlyTheLast()
        {
            string data = new string('x', 300);
            var messages = new List<LlmMessage>
            {
                U("任务"),
                A(Call("read_file", "{\"path\":\"a.txt\"}")), U(Ok(data)),
                A(Call("read_file", "{\"path\":\"a.txt\"}")), U(Ok(data)),
                A("最终答案")
            };

            int changed = HistoryPruner.Prune(messages);

            Assert.Equal(1, changed);
            Assert.Contains("已省略", messages[2].Content);
            Assert.Contains(data, messages[4].Content);
        }

        [Fact]
        public void SmallIdenticalResultsAreNotDeduped()
        {
            var messages = new List<LlmMessage>
            {
                U("任务"),
                A(Call("read_file", "{\"path\":\"a.txt\"}")), U(Ok("ok")),
                A(Call("read_file", "{\"path\":\"a.txt\"}")), U(Ok("ok")),
                A("最终答案")
            };

            Assert.Equal(0, HistoryPruner.Prune(messages));
        }

        [Fact]
        public void EmptyAssistantTurnBeforeWarningIsRemoved()
        {
            const string warning = "（系统提示）上轮正文为空。直接回答，或发 tool-call。";
            var messages = new List<LlmMessage>
            {
                U("任务"), A(""), U(warning), A("最终答案")
            };

            HistoryPruner.Prune(messages);

            Assert.Equal(3, messages.Count);
            Assert.Equal(warning, messages[1].Content);
            Assert.Equal("最终答案", messages[^1].Content);
        }

        [Fact]
        public void LastAssistantAndLastToolResultAreNeverRemoved()
        {
            // 最后一次失败没有后续成功：不能删，否则调用没有结果
            var messages = new List<LlmMessage>
            {
                U("任务"), A(Call("echo", "{\"x\":\"1\"}")), U(Ok("done")), A(Call("echo", "{\"x\":\"2\"}")), U(Fail("boom"))
            };

            Assert.Equal(0, HistoryPruner.Prune(messages));
            Assert.Equal(5, messages.Count);
        }
    }
}
