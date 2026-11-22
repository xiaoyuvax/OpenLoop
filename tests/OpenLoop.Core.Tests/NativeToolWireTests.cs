using System.Text.Json;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Tools;
using Xunit;

namespace OpenLoop.Core.Tests;

/// <summary>
/// 直连通道的原生工具适配：文本历史 → tool_calls / role:tool，响应 tool_calls → 文本协议。
/// 两条方向都必须能走通，否则状态机只认一半协议。
/// </summary>
public class NativeToolWireTests
{
    private sealed class FakeTool(string name, string schema) : ITool
    {
        public string Name => name;
        public string Description => "测试工具";
        public string ParamsSchema => schema;
        public bool IsReadOnly => true;
        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
            => throw new NotSupportedException();
    }

    [Fact]
    public void Declare_turns_prose_schema_into_json_schema()
    {
        var declared = NativeToolWire.Declare(
            [new FakeTool("ReadFile", "path: string（路径）; offset: number 可选（起始行）")]);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(declared));
        var function = doc.RootElement[0].GetProperty("function");
        Assert.Equal("ReadFile", function.GetProperty("name").GetString());

        var parameters = function.GetProperty("parameters");
        Assert.Equal("object", parameters.GetProperty("type").GetString());
        Assert.Equal("string",
            parameters.GetProperty("properties").GetProperty("path").GetProperty("type").GetString());

        var required = parameters.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("path", required);
        Assert.DoesNotContain("offset", required);
    }

    [Fact]
    public void Messages_pairs_tool_calls_with_results()
    {
        var history = new List<LlmMessage>
        {
            new("system", "sys"),
            new("user", "do it"),
            new("assistant", "先看一下。\ntool-call: {\"tool\":\"ReadFile\",\"args\":{\"path\":\"a.txt\"}}"),
            new("user", "tool-result: {\"ok\":true,\"data\":\"hi\"}"),
        };

        var wire = NativeToolWire.Messages(history);

        var assistant = Assert.IsType<Dictionary<string, object>>(wire[2]);
        Assert.Equal("assistant", assistant["role"]);
        Assert.Equal("先看一下。", assistant["content"]);
        var calls = Assert.IsAssignableFrom<IEnumerable<object>>(assistant["tool_calls"]).ToList();
        Assert.Single(calls);

        var tool = Assert.IsType<Dictionary<string, object>>(wire[3]);
        Assert.Equal("tool", tool["role"]);
        Assert.Equal("call_2_0", tool["tool_call_id"]);
        Assert.Equal("{\"ok\":true,\"data\":\"hi\"}", tool["content"]);
    }

    [Fact]
    public void Messages_drops_dangling_tool_calls()
    {
        // 压缩把结果拆散时，线上不能留悬空的 tool_calls（网关会直接 400）
        var wire = NativeToolWire.Messages(
            [new("assistant", "tool-call: {\"tool\":\"ReadFile\",\"args\":{\"path\":\"a.txt\"}}")]);

        var assistant = Assert.IsType<Dictionary<string, object>>(wire[0]);
        Assert.False(assistant.ContainsKey("tool_calls"));
        Assert.Equal("", assistant["content"]);
    }

    [Fact]
    public void Parse_reads_native_tool_calls_as_text_protocol()
    {
        const string body = """
        {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[
          {"id":"call_1","type":"function","function":{"name":"ReadFile","arguments":"{\"path\":\"a.txt\"}"}}]},
          "finish_reason":"tool_calls"}]}
        """;

        var result = OpenAiCompatibleReasoner.Parse(body);
        var calls = AgentToolCallParser.TryParseAll(result.Content);

        Assert.Single(calls);
        Assert.Equal("ReadFile", calls[0].Tool);
        Assert.Equal("a.txt", calls[0].Args["path"]);
        Assert.Equal("tool_calls", result.FinishReason);
    }

    [Fact]
    public async Task ParseStream_reads_native_tool_calls_as_text_protocol()
    {
        var sse = new StringReader(string.Join("\n", new[]
        {
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"c1\",\"function\":{\"name\":\"ReadFile\",\"arguments\":\"{\\\"path\\\"\"}}]}}]}",
            "",
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\":\\\"a.txt\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}",
            "",
            "data: [DONE]",
        }));

        var result = await OpenAiCompatibleReasoner.ParseStreamAsync(sse, null);
        var calls = AgentToolCallParser.TryParseAll(result.Content);

        Assert.Single(calls);
        Assert.Equal("ReadFile", calls[0].Tool);
        Assert.Equal("a.txt", calls[0].Args["path"]);
        Assert.Equal("tool_calls", result.FinishReason);
    }

    [Fact]
    public void Merge_delta_assembles_streamed_arguments()
    {
        var drafts = new Dictionary<int, NativeToolWire.NativeCallDraft>();
        foreach (string json in new[]
                 {
                     """{"tool_calls":[{"index":0,"id":"c1","function":{"name":"ReadFile","arguments":"{\"pa"}}]}""",
                     """{"tool_calls":[{"index":0,"function":{"arguments":"th\":\"a.txt\"}"}}]}""",
                 })
        {
            using var doc = JsonDocument.Parse(json);
            NativeToolWire.MergeDelta(doc.RootElement, drafts);
        }

        var calls = NativeToolWire.Complete(drafts);
        Assert.Single(calls);
        Assert.Equal("c1", calls[0].Id);
        Assert.Equal("ReadFile", calls[0].Name);
        Assert.Equal("{\"path\":\"a.txt\"}", calls[0].Arguments);
    }

    [Fact]
    public void Render_calls_round_trips_through_parser()
    {
        string rendered = NativeToolWire.RenderCalls(
            [new NativeToolWire.NativeCall("c1", "Shell", "{\"argv\":[\"git\",\"status\"]}")]);

        var calls = AgentToolCallParser.TryParseAll(rendered);

        Assert.Single(calls);
        Assert.Equal("Shell", calls[0].Tool);
        Assert.Contains("status", calls[0].Args["argv"].ToString());
    }
}
