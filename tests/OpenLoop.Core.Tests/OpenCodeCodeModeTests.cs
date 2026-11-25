using System.Text.Json;
using OpenLoop.Core.OpenCode;
using Xunit;

namespace OpenLoop.Core.Tests;

/// <summary>F-48a2b91a 回归：Code Mode 死胡同的判定与纠正提示。</summary>
public class OpenCodeCodeModeTests
{
    private static OpenCodeNativeTool Tool(string name) =>
        new("call_1", name, "{}", Ok: true, Output: "", Status: "completed", DurationMs: 10);

    [Fact]
    public void Execute_only_turn_without_text_call_is_code_mode_loop()
    {
        var native = new[] { Tool("execute"), Tool("execute") };
        Assert.True(OpenCodeReasoner.LooksLikeCodeModeLoop(native, "我在探索工具面"));
        Assert.True(OpenCodeReasoner.LooksLikeCodeModeLoop(native, ""));
    }

    [Fact]
    public void Turn_with_text_protocol_call_is_not_code_mode_loop()
    {
        var native = new[] { Tool("execute") };
        Assert.False(OpenCodeReasoner.LooksLikeCodeModeLoop(
            native, "tool-call: {\"tool\":\"ReadFile\",\"args\":{\"path\":\"a.cs\"}}"));
    }

    [Fact]
    public void Real_file_tools_are_not_code_mode_loop()
    {
        var native = new[] { Tool("read"), Tool("shell") };
        Assert.False(OpenCodeReasoner.LooksLikeCodeModeLoop(native, ""));
        Assert.False(OpenCodeReasoner.LooksLikeCodeModeLoop([], ""));
    }

    [Fact]
    public void Code_mode_hint_mentions_no_filesystem()
    {
        Assert.Contains("execute", OpenCodeReasoner.CodeModeHint, StringComparison.Ordinal);
        Assert.Contains("tool-call", OpenCodeReasoner.CodeModeHint, StringComparison.Ordinal);
    }
}
