using OpenLoop.Service.Pending;
using OpenLoop.Service.Storage;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>改的是进程级的 ChatHistory.Root，必须和别的用同一个静态根的测试串行。</summary>
    [Collection("chat-index")]
    public class PendingStoreTests
    {
        [Fact]
        public void Queue_survives_a_restart()
        {
            string root = Path.Combine(Path.GetTempPath(), "ol-pending-" + Guid.NewGuid().ToString("N"));
            string previous = ChatHistory.Root;
            ChatHistory.Root = root;
            try
            {
                var queue = new PendingQueue();
                queue.Add("等这一轮返回", "task", PendingMode.AfterTurn);
                queue.Add("马上插队", "correction", PendingMode.NextStep);
                queue.Paused = true;
                PendingStore.Save(@"C:\work\demo", "s1", queue);

                var loaded = PendingStore.Load(@"C:\work\demo", "s1");
                Assert.True(loaded.Paused);
                Assert.Equal(2, loaded.Items.Count);
                Assert.Equal("等这一轮返回", loaded.Items[0].Text);
                Assert.Equal(PendingMode.AfterTurn, loaded.Items[0].Mode);
                Assert.Equal("correction", loaded.Items[1].Tag);
                Assert.True(loaded.Items[1].Immediate);

                var restored = new PendingQueue();
                restored.Restore(loaded.Items, loaded.Paused);
                Assert.Equal(2, restored.Count);
                Assert.True(restored.Paused);
                Assert.Null(restored.TakeNext());   // 暂停中不续发
            }
            finally
            {
                ChatHistory.Root = previous;
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void Sending_takes_the_item_out_so_it_is_not_resent()
        {
            string root = Path.Combine(Path.GetTempPath(), "ol-pending-" + Guid.NewGuid().ToString("N"));
            string previous = ChatHistory.Root;
            ChatHistory.Root = root;
            try
            {
                var queue = new PendingQueue();
                queue.Add("one");
                queue.Add("two");
                PendingStore.Save(@"C:\work\demo", "s1", queue);

                Assert.Equal("one", queue.TakeNext().Text);
                PendingStore.Save(@"C:\work\demo", "s1", queue);

                var loaded = PendingStore.Load(@"C:\work\demo", "s1");
                Assert.Single(loaded.Items);
                Assert.Equal("two", loaded.Items[0].Text);
            }
            finally
            {
                ChatHistory.Root = previous;
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void Missing_file_reads_as_an_empty_queue()
        {
            string root = Path.Combine(Path.GetTempPath(), "ol-pending-" + Guid.NewGuid().ToString("N"));
            string previous = ChatHistory.Root;
            ChatHistory.Root = root;
            try
            {
                var loaded = PendingStore.Load(@"C:\work\nobody", "s9");
                Assert.Empty(loaded.Items);
                Assert.False(loaded.Paused);
            }
            finally
            {
                ChatHistory.Root = previous;
                try { Directory.Delete(root, true); } catch { }
            }
        }
    }
}
