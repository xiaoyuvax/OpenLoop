using OpenLoop.Core.Agent;
using Xunit;

namespace OpenLoop.Core.Tests
{
    public class TargetTextTests
    {
        [Fact]
        public void Replace_middle_line()
        {
            var (ok, text, error) = TargetText.Replace("a\nb\nc", 2, 2, "B");
            Assert.True(ok, error);
            Assert.Equal("a\nB\nc", text);
        }

        [Fact]
        public void Replace_to_end_with_zero()
        {
            var (ok, text, _) = TargetText.Replace("a\nb\nc", 2, 0, "X");
            Assert.True(ok);
            Assert.Equal("a\nX", text);
        }

        [Fact]
        public void Insert_before_first_and_after_last()
        {
            Assert.Equal("x\na\nb", TargetText.Replace("a\nb", 1, 0, "x").Text);
            Assert.Equal("a\nb\nc", TargetText.Replace("a\nb", 3, 2, "c").Text);
        }

        [Fact]
        public void Insert_block_strips_one_trailing_newline()
        {
            Assert.Equal("a\nx\ny\nb", TargetText.Replace("a\nb", 2, 1, "x\ny\n").Text);
        }

        [Fact]
        public void Delete_line_range()
        {
            Assert.Equal("a\nc", TargetText.Replace("a\nb\nc", 2, 2, "").Text);
            Assert.Equal("a\nb", TargetText.Replace("a\nb\nc", 3, 3, "").Text);
        }

        [Fact]
        public void Replace_normalizes_crlf_in_text()
        {
            Assert.Equal("a\nB\nC\nc", TargetText.Replace("a\nb\nc", 2, 2, "B\r\nC").Text);
        }

        [Fact]
        public void Out_of_range_fails_without_clamping()
        {
            Assert.False(TargetText.Replace("a\nb", 0, 1, "x").Ok);
            Assert.False(TargetText.Replace("a\nb", 3, 3, "x").Ok);
            Assert.False(TargetText.Replace("a\nb\nc\nd\ne", 4, 2, "x").Ok);
            Assert.False(TargetText.Replace("a\nb", 2, 5, "x").Ok);
            Assert.False(TargetText.Replace("a\nb", 4, 3, "x").Ok);
        }

        [Fact]
        public void Page_walks_to_zero_and_covers_whole_buffer()
        {
            string buffer = string.Join("\n", Enumerable.Range(1, 3000).Select(i => $"line {i} " + new string('x', 20)));
            var text = new System.Text.StringBuilder();
            int from = 1;
            int guard = 0;
            while (true)
            {
                var page = TargetText.Page(buffer, from, 0);
                Assert.True(page.Text.Length <= TargetText.PageChars);
                text.Append(text.Length > 0 ? "\n" : "").Append(page.Text);
                if (page.Next == 0) break;
                Assert.True(page.Next > from);
                from = page.Next;
                Assert.True(++guard < 100);
            }
            Assert.Equal(buffer, text.ToString());
        }

        [Fact]
        public void Page_honors_range_and_beyond_end()
        {
            var page = TargetText.Page("a\nb\nc\nd", 2, 3);
            Assert.Equal("b\nc", page.Text);
            Assert.Equal(2, page.From);
            Assert.Equal(3, page.To);
            Assert.Equal(4, page.Next);
            Assert.Equal(4, page.Total);

            var past = TargetText.Page("a\nb", 9, 0);
            Assert.Equal("", past.Text);
            Assert.Equal(0, past.Next);
            Assert.Equal(9, past.From);
        }
    }
}
