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
        public async Task Stops_After_Three_Retries_When_Still_Empty()
        {
            var reasoner = new ScriptedReasoner("User Safety: safe", "Response Safety: safe");
            var loop = Loops.Build(new FakeTool(), reasoner);

            var result = await loop.RunAsync([], "hi");

            Assert.Equal(3, reasoner.Calls.Count);
            Assert.Equal(2, result.Guards.EmptyRetries);
            Assert.Equal("完成", result.FinalAnswer);
        }

        [Fact]
        public async Task Empty_Content_Is_Retried_Not_Replaced_By_Reasoning()
        {
            var reasoner = new ReasoningOnlyReasoner("先看目录，再逐个确认问题。");
            var loop = Loops.Build(new FakeTool(), reasoner);

            var result = await loop.RunAsync([], "hi");

            Assert.True(string.IsNullOrEmpty(result.FinalAnswer));
            Assert.Equal(4, reasoner.Calls);
            Assert.Equal(3, result.Guards.EmptyRetries);
        }

        [Fact]
        public async Task ToolCall_Carried_In_Reasoning_Is_Executed()
        {
            var tool = new FakeTool();
            var reasoner = new ReasoningOnlyReasoner(Loops.Call("echo", "{\"x\":\"1\"}"));
            var loop = Loops.Build(tool, reasoner);

            var result = await loop.RunAsync([], "hi");

            Assert.Single(result.Steps);
            Assert.Equal("echo", result.Steps[0].Tool);
            Assert.Equal(1, tool.Calls);
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
        public async Task RepeatCallGuardNudgesThenBansInsteadOfStopping()
        {
            // 4 次完全相同：第 2 次提醒、第 3 次提醒+禁用、第 4 次被拒收后仍给模型一次改做法的机会
            var tool = new FakeTool();
            var reasoner = new ScriptedReasoner(
                Loops.Call("echo"), Loops.Call("echo"), Loops.Call("echo"), Loops.Call("echo"));
            var loop = Loops.Build(tool, reasoner, new AgentPolicy { MaxRepeatCalls = 2 });

            var result = await loop.RunAsync([], "做点事");

            Assert.Equal("完成", result.FinalAnswer);
            Assert.Equal(4, result.Steps.Count);
            Assert.Equal(3, tool.Calls);
            Assert.Equal(2, result.Guards.RepeatNudges);
            Assert.Equal(1, result.Guards.RepeatBans);
            Assert.False(result.StoppedByGuard);
        }

        [Fact]
        public async Task RepeatCallGuardStopsAfterBanIsIgnored()
        {
            var tool = new FakeTool();
            var calls = Enumerable.Repeat(Loops.Call("echo"), 6).ToArray();
            var loop = Loops.Build(tool, new ScriptedReasoner(calls), new AgentPolicy { MaxRepeatCalls = 2 });

            var result = await loop.RunAsync([], "做点事");

            Assert.True(result.StoppedByGuard);
            Assert.Equal(1, result.Guards.RepeatCallStops);
        }

        [Fact]
        public async Task BudgetAutoExtendsBeforeGivingUp()
        {
            var tool = new FakeTool();
            var reasoner = new ScriptedReasoner(Loops.Call("echo", "{\"x\":\"1\"}"), Loops.Call("echo", "{\"x\":\"2\"}"));
            var loop = Loops.Build(tool, reasoner, new AgentPolicy { MaxSteps = 2 });

            var result = await loop.RunAsync([], "做点事");

            Assert.False(result.BudgetExhausted);
            Assert.Equal(1, result.Guards.BudgetExtensions);
            Assert.Equal("完成", result.FinalAnswer);
        }

        [Fact]
        public async Task BudgetStopsAfterExtensionLimit()
        {
            var tool = new FakeTool();
            var calls = Enumerable.Range(0, 6).Select(i => Loops.Call("echo", $"{{\"x\":\"{i}\"}}")).ToArray();
            var loop = Loops.Build(tool, new ScriptedReasoner(calls), new AgentPolicy
            {
                MaxSteps = 1,
                BudgetExtendSteps = 1,
                MaxBudgetExtensions = 3
            });

            var result = await loop.RunAsync([], "做点事");

            Assert.True(result.BudgetExhausted);
            Assert.Equal(3, result.Guards.BudgetExtensions);
            Assert.Equal(4, result.Steps.Count);
        }

        [Fact]
        public async Task NoStepLimitWhenMaxStepsIsZero()
        {
            var tool = new FakeTool();
            var reasoner = new ScriptedReasoner(
                Loops.Call("echo", "{\"x\":\"1\"}"),
                Loops.Call("echo", "{\"x\":\"2\"}"),
                Loops.Call("echo", "{\"x\":\"3\"}"),
                Loops.Call("echo", "{\"x\":\"4\"}"),
                "最终答案");
            var loop = Loops.Build(tool, reasoner);

            var result = await loop.RunAsync([], "做点事");

            Assert.False(result.BudgetExhausted);
            Assert.Equal(4, result.Steps.Count);
            Assert.Equal("最终答案", result.FinalAnswer);
        }

        [Fact]
        public async Task TruncatedToolCallIsRepairedAndExecuted()
        {
            var tool = new FakeTool();
            const string truncated = "tool-call: {\"tool\":\"echo\",\"args\":{\"x\":\"1\"";
            var loop = Loops.Build(tool, new ScriptedReasoner(truncated, "完成"));

            var result = await loop.RunAsync([], "做点事");

            Assert.Equal(1, tool.Calls);
            Assert.Equal(1, result.Guards.MalformedRepairs);
            Assert.Equal("完成", result.FinalAnswer);
        }

        [Fact]
        public async Task FullWidthLeakedToolCallIsRecovered()
        {
            const string leak = "<｜tool\":\"ReadEvidence\",\"args\":{\"path\":\"loop/findings.jsonl\",\"limit\":40}}";
            var tool = new FakeTool { Name = "ReadEvidence" };
            var loop = Loops.Build(tool, new ScriptedReasoner(leak, "完成"));

            var result = await loop.RunAsync([], "继续");

            Assert.Equal(1, tool.Calls);
            Assert.False(result.StoppedByGuard);
            Assert.Equal("完成", result.FinalAnswer);
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
            Assert.True(result.StoppedByGuard);
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
        public async Task ExplorationGuardNudgesBeforeStopping()
        {
            var tool = new FakeTool { IsReadOnly = true };
            var reasoner = new ScriptedReasoner(
                Loops.Call("echo", "{\"x\":\"1\"}"),
                Loops.Call("echo", "{\"x\":\"2\"}"),
                Loops.Call("echo", "{\"x\":\"3\"}"));
            var loop = Loops.Build(tool, reasoner, new AgentPolicy { MaxExplorationSteps = 2 });

            var result = await loop.RunAsync([], "做点事");

            Assert.Equal(2, result.Guards.ExplorationNudges);
            Assert.Equal(0, result.Guards.ExplorationStops);
            Assert.False(result.StoppedByGuard);
            Assert.Equal("完成", result.FinalAnswer);
        }

        [Fact]
        public async Task ExplorationGuardStopsAfterNudgesAreIgnored()
        {
            var tool = new FakeTool { IsReadOnly = true };
            var calls = Enumerable.Range(0, 5)
                .Select(i => Loops.Call("echo", $"{{\"x\":\"{i}\"}}")).ToArray();
            var loop = Loops.Build(tool, new ScriptedReasoner(calls), new AgentPolicy { MaxExplorationSteps = 2 });

            var result = await loop.RunAsync([], "做点事");

            Assert.True(result.StoppedByGuard);
            Assert.Equal(1, result.Guards.ExplorationStops);
            Assert.Contains("原地打转", result.FinalAnswer);
        }

        [Fact]
        public async Task PollDeclarationExemptsRepeatsWithinItsBudget()
        {
            var tool = new FakeTool();
            var script = Enumerable.Range(0, 5)
                .Select(i => i == 0
                    ? Loops.PollCall("echo", "{\"x\":\"1\"}", "build-1", 5)
                    : Loops.Call("echo", "{\"x\":\"1\"}"))
                .ToArray();
            var loop = Loops.Build(tool, new ScriptedReasoner(script));

            var result = await loop.RunAsync([], "等构建");

            Assert.Equal(5, tool.Calls);
            Assert.Equal(4, result.Guards.PollExemptions);
            Assert.Equal(0, result.Guards.RepeatNudges);
            Assert.False(result.StoppedByGuard);
        }

        [Fact]
        public async Task PollBudgetIsCappedByPolicy()
        {
            var tool = new FakeTool();
            var script = Enumerable.Range(0, 5)
                .Select(i => Loops.PollCall("echo", "{\"x\":\"1\"}", "build-1", 8))
                .ToArray();
            var loop = Loops.Build(tool, new ScriptedReasoner(script),
                new AgentPolicy { MaxPollRepeats = 1 });

            var result = await loop.RunAsync([], "等构建");

            Assert.Equal(1, result.Guards.PollExemptions);
            Assert.True(result.Guards.RepeatNudges >= 1);
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

        [Fact]
        public async Task Compression_that_cannot_shrink_any_further_stops_instead_of_spinning()
        {
            // 实测：最近几条自己就超过阈值时，压缩只剩「摘要的摘要」，长度不再下降，
            // 状态机每圈重入压缩、每次打一次模型，永不结束（本地假网关 2500 次/秒）。
            string chat = new('话', 40);
            var history = Enumerable.Range(0, 8)
                .Select(i => new OpenLoop.Core.Messages.LlmMessage(i % 2 == 0 ? "user" : "assistant", chat))
                .ToList();
            var reasoner = new SummarizeReasoner(new string('摘', 20));
            var loop = Loops.Build(new FakeTool(), reasoner, new AgentPolicy { CompressAt = 0.5 });

            var run = loop.RunAsync(history, "hi", contextLimit: 400);
            var done = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.Same(run, done);

            var result = await run;
            Assert.Equal(1, result.Guards.Compressions);
            Assert.True(reasoner.Summaries <= 3, $"压缩被反复重试：{reasoner.Summaries} 次");
            Assert.False(string.IsNullOrWhiteSpace(result.FinalAnswer));
        }

        [Fact]
        public async Task Long_history_is_compressed_below_the_threshold()
        {
            // 尾部按 token 留（这里预算 = 4000/4 = 1000），压缩后才可能真的降到阈值以下
            var history = Enumerable.Range(0, 12)
                .Select(i => new OpenLoop.Core.Messages.LlmMessage(
                    i % 2 == 0 ? "user" : "assistant", new string('話', 200)))
                .ToList();
            var reasoner = new SummarizeReasoner(new string('摘', 20));
            var loop = Loops.Build(new FakeTool(), reasoner, new AgentPolicy { CompressAt = 0.25 });

            var result = await loop.RunAsync(history, "hi", contextLimit: 4000);

            Assert.True(result.Guards.Compressions >= 1, "长历史没有触发压缩");
            Assert.Contains(result.Transcript,
                m => (m.Content ?? "").StartsWith(AgentContext.SummaryPrefix, StringComparison.Ordinal));
        }

        [Fact]
        public async Task ContextOverflowErrorCompressesAndRetries()
        {
            var history = Enumerable.Range(0, 8)
                .Select(i => new OpenLoop.Core.Messages.LlmMessage(i % 2 == 0 ? "user" : "assistant", "内容" + i))
                .ToList();
            var reasoner = new OverflowOnceReasoner();
            var loop = Loops.Build(new FakeTool(), reasoner);

            var result = await loop.RunAsync(history, "hi");

            Assert.Equal("完成", result.FinalAnswer);
            Assert.Equal(1, result.Guards.Compressions);
            Assert.Equal(3, reasoner.Calls.Count);
            Assert.Contains("此前对话摘要", reasoner.Calls[2][0].Content);
        }

        [Fact]
        public void LongArgPreviewElidesMiddleAndKeepsFileTail()
        {
            string path = @"C:\root\" + new string('a', 80) + @"\Main.cs";
            string preview = AgentLoop.SummarizeArgs(new Dictionary<string, object> { ["path"] = path });

            Assert.StartsWith(@"path=C:\root\aaaa", preview);
            Assert.EndsWith($@"Main.cs（共 {path.Length} 字符）", preview);
            Assert.DoesNotContain("截断", preview);
        }

        [Fact]
        public void MultilineArgsStayOneLineAndKeepEveryName()
        {
            // 工具行是一条文本：值里的换行会把「(…)」撑开，参数名/等号必须全在
            string content = "line1\nline2\n\t" + new string('x', 200) + "\nTAIL";
            string preview = AgentLoop.SummarizeArgs(new Dictionary<string, object>
            {
                ["path"] = "src/A.cs",
                ["content"] = content,
                ["append"] = false,
            });

            Assert.DoesNotContain("\n", preview);
            Assert.DoesNotContain("\t", preview);
            Assert.StartsWith("path=src/A.cs, content=line1 line2", preview);
            Assert.Contains("TAIL", preview);
            Assert.EndsWith($"{content.Length} 字符）, append=False", preview);
        }

        [Theory]
        [InlineData("我看了目录和部分源文件。项目是一个 .NET 控制台应用。让我继续检查其他文件以全面了解。")]
        [InlineData("让我继续读剩下的代码")]
        [InlineData("我接着看其他文件")]
        [InlineData("接下来我会扫描全部模块")]
        [InlineData("I'll continue checking the rest of the files")]
        [InlineData("First let me re-read the chunk. Let me get the exact content of lines 124-129 that got truncated.")]
        [InlineData("Let me fetch the remaining part of the file")]
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
