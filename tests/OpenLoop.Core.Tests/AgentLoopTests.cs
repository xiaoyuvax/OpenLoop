using OpenLoop.Core.Agent;
using OpenLoop.Core.Reasoning;
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

            // 推理文本不算正文：重试用尽后给一句可操作的提示，而不是空白回复
            Assert.Contains("没有返回正文", result.FinalAnswer);
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
        public async Task ExtraMessages_Are_Appended_Before_Final_Answer()
        {
            var reasoner = new ExtraMessagesReasoner();
            var loop = Loops.Build(new FakeTool(), reasoner);

            var result = await loop.RunAsync([], "hi");

            Assert.Equal(4, result.Transcript.Count);
            Assert.Equal("user", result.Transcript[0].Role);
            Assert.Equal("assistant", result.Transcript[1].Role);
            Assert.Contains("native:read", result.Transcript[1].Content);
            Assert.Equal("user", result.Transcript[2].Role);
            Assert.Contains("FILE", result.Transcript[2].Content);
            Assert.Equal("assistant", result.Transcript[3].Role);
            Assert.Equal("完成", result.Transcript[3].Content);
            Assert.Equal("完成", result.FinalAnswer);
            Assert.Equal(1, reasoner.Calls);
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
    public void Output_token_budget_scales_with_context_window()
    {
        // 预算刻意给大：按窗口倍数算，长思考不该被小上限砍掉。
        var policy = new AgentPolicy { OutputTokenRatio = 8 };
        Assert.Equal(262144, policy.OutputTokenBudgetFor(32768));
        Assert.Equal(0, new AgentPolicy { OutputTokenRatio = 0 }.OutputTokenBudgetFor(32768));
        Assert.Equal(0, policy.OutputTokenBudgetFor(0));
    }

    [Fact]
    public async Task Output_token_budget_stops_softly_and_keeps_progress()
    {
        var tool = new FakeTool();
        var calls = Enumerable.Range(0, 6).Select(i => Loops.Call("echo", $"{{\"x\":\"{i}\"}}")).ToArray();
        // 每步报满输出 token：窗口 1000 × 比例 1 = 预算 1000，第一步之后就该收手。
        var loop = Loops.Build(tool, new TokenReportingReasoner(calls, 1000), new AgentPolicy { OutputTokenRatio = 1 });

        var result = await loop.RunAsync([], "做点事", contextLimit: 1000);

        Assert.True(result.BudgetExhausted);
        Assert.Equal(1, result.Guards.OutputBudgetStops);
        Assert.Equal(1, result.Steps.Count);
        Assert.Contains("输出 token 预算", result.FinalAnswer);
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
        public async Task StalledTurnCompressesAndRetriesLikeContextOverflow()
        {
            var history = Enumerable.Range(0, 8)
                .Select(i => new OpenLoop.Core.Messages.LlmMessage(i % 2 == 0 ? "user" : "assistant", "内容" + i))
                .ToList();
            var reasoner = new StallOnceReasoner();
            var loop = Loops.Build(new FakeTool(), reasoner);

            var result = await loop.RunAsync(history, "hi");

            Assert.Equal("完成", result.FinalAnswer);
            Assert.Equal(1, result.Guards.Compressions);
            Assert.Equal(3, reasoner.Calls.Count);
            Assert.Contains("此前对话摘要", reasoner.Calls[2][0].Content);
        }

        [Fact]
        public async Task StalledTurnWithOversizedHistoryDropsTheMiddleInsteadOfSummarizing()
        {
            var history = Enumerable.Range(0, 12)
                .Select(i => new OpenLoop.Core.Messages.LlmMessage(
                    i % 2 == 0 ? "user" : "assistant", new string('話', 4000)))
                .ToList();
            var reasoner = new StallOnceReasoner();
            var loop = Loops.Build(new FakeTool(), reasoner);

            var result = await loop.RunAsync(history, "hi");

            Assert.Equal("完成", result.FinalAnswer);
            Assert.Equal(1, result.Guards.Compressions);
            Assert.Equal(2, reasoner.Calls.Count);   // 只重发一次，没有为摘要再调一次模型
            Assert.Contains("较早的对话已省略", reasoner.Calls[1][0].Content);
        }

        [Fact]
        public async Task StalledTurnUsesUpstreamCompactionSummaryInsteadOfDroppingIt()
        {
            var history = Enumerable.Range(0, 8)
                .Select(i => new OpenLoop.Core.Messages.LlmMessage(
                    i % 2 == 0 ? "user" : "assistant", new string('內', 200) + i))
                .ToList();
            var reasoner = new StallOnceReasoner("上游摘要：目标、决定与进度");
            var loop = Loops.Build(new FakeTool(), reasoner);

            var result = await loop.RunAsync(history, "hi");

            Assert.Equal("完成", result.FinalAnswer);
            Assert.Equal(1, result.Guards.Compressions);
            Assert.Equal(2, reasoner.Calls.Count);
            Assert.Contains("较早的对话已由上游压缩", reasoner.Calls[1][0].Content);
            Assert.Contains("上游摘要：目标、决定与进度", reasoner.Calls[1][0].Content);
        }

        [Fact]
        public async Task UpstreamCompactionSummaryReplacesOlderHistoryAfterANormalTurn()
        {
            var history = Enumerable.Range(0, 12)
                .Select(i => new OpenLoop.Core.Messages.LlmMessage(
                    i % 2 == 0 ? "user" : "assistant", new string('內', 2000) + i))
                .ToList();
            var reasoner = new CompactingOnceReasoner("上游摘要：已完成的决定与进度");
            var loop = Loops.Build(new FakeTool(), reasoner);

            var result = await loop.RunAsync(history, "hi");

            Assert.Equal("完成", result.FinalAnswer);
            Assert.Equal(1, result.Guards.Compressions);
            Assert.True(result.Transcript.Count < history.Count + 2);
            Assert.Contains(result.Transcript, m => m.Content.Contains("上游摘要：已完成的决定与进度"));
        }

        [Fact]
        public async Task DegenerateOutputIsRetriedInsteadOfFailingTheRun()
        {
            var history = new List<OpenLoop.Core.Messages.LlmMessage> { new("user", "hi") };
            var reasoner = new DegenerateOnceReasoner();
            var loop = Loops.Build(new FakeTool(), reasoner);

            var result = await loop.RunAsync(history, "hi");

            Assert.Equal("完成", result.FinalAnswer);
            Assert.Equal(2, reasoner.Calls.Count);
            Assert.Contains(reasoner.Calls[1], m => (m.Content ?? "").Contains("输出退化"));
        }

        [Fact]
        public async Task DegenerateOutputStopsSoftlyWithProgressInsteadOfFailing()
        {
            var history = new List<OpenLoop.Core.Messages.LlmMessage> { new("user", "hi") };
            var reasoner = new DegenerateAlwaysReasoner();
            var loop = Loops.Build(new FakeTool(), reasoner);
            int restarts = 0;

            // 护栏不再整轮抛异常：用户看到的是一段带进度的答复，而不是一条空白失败轮
            var result = await loop.RunAsync(history, "hi", onRestart: () => restarts++);

            Assert.Equal(3, reasoner.Calls);   // 两次重试 + 第三次收尾
            Assert.Equal(3, restarts);         // 每次退化都通知宿主清屏
            Assert.True(result.StoppedByGuard);
            Assert.Equal(1, result.Guards.DegenerateStops);
            Assert.Contains("跑偏", result.FinalAnswer);
            Assert.Contains("继续", result.FinalAnswer);
        }

        [Fact]
        public async Task EmptyAnswerGetsActionableTextInsteadOfBlank()
        {
            var history = new List<OpenLoop.Core.Messages.LlmMessage> { new("user", "hi") };
            var loop = Loops.Build(new FakeTool(), new ReasoningOnlyReasoner("想了一下，但没有结论。"));

            var result = await loop.RunAsync(history, "hi");

            // 正文为空时不能留一条空白回复（用户只看到「没有正文」等于静默中断）
            Assert.False(string.IsNullOrWhiteSpace(result.FinalAnswer));
            Assert.Contains("继续", result.FinalAnswer);
        }

        [Fact]
        public async Task ContextTokens_Uses_Prompt_Estimate_Not_Channel_Bill()
        {
            var history = new List<OpenLoop.Core.Messages.LlmMessage> { new("user", "你好") };
            var loop = Loops.Build(new FakeTool(), new BillingReasoner(700_000));
            int sent = 0;

            var result = await loop.RunAsync(history, "hi", onRequest: (_, tokens) => sent = tokens);

            Assert.Equal(sent, result.ContextTokens ?? -1);
            Assert.True(result.ContextTokens is > 0 and < 700_000);
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
        [InlineData("当前正在运行的构建已经包含这段逻辑。")]
        [InlineData("接下来我检查了文件预览的调用链，确认 README 走的是 MarkdownTextBox，代码文件才走 FastColoredTextBox。控件在 OpenLoop 的 FilePreviewView 里创建，路径判断用扩展名，大小写不敏感。预览面板的布局由 SessionView 和 LoopView 各自持有，两个入口调用的是同一个 TryShowFile。相关的包引用在 Directory.Packages.props，版本 1.5.7，与 ChatFlow 一致。测试工程里没有覆盖这条 UI 分支，但核心分流函数是纯逻辑，可以直接断言扩展名到控件的映射。日志和缓存目录都在用户目录下，不受仓库改动影响。如果后续要支持 .markdown 之外的后缀，只需要在判断表里加一行，其他路径不用动。结论：现有实现已经满足要求，不需要再改代码。")]
        public void Does_Not_Flag_Final_Answers(string text)
            => Assert.False(AgentLoop.LooksLikeActionPromise(text));

        [Theory]
        [InlineData("结论：不需要改动。\n[final]", true)]
        [InlineData("结论：不需要改动。 [FINAL]", true)]
        [InlineData("正文里提到 [final] 这个词，但不是收尾标记。", false)]
        public void Final_Marker_Is_Only_Recognized_At_The_End(string text, bool expected)
            => Assert.Equal(expected, AgentLoop.HasFinalMarker(text));

        [Fact]
        public void Final_Marker_Is_Stripped_From_The_Answer()
            => Assert.Equal("结论：不需要改动。", AgentLoop.StripFinalMarker("结论：不需要改动。\n\n[final]"));

        [Fact]
        public async Task Final_Marker_Suppresses_Promise_Nudge_And_Is_Stripped()
        {
            var reasoner = new ScriptedReasoner("接下来我会继续检查其他文件。\n[final]");
            var loop = Loops.Build(new FakeTool(), reasoner);

            var result = await loop.RunAsync([], "审查代码并给方案");

            Assert.Equal(0, result.Guards.PromiseNudges);
            Assert.Equal("接下来我会继续检查其他文件。", result.FinalAnswer);
            Assert.Single(reasoner.Calls);
        }

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

        [Fact]
        public async Task Compression_failure_does_not_block_the_turn()
        {
            // 实测：摘要调用失败（额度/网络/模型报错）时自动压缩把异常吞成 null，
            // 手动 /compact 也因此一律误报「上下文太短」。现在 CompressAsync 向上抛，
            // 自动压缩按「压不动」跳过，正常轮次不被摘要通道的故障拦住。
            var history = Enumerable.Range(0, 12)
                .Select(i => new OpenLoop.Core.Messages.LlmMessage(
                    i % 2 == 0 ? "user" : "assistant", new string('話', 200)))
                .ToList();
            // 第一次（摘要）成功但摘要比原文长 → 触发不了压缩重入；
            // 这里要的是「摘要调用抛异常」：第一次调用就抛。
            var reasoner = new OverflowThenThrowReasoner(
                new InvalidOperationException("摘要通道炸了"));
            var loop = Loops.Build(new FakeTool(), reasoner, new AgentPolicy { CompressAt = 0.25 });

            var result = await loop.RunAsync(history, "hi", contextLimit: 4000);

            Assert.Equal("完成", result.FinalAnswer);
            Assert.Equal(0, result.Guards.Compressions);
        }

        [Fact]
        public async Task Context_overflow_is_kept_when_the_summary_call_fails()
        {
            // 溢出后摘要调用又失败：交出去的必须是原始的上下文溢出错误，不能被摘要故障顶替。
            var history = Enumerable.Range(0, 8)
                .Select(i => new OpenLoop.Core.Messages.LlmMessage(
                    i % 2 == 0 ? "user" : "assistant", "内容" + i))
                .ToList();
            var reasoner = new OverflowThenThrowReasoner(
                new InvalidOperationException("模型接口返回 400：This model's maximum context length is 1000 tokens"),
                new InvalidOperationException("摘要通道炸了"));
            var loop = Loops.Build(new FakeTool(), reasoner);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => loop.RunAsync(history, "hi"));
            Assert.Contains("maximum context length", ex.Message);
        }
    }

    /// <summary>按脚本依次抛异常：第 N 次调用抛第 N 个异常；脚本用尽后返回「完成」。
    /// 用于验证自动压缩失败不拦路、溢出根因不被摘要故障顶替。</summary>
    internal sealed class OverflowThenThrowReasoner(params Exception[] script) : IReasoner
    {
        private int _calls;

        public Task<CompletionResult> CompleteAsync(
            IReadOnlyList<OpenLoop.Core.Messages.LlmMessage> messages, CancellationToken ct = default)
        {
            if (_calls < script.Length)
            {
                _calls++;
                throw script[_calls - 1];
            }
            return Task.FromResult(new CompletionResult { Content = "完成" });
        }
    }
}
