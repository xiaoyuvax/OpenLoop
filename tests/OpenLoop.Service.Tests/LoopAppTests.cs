using OpenLoop.Core.Tools;
using OpenLoop.Service.OpenUI;
using OpenLoop.Service.Worker;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>
    /// LoopApp 的落盘与边界：名字归一、create 不覆盖已写的代码、Worker 只能写 .openloop/tmp。
    /// 这几条是「agent 反复迭代同一个应用」与「会话边界」的基础，改坏了很隐蔽。
    /// </summary>
    public sealed class LoopAppTests : IDisposable
    {
        private readonly string _workspace = Path.Combine(Path.GetTempPath(), "openui-ws-" + Guid.NewGuid().ToString("N"));
        private readonly string _skills = Path.Combine(Path.GetTempPath(), "openui-skills-" + Guid.NewGuid().ToString("N"));
        private readonly string _skill;

        public LoopAppTests()
        {
            Directory.CreateDirectory(_workspace);
            // 技能库布局：<root>/openui/{SKILL.md,references/runtime}——FindSkill 只认这个形状
            _skill = Path.Combine(_skills, "openui");
            Directory.CreateDirectory(Path.Combine(_skill, "references", "runtime"));
            File.WriteAllText(Path.Combine(_skill, "SKILL.md"), "# openui");
            File.WriteAllText(Path.Combine(_skill, "references", "runtime", "openui.js"), "// runtime");
            File.WriteAllText(Path.Combine(_skill, "references", "runtime", "openui.css"), "/* runtime */");
        }

        public void Dispose()
        {
            try { Directory.Delete(_workspace, true); } catch { }
            try { Directory.Delete(_skills, true); } catch { }
        }

        [Theory]
        [InlineData("订单看板", "订单看板")]
        [InlineData("sales board", "sales-board")]
        [InlineData("a/b\\c:d", "abcd")]
        [InlineData("   ", "app")]
        [InlineData("", "app")]
        public void Slug_keeps_letters_and_chinese_drops_the_rest(string input, string expect)
            => Assert.Equal(expect, LoopAppPaths.Slug(input));

        [Fact]
        public void Create_copies_the_runtime_and_writes_a_skeleton()
        {
            string dir = LoopAppPaths.Create(_workspace, "demo", _skill, "演示", out string error);

            Assert.Null(error);
            Assert.NotNull(dir);
            Assert.True(File.Exists(Path.Combine(dir, "index.html")));
            Assert.True(File.Exists(Path.Combine(dir, "app.js")));
            Assert.True(File.Exists(Path.Combine(dir, "runtime", "openui.js")));
            // 应用自包含：运行时跟着应用走，能整目录搬走
            Assert.Contains("runtime/openui.js", File.ReadAllText(Path.Combine(dir, "index.html")));
        }

        [Fact]
        public void Create_never_overwrites_the_agent_own_code()
        {
            string dir = LoopAppPaths.Create(_workspace, "demo", _skill, "演示", out _);
            string app = Path.Combine(dir, "app.js");
            File.WriteAllText(app, "// agent 写的界面");

            LoopAppPaths.Create(_workspace, "demo", _skill, "演示", out _);

            // 再 create 一次只补缺的文件：迭代期间不能把 agent 刚写的界面打回骨架
            Assert.Equal("// agent 写的界面", File.ReadAllText(app));
        }

        [Fact]
        public void List_reads_the_title_from_app_json()
        {
            string dir = LoopAppPaths.Create(_workspace, "demo", _skill, "演示标题", out _);
            File.WriteAllText(Path.Combine(dir, "app.json"), "{\"title\":\"改过的标题\"}");

            var app = Assert.Single(LoopAppPaths.List(_workspace));
            Assert.Equal("demo", app.Slug);
            Assert.Equal("改过的标题", app.Title);
        }

        [Fact]
        public void List_does_not_create_the_directory()
        {
            // 读写分离：没建过应用时 list 不能顺手把目录建出来
            Assert.Empty(LoopAppPaths.List(_workspace));
            Assert.False(Directory.Exists(LoopAppPaths.Root(_workspace)));
        }

        [Fact]
        public void Worker_scope_allows_loopapps_but_still_hides_the_rest_of_openloop()
        {
            var scope = new WorkspaceScope(_workspace, allowed: WorkerScopeRules.Allowed);

            Assert.True(scope.TryResolve(".openloop/tmp/LoopApps/demo/app.js", out _, out _));
            Assert.False(scope.TryResolve(".openloop/loop/findings.jsonl", out _, out _));
            Assert.False(scope.TryResolve(".openloop/eval/tasks/x.json", out _, out _));
        }

        [Fact]
        public void FindSkill_requires_both_the_skill_file_and_the_runtime()
        {
            Assert.Equal(Path.GetFullPath(_skill), LoopAppPaths.FindSkill(_workspace, _skills));

            // 只有 SKILL.md、没有 references/runtime：create 会失败，不该被当成可用技能
            string bareRoot = Path.Combine(Path.GetTempPath(), "openui-bare-" + Guid.NewGuid().ToString("N"));
            string bare = Path.Combine(bareRoot, "openui");
            Directory.CreateDirectory(bare);
            try
            {
                File.WriteAllText(Path.Combine(bare, "SKILL.md"), "# openui");
                Assert.Null(LoopAppPaths.FindSkill(_workspace, bareRoot));
            }
            finally
            {
                try { Directory.Delete(bareRoot, true); } catch { }
            }
        }

        [Fact]
        public void Tool_is_only_exposed_when_the_skill_is_installed()
        {
            // 没装技能时连工具名都不该出现，免得模型对着必然失败的工具反复试
            Assert.Empty(WorkerScopeRules.LoopApp(_workspace, "sess0001", Path.Combine(_skills, "missing")));
        }
    }
}
