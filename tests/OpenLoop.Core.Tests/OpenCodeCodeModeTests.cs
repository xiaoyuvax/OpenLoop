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

    private static OpenCodeNativeTool Call(string name, string input) =>
        new("call_1", name, input, Ok: true, Output: "", Status: "completed", DurationMs: 10);

    [Fact]
    public void Repeated_echo_probes_are_a_probe_loop()
    {
        var native = new[]
        {
            Call("shell", "{\"command\":\"echo ok\"}"),
            Call("shell", "{\"command\":\"echo probe\"}"),
            Call("shell", "{\"command\":\"echo ready\"}")
        };
        Assert.True(OpenCodeReasoner.LooksLikeProbeLoop(native, ""));
        Assert.True(OpenCodeReasoner.LooksLikeProbeLoop(
            [Call("execute", "{\"code\":\"return 1;\"}"), Call("execute", "{\"code\":\"return 2;\"}"),
             Call("shell", "{\"command\":\"echo x\"}")], ""));
    }

    [Fact]
    public void Few_or_real_calls_are_not_a_probe_loop()
    {
        Assert.False(OpenCodeReasoner.LooksLikeProbeLoop(
            [Call("shell", "{\"command\":\"echo ok\"}"), Call("shell", "{\"command\":\"echo probe\"}")], ""));
        Assert.False(OpenCodeReasoner.LooksLikeProbeLoop(
            [Call("shell", "{\"command\":\"rg -n ProposeTask src\"}"), Call("read", "{\"path\":\"a.cs\"}"),
             Call("read", "{\"path\":\"b.cs\"}")], ""));
        Assert.False(OpenCodeReasoner.LooksLikeProbeLoop([], ""));
    }

    [Fact]
    public void Probe_loop_with_text_protocol_call_is_left_alone()
    {
        var native = new[]
        {
            Call("shell", "{\"command\":\"echo ok\"}"), Call("shell", "{\"command\":\"echo probe\"}"),
            Call("shell", "{\"command\":\"echo ready\"}")
        };
        Assert.False(OpenCodeReasoner.LooksLikeProbeLoop(
            native, "tool-call: {\"tool\":\"ProposeTask\",\"args\":{}}"));
    }

    [Fact]
    public void Probe_hint_mentions_text_protocol()
    {
        Assert.Contains("tool-call", OpenCodeReasoner.ProbeHint, StringComparison.Ordinal);
        Assert.Contains("tool-result", OpenCodeReasoner.ProbeHint, StringComparison.Ordinal);
    }

    [Fact]
    public void Single_echo_per_step_is_a_probe_step()
    {
        // 分诊会话实测：每步一个 echo + 一个 read，没有文本协议调用（38 步烧完整轮）。
        var native = new[]
        {
            Call("shell", "{\"command\":\"echo ok\"}"),
            Call("read", "{\"path\":\"src/OpenLoop.Core/Store/TaskDrafts.cs\"}")
        };
        Assert.True(OpenCodeReasoner.IsProbeStep(native, ""));
        Assert.False(OpenCodeReasoner.LooksLikeProbeLoop(native, ""));
        Assert.False(OpenCodeReasoner.IsProbeStep(
            native, "tool-call: {\"tool\":\"ProposeTask\",\"args\":{}}"));
        Assert.False(OpenCodeReasoner.IsProbeStep([Call("read", "{\"path\":\"a.cs\"}")], ""));
    }

    [Fact]
    public void Consecutive_probe_steps_are_nudged_on_third_and_every_third()
    {
        Assert.False(OpenCodeReasoner.ShouldNudgeProbeSteps(0));
        Assert.False(OpenCodeReasoner.ShouldNudgeProbeSteps(2));
        Assert.True(OpenCodeReasoner.ShouldNudgeProbeSteps(3));
        Assert.False(OpenCodeReasoner.ShouldNudgeProbeSteps(4));
        Assert.True(OpenCodeReasoner.ShouldNudgeProbeSteps(6));
    }

    [Fact]
    public void Only_echo_and_noop_native_calls_count_as_probes()
    {
        Assert.True(OpenCodeReasoner.IsProbeCall(Call("shell", "{\"command\":\"echo ok\"}")));
        Assert.True(OpenCodeReasoner.IsProbeCall(Call("execute", "{\"code\":\"return 1;\"}")));
        Assert.False(OpenCodeReasoner.IsProbeCall(Call("shell", "{\"command\":\"rg -n ProposeTask src\"}")));
        Assert.False(OpenCodeReasoner.IsProbeCall(Call("read", "{\"path\":\"a.cs\"}")));
        Assert.False(OpenCodeReasoner.IsProbeCall(null));
    }
}
