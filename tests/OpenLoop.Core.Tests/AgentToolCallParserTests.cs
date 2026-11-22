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
        public void ParsesDoublePipeDsmlWithMalformedParameterClose()
        {
            // 实测泄漏形态（deepseek v4.1-flash）：下一个参数被写进 </parameter name="offset"> 里，
            // 旧实现会把整段标签残渣并进 path，用户就看到 "<｜｜DSML｜｜ …" 漏出
            const string text =
                "<｜｜DSML｜｜ calls>\n" +
                "<｜｜DSML｜｜ invoke name=\"ReadFile\">\n" +
                "<｜｜DSML｜｜ parameter name=\"path\" string=\"true\">.gitignore</｜｜DSML｜｜ parameter name=\"offset\" string=\"false\">266</｜｜DSML｜｜ parameter>\n" +
                "</｜｜DSML｜｜ invoke>\n" +
                "</｜｜DSML｜｜ calls>";

            Assert.True(AgentToolCallParser.TryParse(text, out var call));
            Assert.Equal("ReadFile", call.Tool);
            Assert.Equal(".gitignore", call.Args["path"]);
            Assert.Equal(266L, call.Args["offset"]);
        }

        [Fact]
        public void ParsesHeadlessDsmlCallsBlock()
        {
            // 实测（a2a/deepseek）：网关把 <invoke name="WriteFile"> 头整个丢掉，
            // 只留参数，工具名写在 <parameter name="tool"> 里；旧实现整轮判成格式损坏。
            const string text =
                "<｜｜DSML｜｜ calls>\n" +
                "<｜｜DSML｜｜ parameter name=\"args\" string=\"false\">{\"content\":\"x\",\"path\":\"a.ps1\"}</｜｜DSML｜｜ parameter>\n" +
                "<｜｜DSML｜｜ parameter name=\"tool\" string=\"true\">WriteFile</｜｜DSML｜｜ parameter>\n" +
                "</｜｜DSML｜｜ invoke>\n" +
                "</｜｜DSML｜｜ calls>";

            Assert.True(AgentToolCallParser.TryParse(text, out var call));
            Assert.Equal("WriteFile", call.Tool);
            Assert.Equal("a.ps1", call.Args["path"]);
            Assert.Equal("x", call.Args["content"]);
            Assert.False(call.Args.ContainsKey("tool"));
        }

        [Fact]
        public void ParsesDsmlInvokeWithSingleQuotesInArgs()
        {
            // 实测（付费模型写 PowerShell 补丁脚本）：args 的 content 全是 'Stop' 这类单引号，
            // 旧实现先把 ' 全换成 " 再解析 → JSON 坏掉 → 整条调用判格式损坏，重试 3 次后整轮停机
            const string text =
                "<｜｜DSML｜｜ calls>\n" +
                "<｜｜DSML｜｜ invoke name=\"WriteFile\">\n" +
                "<｜｜DSML｜｜ parameter name=\"args\" string=\"false\">" +
                "{\"path\":\"a.ps1\",\"content\":\"$x='Stop'\\r\\n$y='D:\\\\PUB'\"}" +
                "</｜｜DSML｜｜ parameter>\n" +
                "</｜｜DSML｜｜ invoke>\n" +
                "</｜｜DSML｜｜ calls>";

            var calls = AgentToolCallParser.TryParseAll(text);
            Assert.Single(calls);
            Assert.Equal("WriteFile", calls[0].Tool);
            Assert.Equal("a.ps1", calls[0].Args["path"]);
            Assert.Equal("$x='Stop'\r\n$y='D:\\PUB'", calls[0].Args["content"]);
        }

        [Fact]
        public void ToolParameterIsNameNotArgument()
        {
            // 同一种泄漏的另一半：有 invoke 头，但参数里又夹了 name="tool"；
            // 旧实现把它当参数，args 摊不平 → 执行时缺 path。
            const string text =
                "<｜｜DSML｜｜ calls>\n" +
                "<｜｜DSML｜｜ invoke name=\"WriteFile\">\n" +
                "<｜｜DSML｜｜ parameter name=\"args\" string=\"false\">{\"content\":\"x\",\"path\":\"a.ps1\"}</｜｜DSML｜｜ parameter>\n" +
                "<｜｜DSML｜｜ parameter name=\"tool\" string=\"true\">WriteFile</｜｜DSML｜｜ parameter>\n" +
                "</｜｜DSML｜｜ invoke>\n" +
                "</｜｜DSML｜｜ calls>";

            Assert.True(AgentToolCallParser.TryParse(text, out var call));
            Assert.Equal("WriteFile", call.Tool);
            Assert.Equal("a.ps1", call.Args["path"]);
            Assert.False(call.Args.ContainsKey("tool"));
        }

        [Fact]
        public void ParsesMultipleInvokeBlocksWithoutMixingArgs()
        {
            const string text =
                "<|DSML| calls>" +
                "<|DSML| invoke name=\"ReadFile\"><|DSML| parameter name=\"path\">.gitignore</|DSML| parameter></|DSML| invoke>" +
                "<|DSML| invoke name=\"Shell\"><|DSML| parameter name=\"argv\" string=\"false\">[\"git\",\"status\"]</|DSML| parameter></|DSML| invoke>";

            var calls = AgentToolCallParser.TryParseAll(text);
            Assert.Equal(2, calls.Count);
            Assert.Equal("ReadFile", calls[0].Tool);
            Assert.Equal(".gitignore", calls[0].Args["path"]);
            Assert.False(calls[0].Args.ContainsKey("argv"));
            Assert.Equal("Shell", calls[1].Tool);
            Assert.Contains("status", calls[1].Args["argv"].ToString());
        }

        [Fact]
        public void ParsesDsmlInvokeWithTrailingBraceJunk()
        {
            // 实测（deepseek-flash-1017，Loop 会话）：args JSON 后面多一个 }、整条 invoke 也没有结束标签。
            // 旧实现把整段值当字符串 → args 摊不平 → 连续 3 次判格式损坏停机。
            const string text =
                "<｜｜DSML｜｜ calls>\n" +
                "<｜｜DSML｜｜ invoke name=\"Shell\">\n" +
                "<｜｜DSML｜｜ parameter name=\"args\" string=\"false\">" +
                "{\"argv\":[\"powershell\",\"-NoProfile\",\"-Command\"," +
                "\"$j.PSObject.Properties | ForEach-Object { $_.Name }\"]}}";

            var calls = AgentToolCallParser.TryParseAll(text);
            Assert.Single(calls);
            Assert.Equal("Shell", calls[0].Tool);
            Assert.Contains("powershell", calls[0].Args["argv"].ToString());
            Assert.Contains("ForEach-Object", calls[0].Args["argv"].ToString());
        }

        [Fact]
        public void BracketStyleToolCallsTokenNeverReachesTheAnswer()
        {
            // Mistral 系没有尖括号，只有 [TOOL_CALLS] 前缀：同样算协议片段
            const string text = "[TOOL_CALLS] [{\"name\":\"Shell\",\"arguments\":{\"command\":\"ls\"}}]";
            Assert.True(AgentToolCallParser.LooksLikeToolCallAttempt(text));
            Assert.Equal("", AgentToolCallParser.CutToolCallText(text));
            Assert.Equal("先看一下现状。", AgentToolCallParser.CutToolCallText("先看一下现状。\n[TOOL_CALLS] [{\"name\":\"Shell\"}]"));
            // 普通方括号不是协议
            Assert.Equal("[tool] 只是个词", AgentToolCallParser.CutToolCallText("[tool] 只是个词"));
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
        public void InlineCodeProtocolIsQuotedNotCalled()
        {
            // 实测（Loop 自己踩到）：回答正文用行内代码讲解协议，被当成真实调用派发（「未知工具：X」）
            const string prose =
                "协议长这样：`<invoke name=\"X\">`，或者 `tool-call: {\"tool\":\"Shell\",\"args\":{}}`。";
            Assert.Empty(AgentToolCallParser.TryParseAll(prose));
            Assert.False(AgentToolCallParser.TryParse(prose, out _));
            Assert.False(AgentToolCallParser.LooksLikeToolCallAttempt(prose));
        }

        [Fact]
        public void FencedProtocolStillRuns()
        {
            // 围栏代码块里的是真实调用（模型偶尔这样包一层），不能被当成引用吞掉
            const string text = "```\ntool-call: {\"tool\":\"Shell\",\"args\":{\"command\":\"ls\"}}\n```";
            var calls = AgentToolCallParser.TryParseAll(text);
            Assert.Single(calls);
            Assert.Equal("Shell", calls[0].Tool);
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
        public void ProtocolFragmentMarkers_AreAttemptsNotAnswers()
        {
            // 实泄露形态：<｜tool_calls｜> + 裸参数标签，凑不出合法 JSON，但绝不能当最终答复
            const string text =
                "<｜tool_calls｜><｜parameter name=\"path\" string=\"true\">loop/findings.jsonl</｜parameter>";
            Assert.True(AgentToolCallParser.LooksLikeToolCallAttempt(text));
            Assert.False(AgentToolCallParser.TryParse(text, out _));
            Assert.Equal("", AgentToolCallParser.CutToolCallText(text));

            Assert.True(AgentToolCallParser.LooksLikeToolCallAttempt("</|DSML| invoke>"));

            Assert.False(AgentToolCallParser.LooksLikeToolCallAttempt("这是普通回答。"));
            Assert.False(AgentToolCallParser.LooksLikeToolCallAttempt("工具调用会返回 tool-result。"));
            // 只吐出半截协议（实测 qwen4-max 整条正文就是 tool）：是坏调用，不是答案
            Assert.True(AgentToolCallParser.LooksLikeToolCallAttempt("tool"));
            Assert.True(AgentToolCallParser.LooksLikeToolCallAttempt("<｜｜tool"));
            Assert.True(AgentToolCallParser.LooksLikeToolCallAttempt("<｜｜"));
            Assert.False(AgentToolCallParser.LooksLikeToolCallAttempt("toolbox 用来装工具。"));
        }

        [Fact]
        public void QuotedProtocolInAnswer_IsNotAttempt()
        {
            // 回答里用反引号/代码块讲解协议时不算尝试，否则说明性回答会被 ResponseGuard 当成坏调用吞掉
            Assert.False(AgentToolCallParser.LooksLikeToolCallAttempt(
                "修复说明：只认单行 `tool-call: {\"tool\":\"x\"}`，不要再吐 `<｜tool_calls｜>` 这类片段。"));
            Assert.False(AgentToolCallParser.LooksLikeToolCallAttempt(
                "泄漏形态长这样：\n```\n<｜tool_calls｜>\n```\n就是它。"));
            // 脱离引用的裸片段仍然算尝试
            Assert.True(AgentToolCallParser.LooksLikeToolCallAttempt(
                "泄漏形态长这样：\n<｜tool_calls｜>"));
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
            // 只吐到一半的协议片段也不能当正文显示（qwen4-max 正文里反复出现 "tool"）
            Assert.Equal("", AgentToolCallParser.CutToolCallText("tool"));
            Assert.Equal("", AgentToolCallParser.CutToolCallText("tool-ca"));
            Assert.Equal("先看一下现状。", AgentToolCallParser.CutToolCallText("先看一下现状。\ntool-call"));
            Assert.Equal("先看一下现状。", AgentToolCallParser.CutToolCallText("先看一下现状。\n<｜｜tool"));
            // 只吐到装饰符的半截（实测 deepseek：流式正文里先冒出 "<｜"、"<｜｜"）也不能当正文显示
            Assert.Equal("", AgentToolCallParser.CutToolCallText("<"));
            Assert.Equal("", AgentToolCallParser.CutToolCallText("<｜"));
            Assert.Equal("", AgentToolCallParser.CutToolCallText("<｜｜"));
            Assert.Equal("先看一下现状。", AgentToolCallParser.CutToolCallText("先看一下现状。\n<｜｜"));
            // 命名空间式装饰（<antml:invoke>）
            Assert.Equal("", AgentToolCallParser.CutToolCallText("<antml:invoke name=\"Shell\">"));
            // DeepSeek 原生分词标记（▁ 是词内分隔符）
            Assert.Equal("", AgentToolCallParser.CutToolCallText("<｜tool▁calls▁begin｜>"));
            Assert.Equal("先看一下现状。", AgentToolCallParser.CutToolCallText("先看一下现状。\n<｜tool▁calls▁begin｜>"));
            // 普通尖括号不是协议
            Assert.Equal("a <", AgentToolCallParser.CutToolCallText("a <"));
            // 其它家特殊 token（harmony 的 python_tag / eot_id）同样是协议：名字不枚举，按 <|…|> 形状认
            Assert.Equal("", AgentToolCallParser.CutToolCallText("<|python_tag|>"));
            Assert.Equal("", AgentToolCallParser.CutToolCallText("<|python_ta"));
            Assert.Equal("先看一下现状。", AgentToolCallParser.CutToolCallText("先看一下现状。\n<|python_tag|>{\"a\":1}"));
            Assert.Equal("对比 a", AgentToolCallParser.CutToolCallText("对比 a<|python_tag|> 就该截"));
            Assert.True(AgentToolCallParser.LooksLikeToolCallAttempt("<|eot_id|>"));
            Assert.Equal("先看一下现状。", AgentToolCallParser.CutToolCallText("先看一下现状。\n</｜｜param"));
            // 正文后面紧跟半截协议（没有换行）同样要截，否则用户会看到正文末尾挂着 tool
            Assert.Equal("先看一下现状。", AgentToolCallParser.CutToolCallText("先看一下现状。tool"));
            Assert.Equal("先看一下现状", AgentToolCallParser.CutToolCallText("先看一下现状tool"));
            Assert.Equal("先看一下现状。", AgentToolCallParser.CutToolCallText("先看一下现状。tool-ca"));
            // 英文词里的 tool 与句中的 tool 是普通单词，不截
            Assert.Equal("改这句要用 tool", AgentToolCallParser.CutToolCallText("改这句要用 tool"));
            Assert.Equal("keytool 是签名工具", AgentToolCallParser.CutToolCallText("keytool 是签名工具"));
            Assert.Equal("toolbox 好用", AgentToolCallParser.CutToolCallText("toolbox 好用"));
        }

        /// <summary>正文里用行内代码/围栏引用协议是讲解，不是泄漏：流式显示不能按它把后文整段截掉。</summary>
        [Fact]
        public void CutToolCallTextKeepsProtocolQuotedInCode()
        {
            string inline = "历史里的 `tool-call:` / `tool-result:` 行由适配层翻译。\n\n结论：保留。";
            Assert.Equal(inline, AgentToolCallParser.CutToolCallText(inline));

            string fenced = "格式如下：\n```\ntool-call: {\"tool\":\"Shell\"}\n```\n其余正文照常显示。";
            Assert.Equal(fenced, AgentToolCallParser.CutToolCallText(fenced));

            // 代码外的真协议片段照旧截断（行内代码里的引用不能把后面这段也豁免掉）
            Assert.Equal("先看一下现状。\n`tool-call:` 之后又吐了真调用：",
                AgentToolCallParser.CutToolCallText(
                    "先看一下现状。\n`tool-call:` 之后又吐了真调用：tool-call: {\"tool\":\"Shell\"}"));
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
