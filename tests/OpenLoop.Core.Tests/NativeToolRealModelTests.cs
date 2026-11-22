using OpenLoop.Core.Agent;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Tools;
using Xunit;
using Xunit.Abstractions;

namespace OpenLoop.Core.Tests;

/// <summary>
/// 直连 provider 的真实模型端到端：默认不跑（会花额度），设 OPENLOOP_NATIVE_REAL=1 才执行。
/// 系统提示词里刻意不提 tool-call 文本协议——文件能产出就说明原生 tools 通道真的生效了。
/// 模型用 OPENLOOP_NATIVE_MODEL 覆盖，默认 deepseek-v4.1-flash。
/// </summary>
public class NativeToolRealModelTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Direct_provider_completes_task_through_native_tools()
    {
        if (Environment.GetEnvironmentVariable("OPENLOOP_NATIVE_REAL") != "1") return;

        string model = Environment.GetEnvironmentVariable("OPENLOOP_NATIVE_MODEL") is { Length: > 0 } m
            ? m
            : "deepseek-v4.1-flash";
        string key = File.ReadAllText(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenLoop", "plugins", "agent2api", "key.txt")).Trim();

        string workspace = Path.Combine(Path.GetTempPath(), "openloop-native-real-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workspace);
        try
        {
            var scope = new WorkspaceScope(workspace);
            var tools = ToolRegistry.Workspace(scope);
            var reasoner = new OpenAiCompatibleReasoner("http://127.0.0.1:3065/v1", model, key);
            var loop = new AgentLoop(tools, reasoner);

            var run = await loop.RunAsync(
                [new LlmMessage("system",
                    "You are a coding agent working inside a workspace. Use the tools you are given to do the task.")],
                "Create a file named hello.txt in the workspace whose content is exactly HELLO. "
                + "Then tell me it is done.",
                contextLimit: 100000);

            output.WriteLine($"model={model} steps={run.Steps.Count} malformed={run.Guards.MalformedRetries}");
            foreach (var step in run.Steps)
                output.WriteLine($"  {step.Index}. {step.Tool}({step.ArgsSummary}) -> {(step.Ok ? "ok" : "fail")}");
            output.WriteLine("answer: " + run.FinalAnswer);

            string file = Path.Combine(workspace, "hello.txt");
            Assert.True(File.Exists(file), "模型没有通过原生 tools 通道写出文件");
            Assert.Equal("HELLO", File.ReadAllText(file).Trim());
            Assert.Equal(0, run.Guards.MalformedRetries);
        }
        finally
        {
            try { Directory.Delete(workspace, true); } catch { }
        }
    }
}
