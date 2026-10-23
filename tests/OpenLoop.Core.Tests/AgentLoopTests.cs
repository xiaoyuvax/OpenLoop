using OpenLoop.Core.Agent;
using Xunit;

namespace OpenLoop.Core.Tests
{
    public class AgentLoopTests
    {
        [Fact]
        public async Task Retries_Once_When_Only_Safety_Verdict()
        {
            var reasoner = new ScriptedReasoner("User Safety: safe\nResponse Safety: safe", "final answer");
            var loop = Loops.Build(new FakeTool(), reasoner);

            var result = await loop.RunAsync([], "hi");

            Assert.Equal("final answer", result.FinalAnswer);
            Assert.Equal(2, reasoner.Calls.Count);
            Assert.Equal(1, result.Guards.EmptyRetries);
        }

        [Fact]
        public async Task Stops_After_One_Retry_When_Still_Empty()
        {
            var reasoner = new ScriptedReasoner("User Safety: safe", "Response Safety: safe");
            var loop = Loops.Build(new FakeTool(), reasoner);

            var result = await loop.RunAsync([], "hi");

            Assert.Equal(2, reasoner.Calls.Count);
            Assert.Equal("Response Safety: safe", result.FinalAnswer);
        }

        [Fact]
        public async Task ToolCallThenFinalAnswer()
        {
            var tool = new FakeTool();
            var reasoner = new ScriptedReasoner(Loops.Call("echo", "{\"x\":\"1\"}"), "最终答案");
            var loop = Loops.Build(tool, reasoner);

            var result = await loop.RunAsync([], "做点事");

            Assert.Equal("最终答案", result.FinalAnswer);
            Assert.Single(result.Steps);
            Assert.Equal("echo", result.Steps[0].Tool);
            Assert.True(result.Steps[0].Ok);
            Assert.Equal(1, tool.Calls);
        }

        [Fact]
        public async Task RepeatCallGuardStopsLoop()
        {
            var tool = new FakeTool();
            var reasoner = new ScriptedReasoner(
                Loops.Call("echo"), Loops.Call("echo"), Loops.Call("echo"), Loops.Call("echo"), Loops.Call("echo"));
            var loop = Loops.Build(tool, reasoner, new AgentPolicy { MaxRepeatCalls = 3 });

            var result = await loop.RunAsync([], "做点事");

            Assert.Contains("无进展", result.FinalAnswer);
            Assert.Equal(3, result.Steps.Count);
            Assert.Equal(1, result.Guards.RepeatCallStops);
        }

        [Fact]
        public async Task BudgetExhaustedAfterMaxSteps()
        {
            var tool = new FakeTool();
            var reasoner = new ScriptedReasoner(Loops.Call("echo", "{\"x\":\"1\"}"), Loops.Call("echo", "{\"x\":\"2\"}"));
            var loop = Loops.Build(tool, reasoner, new AgentPolicy { MaxSteps = 2 });

            var result = await loop.RunAsync([], "做点事");

            Assert.True(result.BudgetExhausted);
            Assert.Equal(2, result.Steps.Count);
        }

        [Fact]
        public async Task MalformedToolCallRetriesThenStops()
        {
            var tool = new FakeTool();
            var reasoner = new ScriptedReasoner("tool-call: {\"tool\":", "tool-call: {\"tool\":", "tool-call: {\"tool\":");
            var loop = Loops.Build(tool, reasoner, new AgentPolicy { MaxMalformedRetries = 2 });

            var result = await loop.RunAsync([], "做点事");

            Assert.Contains("JSON 格式错误", result.FinalAnswer);
            Assert.Equal(2, result.Guards.MalformedRetries);
        }

        [Fact]
        public async Task ClaimGuardNudgesWhenClaimingWithoutTool()
        {
            var tool = new FakeTool();
            var reasoner = new ScriptedReasoner("已写入 a.txt", "未执行任何操作");
            var loop = Loops.Build(tool, reasoner);

            var result = await loop.RunAsync([], "做点事");

            Assert.Equal("未执行任何操作", result.FinalAnswer);
            Assert.Empty(result.Steps);
            Assert.Equal(2, reasoner.Calls.Count);
            Assert.Equal(1, result.Guards.ClaimNudges);
        }

        [Fact]
        public async Task UnknownToolFailureIsFedBack()
        {
            var tool = new FakeTool();
            var reasoner = new ScriptedReasoner(Loops.Call("nope"), "结束");
            var loop = Loops.Build(tool, reasoner);

            var result = await loop.RunAsync([], "做点事");

            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Ok);
            Assert.Contains("未知工具", result.Steps[0].Summary);
        }

        [Fact]
        public async Task ArgRepairRetriesWithCoercedType()
        {
            var tool = new FakeTool
            {
                Handler = args =>
                {
                    var value = args.Raw["x"];
                    if (value is string)
                        return AgentToolResult.Failure("400 invalid parameter \"x\" Expected number, received string");
                    return AgentToolResult.Success("已修正");
                }
            };
            var reasoner = new ScriptedReasoner(Loops.Call("echo", "{\"x\":\"5\"}"), "结束");
            var loop = Loops.Build(tool, reasoner);

            var result = await loop.RunAsync([], "做点事");

            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Ok);
            Assert.True(result.Steps[0].ArgsRepaired);
            Assert.Equal(2, tool.Calls);
            Assert.Equal(1, result.Guards.ArgRepairAttempts);
            Assert.Equal(1, result.Guards.ArgRepairSuccesses);
        }

        [Fact]
        public async Task ExplorationGuardUsesToolIsReadOnly()
        {
            var tool = new FakeTool { IsReadOnly = true };
            var reasoner = new ScriptedReasoner(
                Loops.Call("echo", "{\"x\":\"1\"}"),
                Loops.Call("echo", "{\"x\":\"2\"}"),
                Loops.Call("echo", "{\"x\":\"3\"}"),
                "不会到这里");
            var loop = Loops.Build(tool, reasoner, new AgentPolicy { MaxExplorationSteps = 2 });

            var result = await loop.RunAsync([], "做点事");

            Assert.Contains("原地打转", result.FinalAnswer);
            Assert.Equal(2, result.Steps.Count);
            Assert.Equal(1, result.Guards.ExplorationStops);
        }

        [Fact]
        public async Task SoftStopEndsBeforeNextTurn()
        {
            var tool = new FakeTool();
            var reasoner = new ScriptedReasoner(Loops.Call("echo"), "不会到这里");
            var loop = Loops.Build(tool, reasoner);

            bool stop = false;
            var result = await loop.RunAsync([], "做点事", shouldStop: () => stop);
            stop = true;
            var second = await loop.RunAsync([], "再做点事", shouldStop: () => true);

            Assert.False(result.StoppedByUser);
            Assert.True(second.StoppedByUser);
            Assert.Null(second.FinalAnswer);
        }

        [Theory]
        [InlineData("我看了目录和部分源文件。项目是一个 .NET 控制台应用。让我继续检查其他文件以全面了解。")]
        [InlineData("让我继续读剩下的代码")]
        [InlineData("我接着看其他文件")]
        [InlineData("接下来我会扫描全部模块")]
        [InlineData("I'll continue checking the rest of the files")]
        public void Detects_Continuation_Promises(string text)
            => Assert.True(AgentLoop.LooksLikeActionPromise(text));

        [Theory]
        [InlineData("我看了目录和源文件。项目是 .NET 控制台应用，主要处理程序集展平。")]
        [InlineData("优化方案：1) 拆分大文件 2) 加缓存 3) 补测试")]
        [InlineData("No further action needed.")]
        public void Does_Not_Flag_Final_Answers(string text)
            => Assert.False(AgentLoop.LooksLikeActionPromise(text));

        [Fact]
        public async Task Promise_Without_Tool_Call_Is_Nudged_And_Continues()
        {
            var tool = new FakeTool();
            var reasoner = new ScriptedReasoner(
                Loops.Call("echo", "{\"x\":\"1\"}"),
                "我看了目录和部分源文件。让我继续检查其他文件以全面了解。",
                Loops.Call("echo", "{\"x\":\"2\"}"),
                "优化方案已给出");
            var loop = Loops.Build(tool, reasoner);

            var result = await loop.RunAsync([], "审查代码并给方案");

            Assert.Equal("优化方案已给出", result.FinalAnswer);
            Assert.Equal(2, tool.Calls);
            Assert.Equal(1, result.Guards.PromiseNudges);
        }
    }
}
