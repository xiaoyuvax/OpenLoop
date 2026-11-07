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
        public void RestartScript_backs_up_before_overwrite_and_never_mirrors()
        {
            string script = IterationRunner.RestartScript(
                pid: 4321,
                install: @"C:\apps\OpenLoop",
                exe: @"C:\apps\OpenLoop\OpenLoop.exe",
                args: "--loop \"C:\\work space\" --iteration",
                from: @"C:\data\versions\shell\0.2.0-abcdef12",
                backup: @"C:\data\versions\shell\rollback-1",
                failedMarker: @"C:\data\runtime\iteration-shell-failed.txt",
                startedMarker: @"C:\data\runtime\shell-started.json",
                log: @"C:\data\runtime\iteration-restart.log");

            Assert.Contains("'C:\\apps\\OpenLoop'", script);
            Assert.Contains("--loop \"C:\\work space\" --iteration", script);
            Assert.Contains("Copy-Item", script);
            Assert.Contains("'new shell failed to start'", script);
            Assert.Contains("shell-started.json", script);
            Assert.DoesNotContain("robocopy", script, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/MIR", script, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 真的跑一遍重启脚本：新壳起不来（假 exe 立刻退出）时必须恢复旧文件、写失败标记、再拉起旧壳。
        /// </summary>
        [Fact]
        public void RestartScript_rolls_back_when_new_shell_never_reports()
        {
            if (!OperatingSystem.IsWindows()) return;

            string root = Temp();
            try
            {
                string install = Dir(root, "install");
                string from = Dir(root, "from");
                string backup = Path.Combine(root, "backup");
                string failed = Path.Combine(root, "failed.txt");
                string started = Path.Combine(root, "started.json");
                string log = Path.Combine(root, "restart.log");

                string fake = Path.Combine(Environment.SystemDirectory, "whoami.exe");
                string oldExe = Path.Combine(install, "OpenLoop.exe");
                File.Copy(fake, oldExe);
                byte[] oldBytes = File.ReadAllBytes(oldExe);
                File.WriteAllText(Path.Combine(install, "old.txt"), "OLD");
                File.WriteAllText(Path.Combine(install, "keep.txt"), "KEEP");
                File.Copy(fake, Path.Combine(from, "OpenLoop.exe"));
                File.WriteAllText(Path.Combine(from, "old.txt"), "NEW");

                string script = Path.Combine(root, "restart.ps1");
                File.WriteAllText(script, IterationRunner.RestartScript(
                    999999, install, Path.Combine(install, "OpenLoop.exe"), "--iteration",
                    from, backup, failed, started, log), new System.Text.UTF8Encoding(true));

                var start = new System.Diagnostics.ProcessStartInfo("powershell.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (string arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script })
                    start.ArgumentList.Add(arg);
                using var process = System.Diagnostics.Process.Start(start);
                process.WaitForExit(60_000);

                Assert.True(File.Exists(failed), "应写失败标记");
                Assert.Equal(oldBytes, File.ReadAllBytes(oldExe));
                Assert.Equal("OLD", File.ReadAllText(Path.Combine(install, "old.txt")));
                Assert.Equal("KEEP", File.ReadAllText(Path.Combine(install, "keep.txt")));
                Assert.Contains("restart failed", File.ReadAllText(log));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void RolesFor_maps_changed_paths_to_the_binaries_they_affect()
        {
            Assert.Equal(["a", "i", "shell"], Sorted("src/OpenLoop.Service/Web/WorkerApiClient.cs"));
            Assert.Equal(["a", "i", "shell"], Sorted("src/OpenLoop.Core/Tools/ToolRegistry.cs"));
            Assert.Equal(["shell"], Sorted("src/OpenLoop.App/MainForm.cs", "OpenLoop.ico"));
            Assert.Equal(["a"], Sorted("src/OpenLoop.A/Program.cs"));
            Assert.Equal(["i"], Sorted("src/OpenLoop.I/Program.cs"));
            Assert.Equal(["a", "i", "shell"], Sorted("OpenLoop.slnx"));
        }

        [Fact]
        public void RolesFor_ignores_docs_tests_scripts_and_splits_renames()
        {
            Assert.Empty(Sorted("docs/PLAN-DUAL-PROCESS.md", "README.md", "tests/OpenLoop.Core.Tests/X.cs",
                "scripts/publish-role.ps1", "samples/plugins/dsh-free/plugin.json"));
            Assert.Equal(["a", "i"], Sorted("src/OpenLoop.A/Old.cs -> src/OpenLoop.I/New.cs"));
            Assert.Equal(["a", "i", "shell"], Sorted("unknown-file.xyz"));
        }

        private static string[] Sorted(params string[] paths)
            => IterationRunner.RolesFor(paths).OrderBy(r => r, StringComparer.Ordinal).ToArray();

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
