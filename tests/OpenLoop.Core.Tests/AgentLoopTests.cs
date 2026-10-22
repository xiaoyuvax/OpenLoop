using OpenLoop.Core.Agent;
using Xunit;

namespace OpenLoop.Core.Tests
{
    public class AgentLoopTests
    {
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
    }
}
