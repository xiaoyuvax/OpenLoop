using OpenLoop.Service.Shell;
using OpenLoop.Service.Source;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>迭代编排里不碰进程的部分：回退对象判定、报告文本、重启脚本。</summary>
    public class IterationRunnerTests
    {
        [Fact]
        public void RolesToRollback_returns_swapped_roles_with_existing_dirs_i_first()
        {
            string root = Temp();
            try
            {
                var store = new VersionStore(root);
                string oldA = Dir(root, "a-old");
                string oldI = Dir(root, "i-old");
                string newA = Dir(root, "a-new");
                string newI = Dir(root, "i-new");
                store.Activate("a", new VersionEntry { Dir = newA, Version = "0.2.0" });
                store.Activate("i", new VersionEntry { Dir = newI, Version = "0.2.0" });

                var before = new Dictionary<string, VersionEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    ["a"] = new() { Dir = oldA, Version = "0.1.0" },
                    ["i"] = new() { Dir = oldI, Version = "0.1.0" }
                };

                Assert.Equal(["i", "a"], IterationRunner.RolesToRollback(before, store));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void RolesToRollback_skips_unchanged_and_missing_dirs()
        {
            string root = Temp();
            try
            {
                var store = new VersionStore(root);
                string same = Dir(root, "a-same");
                store.Activate("a", new VersionEntry { Dir = same, Version = "0.1.0" });

                var before = new Dictionary<string, VersionEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    ["a"] = new() { Dir = same, Version = "0.1.0" },
                    ["i"] = new() { Dir = Path.Combine(root, "gone"), Version = "0.1.0" }
                };

                Assert.Empty(IterationRunner.RolesToRollback(before, store));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void ReportText_lists_result_steps_and_rollback()
        {
            var report = new IterationReport
            {
                Version = "0.2.0",
                At = "2026-11-06T00:00:00+08:00",
                Ok = false,
                RolledBack = true,
                Steps = [new Iteration.Step("build", true, "ok"), new Iteration.Step("restart a", false, "timeout")]
            };

            string text = IterationRunner.ReportText(report);

            Assert.Contains("结果：失败", text);
            Assert.Contains("已回退到迭代前的版本", text);
            Assert.Contains("OK build ok", text);
            Assert.Contains("FAIL restart a timeout", text);
        }

        [Fact]
        public void ShellUpdate_copy_backs_up_overwritten_files_and_keeps_extra_ones()
        {
            string root = Temp();
            try
            {
                string source = Dir(root, "staging");
                string install = Dir(root, "install");
                string backup = Path.Combine(root, "backup");
                File.WriteAllText(Path.Combine(source, "OpenLoop.exe"), "NEW");
                File.WriteAllText(Path.Combine(source, "new.txt"), "ADDED");
                Directory.CreateDirectory(Path.Combine(install, "backends"));
                File.WriteAllText(Path.Combine(install, "OpenLoop.exe"), "OLD");
                File.WriteAllText(Path.Combine(install, "backends", "own.txt"), "KEEP");

                ShellUpdate.Copy(source, install, backup);

                Assert.Equal("NEW", File.ReadAllText(Path.Combine(install, "OpenLoop.exe")));
                Assert.Equal("OLD", File.ReadAllText(Path.Combine(backup, "OpenLoop.exe")));
                Assert.Equal("ADDED", File.ReadAllText(Path.Combine(install, "new.txt")));
                Assert.Equal("KEEP", File.ReadAllText(Path.Combine(install, "backends", "own.txt")));   // 不镜像

                ShellUpdate.Restore(backup, install);
                Assert.Equal("OLD", File.ReadAllText(Path.Combine(install, "OpenLoop.exe")));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Decide_cold_start_trusts_the_binaries_that_report_the_declared_version()
        {
            var harness = new PlanHarness();                 // 没有基准 = 冷启动
            harness.Binary["a"] = "0.1.0";
            harness.Binary["i"] = "0.1.0";
            harness.Binary["shell"] = "0.1.0";

            var plans = harness.Plan();

            // 源码号 = 二进制号：不用重编，但要把基准补回去，下一轮才有对照
            Assert.All(plans, p => Assert.False(p.Update));
            Assert.Empty(harness.Written);
            Assert.All(plans, p => Assert.Equal(harness.Declared[p.Role], harness.Pending[p.Role].Version));
        }

        [Fact]
        public void Decide_cold_start_bumps_a_node_whose_binary_is_at_another_version()
        {
            var harness = new PlanHarness();
            harness.Binary["a"] = "0.0.9";
            harness.Binary["i"] = "0.1.0";
            harness.Binary["shell"] = "0.1.0";

            var plans = harness.Plan();

            Assert.Equal("0.1.1", plans.Single(p => p.Role == "a").Target);
            Assert.False(plans.Single(p => p.Role == "i").Update);
            Assert.False(plans.Single(p => p.Role == "shell").Update);
            Assert.Equal("0.1.1", harness.Declared["a"]);
        }

        [Fact]
        public void Decide_does_nothing_when_fingerprints_match_the_last_publish()
        {
            var harness = new PlanHarness();
            harness.Plan();
            harness.Promote();

            var plans = harness.Plan();

            Assert.Equal(harness.State["core"].Fingerprint, harness.Pending["core"].Fingerprint);
            Assert.Equal(harness.State["core"].Version, harness.Declared["core"]);
            Assert.All(plans, p => Assert.False(p.Update));
            Assert.Empty(harness.Written);
        }

        [Fact]
        public void Decide_cascades_a_core_change_to_every_dependent()
        {
            var harness = new PlanHarness();
            harness.Plan();
            harness.Promote();

            harness.Content["core"] = "core-2";
            var plans = harness.Plan();

            Assert.True(plans.Single(p => p.Role == "core").Update);
            Assert.True(plans.Single(p => p.Role == "service").Update);
            Assert.True(plans.Single(p => p.Role == "a").Update);
            Assert.True(plans.Single(p => p.Role == "i").Update);
            Assert.True(plans.Single(p => p.Role == "shell").Update);
            Assert.False(plans.Single(p => p.Role == "chatflow").Update);
        }

        [Fact]
        public void Decide_keeps_leaf_changes_local()
        {
            var harness = new PlanHarness();
            harness.Plan();
            harness.Promote();

            harness.Content["a"] = "a-2";
            var plans = harness.Plan();

            Assert.True(plans.Single(p => p.Role == "a").Update);
            Assert.False(plans.Single(p => p.Role == "service").Update);
            Assert.False(plans.Single(p => p.Role == "i").Update);
            Assert.False(plans.Single(p => p.Role == "shell").Update);
        }

        [Fact]
        public void Decide_respects_a_manual_bump_and_cascades_it()
        {
            var harness = new PlanHarness();
            harness.Plan();
            harness.Promote();

            harness.Declared["core"] = "0.5.0";   // 人工改号，内容没变
            var plans = harness.Plan();

            Assert.Equal("0.5.0", plans.Single(p => p.Role == "core").Target);
            Assert.True(plans.Single(p => p.Role == "service").Update);
        }

        [Fact]
        public void Decide_retries_the_same_number_when_the_last_run_failed()
        {
            var harness = new PlanHarness();
            harness.Plan();
            harness.Promote();

            harness.Content["core"] = "core-2";
            var first = harness.Plan();           // 没 Promote：模拟构建失败

            harness.Content["core"] = "core-3";
            var second = harness.Plan();

            Assert.Equal("0.1.1", first.Single(p => p.Role == "core").Target);
            Assert.Equal("0.1.1", second.Single(p => p.Role == "core").Target);
        }

        [Fact]
        public void Decide_retries_the_planned_number_when_no_baseline_was_promoted()
        {
            var harness = new PlanHarness();
            harness.Binary["a"] = "0.0.9";         // 二进制落后，冷启动就会升它

            var first = harness.Plan();           // 0.1.1，但没 Promote
            var second = harness.Plan();          // 同一份内容：不往上再跳一版

            Assert.Equal("0.1.1", first.Single(p => p.Role == "a").Target);
            Assert.Equal("0.1.1", second.Single(p => p.Role == "a").Target);
            Assert.True(second.Single(p => p.Role == "a").Update);
        }

        /// <summary>把 Decide 接到内存里的节点图上：不碰磁盘，但保留「用上游本轮目标版本算指纹」的语义。</summary>
        private sealed class PlanHarness
        {
            public readonly Dictionary<string, string> Content = new(StringComparer.OrdinalIgnoreCase)
            {
                ["core"] = "core-1", ["service"] = "service-1", ["chatflow"] = "chatflow-1",
                ["a"] = "a-1", ["i"] = "i-1", ["shell"] = "shell-1"
            };

            public readonly Dictionary<string, string> Declared = new(StringComparer.OrdinalIgnoreCase)
            {
                ["core"] = "0.1.0", ["service"] = "0.1.0", ["chatflow"] = "0.1.0",
                ["a"] = "0.1.0", ["i"] = "0.1.0", ["shell"] = "0.1.0"
            };

            public readonly Dictionary<string, string> Written = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, NodeState> Pending = new(StringComparer.OrdinalIgnoreCase);
            /// <summary>运行中的二进制自己报的版本；没有条目的节点（库/外部组件）当作读不到。</summary>
            public readonly Dictionary<string, string> Binary = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, NodeState> State;

            private static VersionNode[] Graph() =>
            [
                new("core", "Core", "", "src/OpenLoop.Core", "", []),
                new("service", "Service", "", "src/OpenLoop.Service", "", ["core"]),
                new("chatflow", "ChatFlow", "", "ChatFlow", "", []),
                new("a", "Worker（A）", "", "src/OpenLoop.A", "", ["service"]),
                new("i", "Loop（I）", "", "src/OpenLoop.I", "", ["service"]),
                new("shell", "壳", "", "src/OpenLoop.App", "", ["service", "chatflow"])
            ];

            public List<RolePlan> Plan()
            {
                var planned = new Dictionary<string, NodeState>(Pending, StringComparer.OrdinalIgnoreCase);
                Pending.Clear();
                Written.Clear();
                return IterationRunner.Decide(
                    Graph(),
                    State,
                    planned,
                    node => Declared[node.Id],
                    (node, declared) => State != null && State.TryGetValue(node.Id, out var state)
                        ? state.Version
                        : declared,
                    (node, fingerprints, versions) =>
                    {
                        var deps = node.Upstream.Select(up => new NodeDep(
                            up, "project",
                            versions.TryGetValue(up, out var version) ? version : "",
                            fingerprints.TryGetValue(up, out var print) ? print : "")).ToList();
                        return new NodeFingerprint(VersionScan.Combine(Content[node.Id], "", [], deps), deps);
                    },
                    (node, version) =>
                    {
                        Declared[node.Id] = version;
                        Written[node.Id] = version;
                    },
                    (node, state) => Pending[node.Id] = state,
                    node => Binary.TryGetValue(node.Id, out var binary) ? binary : null);
            }

            public void Promote() => State = new Dictionary<string, NodeState>(Pending, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 两套发布参数都是单文件，差别只在「自带框架」：只有它开压缩
        /// （框架依赖 + 压缩会被 SDK 拒绝，NETSDK1176）；RID 必须显式给（NETSDK1191）。
        /// </summary>
        [Fact]
        public void Unattended_runs_refuse_changes_outside_the_version_files()
        {
            // 计划每轮都会改写版本文件，这几个算正常
            Assert.Empty(IterationRunner.UnexpectedChanges(
            [
                " M ShellVersion.props",
                " M src/OpenLoop.A/OpenLoop.A.csproj",
                " M src/OpenLoop.Service/OpenLoop.Service.csproj"
            ]));

            // 别人没写完的代码不能顺手发出去
            var offending = IterationRunner.UnexpectedChanges(
            [
                " M src/OpenLoop.Core/Tools/Builtin/WebFetchTool.cs",
                "?? tests/OpenLoop.Core.Tests/WebFetchToolTests.cs",
                " M src/OpenLoop.A/OpenLoop.A.csproj"
            ]);

            Assert.Equal(2, offending.Count);
            Assert.Contains(offending, p => p.EndsWith("WebFetchTool.cs", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Publish_args_follow_the_publish_mode()
        {
            string[] framework = Iteration.PublishArgs(null, "out");
            string[] bundled = Iteration.PublishArgs(Iteration.SelfContainedMode, "out");

            foreach (string[] args in new[] { framework, bundled })
            {
                Assert.Contains("-p:PublishSingleFile=true", args);
                Assert.Contains("-p:ValidateExecutableReferencesMatchSelfContained=false", args);
                Assert.Equal(Iteration.RuntimeIdentifier, ArgAfter(args, "-r"));
            }

            Assert.Equal("false", ArgAfter(framework, "--self-contained"));
            Assert.DoesNotContain("-p:EnableCompressionInSingleFile=true", framework);

            Assert.Equal("true", ArgAfter(bundled, "--self-contained"));
            Assert.Contains("-p:EnableCompressionInSingleFile=true", bundled);
            // RID 必须是可移植形态（win-x64 / linux-arm64…）：空串或带路径的都会让 publish 直接失败
            Assert.Matches("^[a-z]+(-[a-z0-9]+)+$", Iteration.RuntimeIdentifier);
        }

        private static string ArgAfter(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static string Temp()
        {
            string root = Path.Combine(Path.GetTempPath(), "ol-iter-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static string Dir(string root, string name)
        {
            string dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
