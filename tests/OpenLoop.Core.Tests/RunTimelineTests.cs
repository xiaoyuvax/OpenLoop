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
    }
}
