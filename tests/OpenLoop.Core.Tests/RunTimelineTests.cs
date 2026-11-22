using OpenLoop.Core.Agent;
using Xunit;

namespace OpenLoop.Core.Tests
{
    public class RunTimelineTests
    {
        [Fact]
        public void InterleavesThinkingAndToolBlocksInOrder()
        {
            var timeline = new RunTimeline();
            timeline.Thinking("先看看仓库。");
            timeline.Tool("Grep(a) · 1s");
            timeline.Thinking("再看文件。");
            timeline.Tool("Read(b) · 2s");

            Assert.Equal(
                "先看看仓库。\n\nGrep(a) · 1s\n\n再看文件。\n\nRead(b) · 2s",
                timeline.Render());
        }

        [Fact]
        public void ConsecutiveThinkingPiecesJoinOneBlock()
        {
            var timeline = new RunTimeline();
            timeline.Thinking("片段");
            timeline.Thinking("拼接");

            Assert.Equal("片段拼接", timeline.Render());
        }

        [Fact]
        public void DropThinkingRemovesOnlyThePartialTail()
        {
            var timeline = new RunTimeline();
            timeline.Thinking("第一轮");
            timeline.Tool("Grep(a) · 1s");
            timeline.Thinking("重发中的半截");
            timeline.DropThinking();

            Assert.Equal("第一轮\n\nGrep(a) · 1s", timeline.Render());
        }

        [Fact]
        public void EmptyTimelineRendersNothing()
        {
            Assert.Equal("", new RunTimeline().Render());
        }

        [Fact]
        public void RenderPhasesSplitsAfterAnswer()
        {
            var timeline = new RunTimeline();
            timeline.Thinking("想");
            timeline.Answer("正文一。");
            timeline.Thinking("再想");
            timeline.Answer("正文二。");

            // 整轮时间线仍只含思维与工具（正文由分段渲染，避免折叠区重复正文）
            Assert.Equal("想\n\n再想", timeline.Render());

            var phases = timeline.RenderPhases();
            Assert.Equal(2, phases.Count);
            Assert.Equal(("想", "正文一。"), phases[0]);
            Assert.Equal(("再想", "正文二。"), phases[1]);
        }

        [Fact]
        public void RenderPhasesKeepsMidSentenceAnswerInOnePhase()
        {
            var timeline = new RunTimeline();
            timeline.Thinking("想");
            timeline.Answer("这些改动要在下一次迭代换版（A/I/");
            timeline.Tool("Read(AGENTS.md) · 1s");
            timeline.Answer("壳）后才对运行中的 Loop 生效。");

            var phases = timeline.RenderPhases();
            Assert.Single(phases);
            Assert.Equal("这些改动要在下一次迭代换版（A/I/壳）后才对运行中的 Loop 生效。", phases[0].Answer);
            Assert.Contains("Read(AGENTS.md)", phases[0].Thinking);
        }

        [Fact]
        public void RenderPhasesWithoutAnswerIsOneOpenPhase()
        {
            var timeline = new RunTimeline();
            timeline.Thinking("想");
            timeline.Answer("正文一。");
            timeline.Thinking("还在流");

            var phases = timeline.RenderPhases();
            Assert.Equal(2, phases.Count);
            Assert.Equal(("想", "正文一。"), phases[0]);
            Assert.Equal(("还在流", ""), phases[1]);

            Assert.Equal(("", ""), new RunTimeline().RenderPhases()[0]);
        }

        [Fact]
        public void FeedAnswerTailAppendsThrottledTailOnly()
        {
            var timeline = new RunTimeline();
            timeline.Thinking("思考");
            timeline.Answer("前半");
            timeline.FeedAnswerTail("前半后半");

            var phases = timeline.RenderPhases();
            Assert.Single(phases);
            Assert.Equal(("思考", "前半后半"), phases[0]);

            timeline.FeedAnswerTail("完全对不上的文本");
            Assert.Equal("前半后半", timeline.RenderPhases()[0].Answer);
        }
    }
}
