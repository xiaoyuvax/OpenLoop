using OpenLoop.Core.Agent;
using Xunit;

namespace OpenLoop.Core.Tests
{
    public class AgentToolCallParserTests
    {
        [Fact]
        public void ParsesFancyToolCallBlock()
        {
            const string text = "<|tool_call_start|>[read_file(path='BFlatA.cs'), read_file(path='BFlatA.csproj')]<|tool_call_end|>";
            Assert.True(AgentToolCallParser.TryParse(text, out var call));
            Assert.Equal("read_file", call.Tool);
            Assert.Equal("BFlatA.cs", call.Args["path"]);
            Assert.True(AgentToolCallParser.LooksLikeToolCallAttempt(text));
        }
        [Fact]
        public void ParsesSingleLineMarker()
        {
            Assert.True(AgentToolCallParser.TryParse(
                "tool-call: {\"tool\":\"read_file\",\"args\":{\"path\":\"a.txt\"}}", out var call));
            Assert.Equal("read_file", call.Tool);
            Assert.Equal("a.txt", call.Args["path"]);
        }

        [Fact]
        public void ParsesMultilineJsonWithTrailingText()
        {
            string text = "我先看看。\ntool-call: {\n  \"tool\": \"grep\",\n  \"args\": {\"pattern\": \"TODO\"}\n}\n然后继续。";
            Assert.True(AgentToolCallParser.TryParse(text, out var call));
            Assert.Equal("grep", call.Tool);
            Assert.Equal("TODO", call.Args["pattern"]);
        }

        [Fact]
        public void ParsesWithoutMarker()
        {
            Assert.True(AgentToolCallParser.TryParse(
                "{\"tool\":\"list_dir\",\"args\":{\"recursive\":true}}", out var call));
            Assert.Equal("list_dir", call.Tool);
            Assert.Equal(true, call.Args["recursive"]);
        }

        [Fact]
        public void ParsesTaggedStyle()
        {
            Assert.True(AgentToolCallParser.TryParse(
                "<tool_call><function=write_file><parameter=args>{\"path\":\"a\"}</parameter></tool_call>",
                out var call));
            Assert.Equal("write_file", call.Tool);
            Assert.Equal("a", call.Args["path"]);
        }

        [Fact]
        public void ParsesOpenAiStyle()
        {
            Assert.True(AgentToolCallParser.TryParse(
                "{\"name\":\"read_file\",\"arguments\":{\"path\":\"b.txt\"}}", out var call));
            Assert.Equal("read_file", call.Tool);
            Assert.Equal("b.txt", call.Args["path"]);
        }

        [Fact]
        public void TruncatedJsonIsNotACall()
        {
            Assert.False(AgentToolCallParser.TryParse(
                "tool-call: {\"tool\":\"read_file\",\"args\":{\"path\":\"a", out _));
        }

        [Fact]
        public void LooksLikeAttemptDetectsBrokenProtocol()
        {
            Assert.True(AgentToolCallParser.LooksLikeToolCallAttempt("tool-call: {\"tool\":"));
            Assert.True(AgentToolCallParser.LooksLikeToolCallAttempt("<tool_call>"));
            Assert.False(AgentToolCallParser.LooksLikeToolCallAttempt("这是普通回答。"));
        }
    }
}
