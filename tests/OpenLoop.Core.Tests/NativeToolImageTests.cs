using System.Text.Json;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using Xunit;

namespace OpenLoop.Core.Tests
{
    public class NativeToolImageTests
    {
        private static List<LlmMessage> History(string imageDataUrlPart) => new()
        {
            new("system", "sys"),
            new("assistant", "tool-call: {\"tool\":\"Puppet\",\"args\":{\"method\":\"CaptureAction\"}}"),
            new("user", "tool-result: {\"ok\":true,\"data\":\"x\"}")
        };

        [Fact]
        public void Tool_result_message_with_image_becomes_image_url_part()
        {
            var history = new List<LlmMessage>
            {
                new("system", "sys"),
                new("assistant", "tool-call: {\"tool\":\"Puppet\",\"args\":{\"method\":\"CaptureAction\"}}"),
                new("user", "tool-result: {\"ok\":true,\"data\":\"x\"}")
                {
                    Images = [new ToolImage { MimeType = "image/png", Base64 = "aGVsbG8=" }]
                }
            };

            var wire = NativeToolWire.Messages(history);

            // 找出 content 为多 part 的 user 消息（图片走附件通道）
            var withParts = wire.OfType<Dictionary<string, object>>()
                .Where(m => m.TryGetValue("role", out var r) && string.Equals(r, "user")
                            && m["content"] is List<object>)
                .ToList();
            Assert.Single(withParts);

            var content = (List<object>)withParts[0]["content"];
            Assert.Equal(1, content.Count); // tool-result 本体在 role:tool 里；这里只有 image part

            var imagePart = Assert.IsType<Dictionary<string, object>>(content[0]);
            Assert.Equal("image_url", imagePart["type"]);
            var url = (Dictionary<string, object>)imagePart["image_url"];
            Assert.Equal("data:image/png;base64,aGVsbG8=", url["url"]);

            // 历史正文里没有任何 base64
            Assert.DoesNotContain("aGVsbG8=", history[2].Content);
        }

        [Fact]
        public void Tool_result_without_image_stays_plain_text()
        {
            var history = new List<LlmMessage>
            {
                new("assistant", "tool-call: {\"tool\":\"ReadFile\",\"args\":{}}"),
                new("user", "tool-result: {\"ok\":true,\"data\":\"text\"}")
            };

            var wire = NativeToolWire.Messages(history);
            var users = wire.OfType<Dictionary<string, object>>()
                .Where(m => string.Equals(m["role"], "user")).ToList();
            Assert.All(users, m => Assert.IsType<string>(m["content"])); // 纯文本，不是 parts
        }
    }
}
