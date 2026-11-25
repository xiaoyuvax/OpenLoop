using System.Text.Json;
using OpenLoop.Core.Agent;

namespace OpenLoop.Core.Store
{
    /// <summary>
    /// 种子特征与「激活记录 → 运行时形态」的映射。
    /// 映射表是代码，因为消费者是代码（规范 §5.3：表示形式由消费者决定）。
    /// </summary>
    public static class FeatureCatalog
    {
        public const string RepeatCall = "guard.repeat_call";
        public const string Exploration = "guard.exploration";
        public const string Claim = "guard.claim";
        public const string Promise = "guard.promise";
        public const string ArgRepair = "repair.args";
        public const string MalformedRetry = "retry.malformed";
        public const string ConnectRetry = "guard.connect_retry";
        public const string FreeLimit = "guard.free_limit";
        public const string Degenerate = "guard.degenerate";
        public const string MaxSteps = "limit.steps";
        public const string ToolResultLimit = "limit.tool_result";

        /// <summary>Puppet 单次响应上限（字节）；会话板/日志/截图 base64 都可能很长，与普通工具结果分开调。</summary>
        public const string PuppetResultLimit = "limit.puppet_result";
        public const string CompressAt = "compress.at";
        public const string OutputTokenRatio = "limit.output_tokens";
        public const string MentionTurns = "limit.mention_turns";
        public const string Poll = "limit.poll";
        public const string BudgetExtend = "limit.budget_extend";
        public const string CompressKeep = "compress.keep";
        public const string CompressRetry = "compress.retry";

        /// <summary>收起来的「网页」页闲置多少分钟后销毁 WebView2 实例（分钟）。</summary>
        public const string BrowserIdle = "limit.browser_idle";

        /// <summary>工具闪现区节拍（毫秒）：没有正文、也没有工具行时，思维链尾部半行多久刷一次。</summary>
        public const string FlashInterval = "limit.flash_interval";

        public const int DefaultMentionTurns = 6;
        public const int DefaultBrowserIdleMinutes = 30;
        public const int DefaultFlashInterval = 500;

        /// <summary>中途快照的节流间隔（秒）：整轮跑很久时按它落一次盘，崩溃最多丢这一段进度。</summary>
        public const string CheckpointInterval = "limit.checkpoint_interval";

        public const int DefaultCheckpointSeconds = 60;

        /// <summary>一轮 WebFetch 的抓取次数上限：查外部资料密集的任务（库源码、文档比对）8 次很快用满。</summary>
        public const string WebFetches = "limit.web_fetches";

        public const int DefaultWebFetches = 8;

        public static IReadOnlyList<FeatureRecord> SeedFeatures() =>
        [
            new FeatureRecord
            {
                Id = RepeatCall, Kind = "guard", Source = "user", CreatedAt = Now(),
                Problem = "模型连续用完全相同的参数调用同一工具，步数被白白烧掉。",
                Payload = J(new { enabled = true, max = 2 })
            },
            new FeatureRecord
            {
                Id = Exploration, Kind = "guard", Source = "user", CreatedAt = Now(),
                Problem = "模型连续只读侦察、迟迟不落笔，任务停在原地。",
                Payload = J(new { enabled = true, max = 30 })
            },
            new FeatureRecord
            {
                Id = Claim, Kind = "guard", Source = "user", CreatedAt = Now(),
                Problem = "模型没调用任何工具就声称已写入/已修改，结果不可信。",
                Payload = J(new { enabled = true })
            },
            new FeatureRecord
            {
                Id = Promise, Kind = "guard", Source = "user", CreatedAt = Now(),
                Problem = "模型只回「请稍候」这类占位话术，不真正执行。",
                Payload = J(new { enabled = true, max = 2 })
            },
            new FeatureRecord
            {
                Id = ArgRepair, Kind = "guard", Source = "user", CreatedAt = Now(),
                Problem = "模型把参数类型写错导致工具失败，本可自愈的一次错误却中断任务。",
                Payload = J(new { enabled = true })
            },
            new FeatureRecord
            {
                Id = MalformedRetry, Kind = "guard", Source = "user", CreatedAt = Now(),
                Problem = "模型输出半截 JSON，工具调用解析失败，整轮直接作废。",
                Payload = J(new { enabled = true, max = 2 })
            },
            new FeatureRecord
            {
                Id = ConnectRetry, Kind = "guard", Source = "user", CreatedAt = Now(),
                Problem = "连接层失败（拒绝连接、DNS、重置）直接判本轮失败，网络恢复后任务已经从零开始。",
                Payload = J(new { enabled = true, @base = 5, max = 300 })
            },
            new FeatureRecord
            {
                Id = FreeLimit, Kind = "guard", Source = "user", CreatedAt = Now(),
                Problem = "免费模型额度耗尽后会话停在错误上，用户得自己换节点再喊「继续」。",
                Payload = J(new { enabled = true })
            },
            new FeatureRecord
            {
                Id = Degenerate, Kind = "guard", Source = "user", CreatedAt = Now(),
                Problem = "模型吐词沙拉/复读时整轮作废，几十步进度全丢；无限重试又会一直烧钱。",
                Payload = J(new { enabled = true, max = 2 })
            },
            new FeatureRecord
            {
                Id = MaxSteps, Kind = "limit", Source = "user", CreatedAt = Now(),
                Problem = "任务无进展时限不住步数，成本失控。",
                Payload = J(new { value = 0 })
            },
            new FeatureRecord
            {
                Id = BudgetExtend, Kind = "limit", Source = "user", CreatedAt = Now(),
                Problem = "步数到顶就收尾，还在推进的长任务被砍断；无限续跑又会耗尽成本。",
                Payload = J(new { steps = 10, max = 3 })
            },
            new FeatureRecord
            {
                Id = ToolResultLimit, Kind = "limit", Source = "user", CreatedAt = Now(),
                Problem = "单个工具结果过长，挤占上下文、推高成本。",
                Payload = J(new { value = Tools.ToolText.ModelBytes })
            },
            new FeatureRecord
            {
                Id = PuppetResultLimit, Kind = "limit", Source = "user", CreatedAt = Now(),
                Problem = "Puppet 单次响应过长（会话板、日志、截图 base64），挤占上下文。",
                Payload = J(new { value = Tools.ToolText.ModelBytes })
            },
            new FeatureRecord
            {
                Id = OutputTokenRatio, Kind = "limit", Source = "user", CreatedAt = Now(),
                Problem = "模型单步思考一路发散、把几十万 token 的思考吐进上下文，成本与界面一起失控。",
                Payload = J(new { value = 8 })
            },
            new FeatureRecord
            {
                Id = CompressAt, Kind = "limit", Source = "user", CreatedAt = Now(),
                Problem = "上下文超过窗口后请求直接失败。",
                Payload = J(new { value = 0.8 })
            },
            new FeatureRecord
            {
                Id = CompressKeep, Kind = "limit", Source = "user", CreatedAt = Now(),
                Problem = "压缩保留太多等于没压；保留太少，模型忘了刚做过什么。",
                Payload = J(new { messages = AgentContext.KeepRecentMessages, tokens = AgentContext.KeepRecentTokens })
            },
            new FeatureRecord
            {
                Id = CompressRetry, Kind = "limit", Source = "user", CreatedAt = Now(),
                Problem = "服务端上下文上限比配置低时，压缩一次不够就整轮失败。",
                Payload = J(new { max = 2 })
            },
            new FeatureRecord
            {
                Id = Poll, Kind = "limit", Source = "user", CreatedAt = Now(),
                Problem = "声明轮询等结果时次数没有上限，一次任务把步数全耗在等待上。",
                Payload = J(new { max = 10, total = 20 })
            },
            new FeatureRecord
            {
                Id = MentionTurns, Kind = "limit", Source = "user", CreatedAt = Now(),
                Problem = "@ 引用另一个会话时附带的轮数：少了看不懂，多了挤占本轮上下文。",
                Payload = J(new { value = DefaultMentionTurns })
            },
            new FeatureRecord
            {
                Id = BrowserIdle, Kind = "limit", Source = "user", CreatedAt = Now(),
                Problem = "「网页」页关掉后 WebView2 实例一直占着内存与浏览器进程，闲置久了也不回收。",
                Payload = J(new { value = DefaultBrowserIdleMinutes })
            },
            new FeatureRecord
            {
                Id = FlashInterval, Kind = "limit", Source = "user", CreatedAt = Now(),
                Problem = "工具闪现区的节拍：太快只是白刷界面，太慢用户就以为卡住了。",
                Payload = J(new { value = DefaultFlashInterval })
            },
            new FeatureRecord
            {
                Id = CheckpointInterval, Kind = "limit", Source = "user", CreatedAt = Now(),
                Problem = "一轮跑十几二十分钟却只在收尾落盘：进程崩了就整段丢失。快照太频繁又白写几十 MB 的对话文件。",
                Payload = J(new { value = DefaultCheckpointSeconds })
            },
            new FeatureRecord
            {
                Id = WebFetches, Kind = "limit", Source = "user", CreatedAt = Now(),
                Problem = "查外部资料密集的任务（查库源码、比对文档）一轮 8 次很快用满，被迫中断改道；写死在源码里也没法按工作区配置与演化。",
                Payload = J(new { value = DefaultWebFetches })
            },
            new FeatureRecord
            {
                Id = "rule.read_before_write", Kind = "rule", Source = "user", CreatedAt = Now(),
                Problem = "不先读现场就写，覆盖掉不知道的内容。",
                Payload = JsonSerializer.SerializeToElement("写文件前先用 ListDir / ReadFile 看清现场；不确定是否存在或内容时，先读再改。")
            },
            new FeatureRecord
            {
                Id = "rule.answer_first", Kind = "rule", Source = "user", CreatedAt = Now(),
                Problem = "回答绕圈子，用户要翻过三段铺垫才看到结论。",
                Payload = JsonSerializer.SerializeToElement("最终回答先给结论或结果，再给依据与后续建议。")
            },
            new FeatureRecord
            {
                Id = "rule.ask_when_unsure", Kind = "rule", Source = "user", CreatedAt = Now(),
                Problem = "参数拿不准时硬猜，产生删除或覆盖这类破坏性操作。",
                Payload = JsonSerializer.SerializeToElement("可能删除或覆盖内容、且参数拿不准时，先问一句再动手。")
            }
        ];

        public static ActivationRecord SeedActivation() => new()
        {
            Version = 1,
            At = Now(),
            By = "user",
            Note = "v0 默认集",
            Features =
            [
                Ref(RepeatCall, ("enabled", true), ("max", 2)),
                Ref(Exploration, ("enabled", true), ("max", 30)),
                Ref(Claim, ("enabled", true)),
                Ref(Promise, ("enabled", true), ("max", 2)),
                Ref(ArgRepair, ("enabled", true)),
                Ref(MalformedRetry, ("enabled", true), ("max", 2)),
                Ref(ConnectRetry, ("enabled", true), ("base", 5), ("max", 300)),
                Ref(FreeLimit, ("enabled", true)),
                Ref(Degenerate, ("enabled", true), ("max", 2)),
                Ref(MaxSteps, ("value", 0)),
                Ref(BudgetExtend, ("steps", 10), ("max", 3)),
                Ref(ToolResultLimit, ("value", Tools.ToolText.ModelBytes)),
                Ref(PuppetResultLimit, ("value", Tools.ToolText.ModelBytes)),
                Ref(OutputTokenRatio, ("value", 8)),
                Ref(CompressAt, ("value", 0.8)),
                Ref(CompressKeep, ("messages", AgentContext.KeepRecentMessages), ("tokens", AgentContext.KeepRecentTokens)),
                Ref(CompressRetry, ("max", 2)),
                Ref(Poll, ("max", 10), ("total", 20)),
                Ref(MentionTurns, ("value", DefaultMentionTurns)),
                Ref(BrowserIdle, ("value", DefaultBrowserIdleMinutes)),
                Ref(FlashInterval, ("value", DefaultFlashInterval)),
                Ref(CheckpointInterval, ("value", DefaultCheckpointSeconds)),
                Ref(WebFetches, ("value", DefaultWebFetches)),
                Ref("rule.read_before_write"),
                Ref("rule.answer_first"),
                Ref("rule.ask_when_unsure")
            ]
        };

        public static AgentPolicy PolicyFrom(
            ActivationRecord activation, IReadOnlyDictionary<string, FeatureRecord> features)
        {
            var policy = new AgentPolicy
            {
                RepeatCallGuard = false,
                ExplorationGuard = false,
                ClaimGuard = false,
                PromiseGuard = false,
                ArgRepair = false,
                MalformedRetry = false
            };
            if (activation == null) return policy;

            foreach (var reference in activation.Features)
            {
                if (reference?.Id == null || !features.TryGetValue(reference.Id, out var feature)) continue;
                switch (reference.Id)
                {
                    case RepeatCall:
                        policy.RepeatCallGuard = Flag(reference, feature, "enabled");
                        policy.MaxRepeatCalls = Number(reference, feature, "max", policy.MaxRepeatCalls);
                        break;
                    case Exploration:
                        policy.ExplorationGuard = Flag(reference, feature, "enabled");
                        policy.MaxExplorationSteps = Number(reference, feature, "max", policy.MaxExplorationSteps);
                        break;
                    case Claim:
                        policy.ClaimGuard = Flag(reference, feature, "enabled");
                        break;
                    case Promise:
                        policy.PromiseGuard = Flag(reference, feature, "enabled");
                        policy.MaxPromiseNudges = Number(reference, feature, "max", policy.MaxPromiseNudges);
                        break;
                    case ArgRepair:
                        policy.ArgRepair = Flag(reference, feature, "enabled");
                        break;
                    case MalformedRetry:
                        policy.MalformedRetry = Flag(reference, feature, "enabled");
                        policy.MaxMalformedRetries = Number(reference, feature, "max", policy.MaxMalformedRetries);
                        break;
                    case ConnectRetry:
                        policy.ConnectRetry = Flag(reference, feature, "enabled");
                        policy.ConnectRetryBaseSeconds = Number(reference, feature, "base", policy.ConnectRetryBaseSeconds);
                        policy.ConnectRetryMaxSeconds = Number(reference, feature, "max", policy.ConnectRetryMaxSeconds);
                        break;
                    case FreeLimit:
                        policy.FreeLimitRecovery = Flag(reference, feature, "enabled");
                        break;
                    case Degenerate:
                        policy.MaxDegenerateRetries = Flag(reference, feature, "enabled")
                            ? Number(reference, feature, "max", policy.MaxDegenerateRetries)
                            : 0;
                        break;
                    case MaxSteps:
                        policy.MaxSteps = Number(reference, feature, "value", policy.MaxSteps);
                        break;
                    case ToolResultLimit:
                        policy.ToolResultLimit = Number(reference, feature, "value", policy.ToolResultLimit);
                        break;
                    case PuppetResultLimit:
                        policy.PuppetResultLimit = Number(reference, feature, "value", policy.PuppetResultLimit);
                        break;
                    case OutputTokenRatio:
                        policy.OutputTokenRatio = Real(reference, feature, "value", policy.OutputTokenRatio);
                        break;
                    case CompressAt:
                        policy.CompressAt = Real(reference, feature, "value", policy.CompressAt);
                        break;
                    case CompressKeep:
                        policy.CompressKeepMessages = Number(reference, feature, "messages", policy.CompressKeepMessages);
                        policy.CompressKeepTokens = Number(reference, feature, "tokens", policy.CompressKeepTokens);
                        break;
                    case CompressRetry:
                        policy.CompressRetries = Number(reference, feature, "max", policy.CompressRetries);
                        break;
                    case Poll:
                        policy.MaxPollRepeats = Number(reference, feature, "max", policy.MaxPollRepeats);
                        policy.MaxPollTotal = Number(reference, feature, "total", policy.MaxPollTotal);
                        break;
                    case BudgetExtend:
                        policy.BudgetExtendSteps = Number(reference, feature, "steps", policy.BudgetExtendSteps);
                        policy.MaxBudgetExtensions = Number(reference, feature, "max", policy.MaxBudgetExtensions);
                        break;
                }
            }
            return policy;
        }

        /// <summary>按工作区账本取运行时策略；账本缺失或读失败用默认（只读，不建目录）。</summary>
        public static AgentPolicy PolicyOf(string workspace)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return new AgentPolicy();
            try
            {
                var ledger = Ledger.Open(workspace);
                return PolicyFrom(ledger.Current,
                    ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal));
            }
            catch
            {
                return new AgentPolicy();
            }
        }

        /// <summary>一轮 WebFetch 的抓取上限：读工作区账本 limit.web_fetches，读不到用默认（只读，不建目录）。</summary>
        public static int WebFetchesOf(string workspace)
        {
            int fallback = DefaultWebFetches;
            if (string.IsNullOrWhiteSpace(workspace)) return fallback;
            try
            {
                var ledger = Ledger.Open(workspace);
                return Math.Clamp(ValueOf(ledger.Current,
                    ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal),
                    WebFetches, fallback), 1, 100);
            }
            catch
            {
                return fallback;
            }
        }

        /// <summary>读激活集里某个 limit 特征的 value；读不到用 fallback（壳侧数值也走账本演进）。</summary>
        public static int ValueOf(
            ActivationRecord activation, IReadOnlyDictionary<string, FeatureRecord> features,
            string id, int fallback)
        {
            if (activation == null) return fallback;
            var reference = activation.Features.FirstOrDefault(r => r?.Id == id);
            if (reference?.Id == null || !features.TryGetValue(id, out var feature)) return fallback;
            return Number(reference, feature, "value", fallback);
        }

        /// <summary>按激活顺序取出规则文本（顺序即提示词顺序）。</summary>
        public static IReadOnlyList<string> RulesFrom(
            ActivationRecord activation, IReadOnlyDictionary<string, FeatureRecord> features)
        {
            var rules = new List<string>();
            if (activation == null) return rules;

            foreach (var reference in activation.Features)
            {
                if (reference?.Id == null || !features.TryGetValue(reference.Id, out var feature)) continue;
                if (feature.Kind != "rule" || feature.Payload.ValueKind != JsonValueKind.String) continue;
                string text = feature.Payload.GetString();
                if (!string.IsNullOrWhiteSpace(text)) rules.Add(text.Trim());
            }
            return rules;
        }

        private static FeatureRef Ref(string id, params (string Name, object Value)[] parameters) => new()
        {
            Id = id,
            Params = parameters.Length == 0
                ? null
                : parameters.ToDictionary(p => p.Name, p => J(p.Value))
        };

        private static JsonElement J(object value) => value switch
        {
            bool b => JsonSerializer.SerializeToElement(b),
            int i => JsonSerializer.SerializeToElement(i),
            double d => JsonSerializer.SerializeToElement(d),
            string s => JsonSerializer.SerializeToElement(s),
            _ => JsonSerializer.SerializeToElement(value)
        };

        private static bool Flag(FeatureRef reference, FeatureRecord feature, string name)
        {
            if (reference.Params != null && reference.Params.TryGetValue(name, out var own)
                && own.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return own.GetBoolean();
            return feature.Payload.ValueKind == JsonValueKind.Object
                && feature.Payload.TryGetProperty(name, out var fallback)
                && fallback.ValueKind is JsonValueKind.True or JsonValueKind.False
                && fallback.GetBoolean();
        }

        private static int Number(FeatureRef reference, FeatureRecord feature, string name, int fallback)
        {
            if (reference.Params != null && reference.Params.TryGetValue(name, out var own)
                && own.ValueKind == JsonValueKind.Number && own.TryGetInt32(out int value))
                return value;
            if (feature.Payload.ValueKind == JsonValueKind.Object
                && feature.Payload.TryGetProperty(name, out var def)
                && def.ValueKind == JsonValueKind.Number && def.TryGetInt32(out int fallbackValue))
                return fallbackValue;
            return fallback;
        }

        private static double Real(FeatureRef reference, FeatureRecord feature, string name, double fallback)
        {
            if (reference.Params != null && reference.Params.TryGetValue(name, out var own)
                && own.ValueKind == JsonValueKind.Number && own.TryGetDouble(out double value))
                return value;
            if (feature.Payload.ValueKind == JsonValueKind.Object
                && feature.Payload.TryGetProperty(name, out var def)
                && def.ValueKind == JsonValueKind.Number && def.TryGetDouble(out double fallbackValue))
                return fallbackValue;
            return fallback;
        }

        private static string Now() => DateTimeOffset.UtcNow.ToString("O");
    }
}
