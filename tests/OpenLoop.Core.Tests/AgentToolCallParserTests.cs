using OpenLoop.Core.Agent;
using Xunit;

namespace OpenLoop.Core.Tests
{
    public class AgentToolCallParserTests
    {
        [Fact]
        public void ParsesFancyToolCallBlock()
        {
            const string text = "<|tool_call_start|>[ReadFile(path='BFlatA.cs'), ReadFile(path='BFlatA.csproj')]<|tool_call_end|>";
            Assert.True(AgentToolCallParser.TryParse(text, out var call));
            Assert.Equal("ReadFile", call.Tool);
            Assert.Equal("BFlatA.cs", call.Args["path"]);
            Assert.True(AgentToolCallParser.LooksLikeToolCallAttempt(text));
        }
        [Fact]
        public void ParsesSingleLineMarker()
        {
            Assert.True(AgentToolCallParser.TryParse(
                "tool-call: {\"tool\":\"ReadFile\",\"args\":{\"path\":\"a.txt\"}}", out var call));
            Assert.Equal("ReadFile", call.Tool);
            Assert.Equal("a.txt", call.Args["path"]);
        }

        [Fact]
        public void ParsesAll_MultipleMarkers()
        {
            var calls = AgentToolCallParser.TryParseAll(
                "先并行看这两处。\ntool-call: {\"tool\":\"ReadFile\",\"args\":{\"path\":\"a.cs\"}}\n" +
                "tool-call: {\"tool\":\"ListDir\",\"args\":{\"path\":\"src\"}}\ntool-call: {\"tool\":\"Grep\",\"args\":{\"pattern\":\"x\"}}");
            Assert.Equal(3, calls.Count);
            Assert.Equal("ReadFile", calls[0].Tool);
            Assert.Equal("a.cs", calls[0].Args["path"]);
            Assert.Equal("ListDir", calls[1].Tool);
            Assert.Equal("Grep", calls[2].Tool);
        }

        [Fact]
        public void ParsesAll_SingleCall_FallsBackToFullTolerance()
        {
            // 无 marker 的裸 JSON 单调用也要能走 TryParse 兜底
            var calls = AgentToolCallParser.TryParseAll("{\"tool\":\"ListDir\",\"args\":{\"recursive\":true}}");
            Assert.Single(calls);
            Assert.Equal("ListDir", calls[0].Tool);
        }

        [Fact]
        public void ParsesAll_EmptyOrPlainText_ReturnsEmpty()
        {
            Assert.Empty(AgentToolCallParser.TryParseAll(""));
            Assert.Empty(AgentToolCallParser.TryParseAll("普通最终回答，没有工具调用。"));
        }

        [Fact]
        public void ParsesAll_MultilineJsonPerMarker()
        {
            var calls = AgentToolCallParser.TryParseAll(
                "tool-call: {\n  \"tool\": \"Grep\",\n  \"args\": {\"pattern\": \"TODO\"}\n}\n" +
                "tool-call: {\"tool\":\"ReadFile\",\"args\":{\"path\":\"b.cs\"}}");
            Assert.Equal(2, calls.Count);
            Assert.Equal("Grep", calls[0].Tool);
            Assert.Equal("TODO", calls[0].Args["pattern"]);
            Assert.Equal("ReadFile", calls[1].Tool);
        }

        [Fact]
        public void ParsesMultilineJsonWithTrailingText()
        {
            string text = "我先看看。\ntool-call: {\n  \"tool\": \"Grep\",\n  \"args\": {\"pattern\": \"TODO\"}\n}\n然后继续。";
            Assert.True(AgentToolCallParser.TryParse(text, out var call));
            Assert.Equal("Grep", call.Tool);
            Assert.Equal("TODO", call.Args["pattern"]);
        }

        [Fact]
        public void ParsesWithoutMarker()
        {
            Assert.True(AgentToolCallParser.TryParse(
                "{\"tool\":\"ListDir\",\"args\":{\"recursive\":true}}", out var call));
            Assert.Equal("ListDir", call.Tool);
            Assert.Equal(true, call.Args["recursive"]);
        }

        [Fact]
        public void ParsesTaggedStyle()
        {
            Assert.True(AgentToolCallParser.TryParse(
                "<tool_call><function=WriteFile><parameter=args>{\"path\":\"a\"}</parameter></tool_call>",
                out var call));
            Assert.Equal("WriteFile", call.Tool);
            Assert.Equal("a", call.Args["path"]);
        }

        [Fact]
        public void ParsesOpenAiStyle()
        {
            Assert.True(AgentToolCallParser.TryParse(
                "{\"name\":\"ReadFile\",\"arguments\":{\"path\":\"b.txt\"}}", out var call));
            Assert.Equal("ReadFile", call.Tool);
            Assert.Equal("b.txt", call.Args["path"]);
        }

        [Fact]
        public void TruncatedJsonIsNotACall()
        {
            Assert.False(AgentToolCallParser.TryParse(
                "tool-call: {\"tool\":\"ReadFile\",\"args\":{\"path\":\"a", out _));
        }

        [Fact]
        public void ParsesChineseSpecialMarker()
        {
            const string text =
                "<｜工具调用： Shell>{\"argv\":[\"powershell\",\"-NoProfile\",\"-File\",\"x.ps1\"],\"cwd\":\".\"}<｜工具调用结束｜>";

            Assert.True(AgentToolCallParser.TryParse(text, out var call));
            Assert.Equal("Shell", call.Tool);
            Assert.Contains("x.ps1", call.Args["argv"].ToString());
            Assert.Equal(".", call.Args["cwd"]);
            Assert.True(AgentToolCallParser.LooksLikeToolCallAttempt(text));
            Assert.Equal("", AgentToolCallParser.CutToolCallText(text));
        }

        [Fact]
        public void ChineseSpecialMarker_TruncatedJsonIsAttemptNotAnswer()
        {
            const string text = "<｜工具调用： ReadFile>{\"path\":\"a.txt";
            Assert.False(AgentToolCallParser.TryParse(text, out _));
            Assert.True(AgentToolCallParser.LooksLikeToolCallAttempt(text));
        }

        [Fact]
        public void ParsesInventedXmlInvoke()
        {
            const string text =
                "<|DSML| calls><|DSML| invoke name=\"Shell\">" +
                "<|DSML| parameter name=\"command\" string=\"true\">echo PROBE > deny.txt</|DSML| parameter>" +
                "</|DSML| invoke>";

            Assert.True(AgentToolCallParser.TryParse(text, out var call));
            Assert.Equal("Shell", call.Tool);
            Assert.Equal("echo PROBE > deny.txt", call.Args["command"]);
            Assert.True(AgentToolCallParser.LooksLikeToolCallAttempt(text));
        }

        [Fact]
        public void LooksLikeAttemptDetectsBrokenProtocol()
        {
            Assert.True(AgentToolCallParser.LooksLikeToolCallAttempt("tool-call: {\"tool\":"));
            Assert.True(AgentToolCallParser.LooksLikeToolCallAttempt("<tool_call>"));
            Assert.False(AgentToolCallParser.LooksLikeToolCallAttempt("这是普通回答。"));
        }

        [Fact]
        public void LooksLikeAttemptDetectsFullWidthDelimiters()
        {
            Assert.True(AgentToolCallParser.LooksLikeToolCallAttempt(
                "<｜tool\":\"ReadEvidence\",\"args\":{\"path\":\"loop/findings.jsonl\",\"limit\":40}}"));
            Assert.True(AgentToolCallParser.LooksLikeToolCallAttempt(
                "{\"tool\":\"ReadFile\"}"));
            Assert.False(AgentToolCallParser.LooksLikeToolCallAttempt("工具调用会返回 tool-result。"));
        }

        [Fact]
        public void LooksLikeToolCallTextOnlyMatchesProtocolBody()
        {
            Assert.True(AgentToolCallParser.LooksLikeToolCallText(
                "tool-call: {\"tool\":\"ListDir\",\"args\":{\"path\":\".\"}}\n" +
                "tool-call: {\"tool\":\"Grep\",\"args\":{\"pattern\":\"x\"}}"));
            Assert.True(AgentToolCallParser.LooksLikeToolCallText("  \r\n tool-call: {\"tool\":\"x\"}"));
            Assert.False(AgentToolCallParser.LooksLikeToolCallText("先看一下现状。\ntool-call: {\"tool\":\"x\"}"));
            Assert.False(AgentToolCallParser.LooksLikeToolCallText(""));
        }

        [Fact]
        public void ParsesArgsEncodedAsJsonString()
        {
            Assert.True(AgentToolCallParser.TryParse(
                "tool-call: {\"tool\":\"ReadFile\",\"args\":\"{\\\"path\\\":\\\"a.txt\\\",\\\"limit\\\":20}\"}", out var call));
            Assert.Equal("ReadFile", call.Tool);
            Assert.Equal("a.txt", call.Args["path"]);
            Assert.Equal(20L, call.Args["limit"]);
        }

        [Fact]
        public void UnwrapsNestedToolCallWrapper()
        {
            Assert.True(AgentToolCallParser.TryParse(
                "tool-call: {\"tool\":\"Tool_call\",\"args\":{\"tool\":\"OpenCodeTask\",\"args\":{\"task\":\"x\"}}}", out var call));
            Assert.Equal("OpenCodeTask", call.Tool);
            Assert.Equal("x", call.Args["task"]);
        }

        [Fact]
        public void UnparseableArgsStringIsFormatDamage()
        {
            Assert.False(AgentToolCallParser.TryParse(
                "tool-call: {\"tool\":\"ReadFile\",\"args\":\"path=a.txt\"}", out _));
        }

        [Fact]
        public void CutToolCallTextHidesProtocolTail()
        {
            Assert.Equal("先看一下现状。", AgentToolCallParser.CutToolCallText(
                "先看一下现状。\n<|DSML| invoke name=\"Shell\"><|DSML| parameter name=\"command\">ls</|DSML| parameter>"));
            Assert.Equal("先看一下现状。", AgentToolCallParser.CutToolCallText(
                "先看一下现状。\n</｜｜DSML｜｜ invoke>"));
            Assert.Equal("", AgentToolCallParser.CutToolCallText("tool-call: {\"tool\":\"x\"}"));
            Assert.Equal("普通回答，含 <code> 标签。", AgentToolCallParser.CutToolCallText("普通回答，含 <code> 标签。"));
        }

        [Fact]
        public void CutToolCallTextHidesFinalMarker()
        {
            Assert.Equal("结论：不需要改动。", AgentToolCallParser.CutToolCallText("结论：不需要改动。\n\n[final]"));
            Assert.Equal("结论：不需要改动。", AgentToolCallParser.CutToolCallText("结论：不需要改动。\n\n[fina"));
            Assert.Equal("", AgentToolCallParser.CutToolCallText("[final]"));
            Assert.Equal("正文里提到 [final] 这个词，但不是收尾标记。",
                AgentToolCallParser.CutToolCallText("正文里提到 [final] 这个词，但不是收尾标记。"));
        }
    }
}
