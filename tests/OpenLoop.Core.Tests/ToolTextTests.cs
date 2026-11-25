using System.Text;
using OpenLoop.Core.Tools;
using Xunit;

namespace OpenLoop.Core.Tests
{
    public class ToolTextTests
    {
        [Fact]
        public void Clip_keeps_text_within_limits_untouched()
        {
            string text = string.Join("\n", Enumerable.Range(1, 2000).Select(i => "line " + i));
            Assert.Equal(text, ToolText.Clip(text));
        }

        [Fact]
        public void Clip_cuts_at_line_limit()
        {
            string text = string.Join("\n", Enumerable.Repeat("x", 3000));
            string head = string.Join("\n", Enumerable.Repeat("x", 2000));
            Assert.Equal(head + "…（截断，共 3000 行 / 5999 字节）", ToolText.Clip(text));
        }

        [Fact]
        public void Clip_cuts_single_long_line_by_utf8_bytes()
        {
            string clipped = ToolText.Clip(new string('a', 60000));
            Assert.Equal(ToolText.ModelBytes, Encoding.UTF8.GetByteCount(clipped[..51200]));
            Assert.StartsWith(new string('a', 51200), clipped);
            Assert.Contains("共 1 行 / 60000 字节", clipped);
        }

        [Fact]
        public void Clip_counts_chinese_as_three_bytes()
        {
            string clipped = ToolText.Clip(new string('中', 20000));
            Assert.True(Encoding.UTF8.GetByteCount(clipped[..17066]) <= ToolText.ModelBytes);
            Assert.StartsWith(new string('中', 17066), clipped);
            Assert.DoesNotContain('\uFFFD', clipped);
        }
    }
}
