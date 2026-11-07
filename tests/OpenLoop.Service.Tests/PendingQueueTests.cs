using OpenLoop.Service.Pending;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>输入队列的两种插入时机与出队优先级。</summary>
    public class PendingQueueTests
    {
        [Fact]
        public void Add_defaults_to_after_turn_and_trims()
        {
            var queue = new PendingQueue();
            var item = queue.Add("  hello  ");

            Assert.Equal("hello", item.Text);
            Assert.Equal(PendingMode.AfterTurn, item.Mode);
            Assert.False(item.Immediate);
            Assert.Equal(1, queue.Count);
        }

        [Fact]
        public void Add_ignores_blank()
        {
            var queue = new PendingQueue();
            Assert.Null(queue.Add("   "));
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void Toggle_switches_mode_both_ways()
        {
            var queue = new PendingQueue();
            var item = queue.Add("x");

            Assert.True(queue.Toggle(item).Immediate);
            Assert.False(queue.Toggle(item).Immediate);
        }

        [Fact]
        public void TakeNext_prefers_immediate_over_order()
        {
            var queue = new PendingQueue();
            var first = queue.Add("first");
            var second = queue.Add("second");
            queue.Toggle(second);

            Assert.Equal(second, queue.TakeNext());
            Assert.Equal(first, queue.TakeNext());
            Assert.Null(queue.TakeNext());
        }

        [Fact]
        public void TakeNext_respects_pause()
        {
            var queue = new PendingQueue();
            queue.Add("x");
            queue.Paused = true;

            Assert.Null(queue.TakeNext());
            queue.Paused = false;
            Assert.NotNull(queue.TakeNext());
        }
    }
}
