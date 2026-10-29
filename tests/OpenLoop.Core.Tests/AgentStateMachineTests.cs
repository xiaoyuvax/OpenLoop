using OpenLoop.Core.Agent;
using OpenLoop.Core.Tools;
using Xunit;

namespace OpenLoop.Core.Tests
{
    public class AgentStateMachineTests
    {
        [Fact]
        public async Task Yield_Is_Resolved_By_ConfirmContinue()
        {
            int asked = 0;
            var step = new DelegateStep("ask", (state, _) =>
            {
                if (state.PendingApproval == null && state.ApprovalGranted == null)
                {
                    state.PendingApproval = "继续？";
                    return AgentStepResult.Yield();
                }

                if (state.ApprovalGranted == true)
                {
                    state.Result.FinalAnswer = "done";
                    return AgentStepResult.Complete();
                }

                return AgentStepResult.Pass;
            });

            var machine = new AgentStateMachine([step]);
            var state = new AgentState([], "hi") { MaxSteps = 3 };
            var context = new AgentRunContext
            {
                ConfirmContinue = _ => { asked++; return true; }
            };

            var result = await machine.RunAsync(state, context);

            Assert.Equal(1, asked);
            Assert.Equal("done", result.FinalAnswer);
        }

        [Fact]
        public async Task AgentLoop_Can_Be_Driven_Step_By_Step()
        {
            var reasoner = new ScriptedReasoner("最终答案");
            var loop = Loops.Build(new FakeTool(), reasoner);
            var state = loop.CreateState([], "hi");
            var context = loop.CreateContext();
            var machine = loop.CreateMachine();

            var first = await machine.StepAsync(state, context);
            machine.Apply(state, context, first);

            Assert.True(first.Applied);
            Assert.Equal(AgentStepStatus.Continuing, first.Status);
            Assert.Equal(1, state.Step);
            Assert.Equal("assistant", state.Messages[^1].Role);

            var second = await machine.StepAsync(state, context);
            machine.Apply(state, context, second);

            Assert.Equal(AgentStepStatus.Completed, second.Status);
            Assert.Equal("最终答案", state.Result.FinalAnswer);
        }

        [Fact]
        public async Task SoftStop_After_Tool_Does_Not_Start_Another_Turn()
        {
            var tool = new FakeTool();
            var reasoner = new ScriptedReasoner(
                Loops.Call("echo"), Loops.Call("echo"), "不会到这里");
            var loop = Loops.Build(tool, reasoner);

            var result = await loop.RunAsync([], "做点事", shouldStop: () => tool.Calls >= 1);

            Assert.True(result.StoppedByUser);
            Assert.Equal(1, tool.Calls);
            Assert.Null(result.FinalAnswer);
        }

        [Fact]
        public async Task ExplorationGuard_Yields_And_Continues_When_Approved()
        {
            var tool = new FakeTool { IsReadOnly = true };
            var reasoner = new ScriptedReasoner(
                Loops.Call("echo", "{\"x\":\"1\"}"),
                Loops.Call("echo", "{\"x\":\"2\"}"),
                Loops.Call("echo", "{\"x\":\"3\"}"),
                "结束");

            int approvals = 0;
            var loop = new AgentLoop(
                new ToolRegistry([tool]), reasoner,
                new AgentPolicy { MaxExplorationSteps = 2 },
                confirmContinue: _ => { approvals++; return true; });

            var result = await loop.RunAsync([], "做点事");

            Assert.Equal(1, approvals);
            Assert.Equal(1, result.Guards.ExplorationContinues);
            Assert.Equal(0, result.Guards.ExplorationStops);
            Assert.Equal("结束", result.FinalAnswer);
            Assert.Equal(3, tool.Calls);
        }

        private sealed class DelegateStep : IAgentStep
        {
            private readonly Func<AgentState, AgentRunContext, AgentStepResult> _run;

            public DelegateStep(string name, Func<AgentState, AgentRunContext, AgentStepResult> run)
            {
                Name = name;
                _run = run;
            }

            public string Name { get; }

            public Task<AgentStepResult> RunAsync(
                AgentState state, AgentRunContext context, CancellationToken ct)
                => Task.FromResult(_run(state, context));
        }
    }
}
