using System.Diagnostics;
using OpenLoop.Service.Source;
using Xunit;

namespace OpenLoop.Service.Tests
{
    public class GitServiceEncodingTests
    {
        private const string Name = "docs/thread-新会话页缺失只查看免费切换-aabb2b4c-a4f.md";

        /// <summary>
        /// git 输出是 UTF-8：不显式指定编码就按控制台代码页（中文系统 936）解码，
        /// 非 ASCII 路径变成乱码，再拿乱码路径去 add 就报 "pathspec ... did not match any files"。
        /// </summary>
        [Fact]
        public void ReadRepo_and_commit_keep_non_ascii_paths_intact()
        {
            string root = Temp();
            try
            {
                Run(root, "init", "-q");
                Run(root, "config", "user.email", "test@example.com");
                Run(root, "config", "user.name", "test");
                File.WriteAllText(Path.Combine(root, Name), "x");
                Run(root, "add", "-A");
                Run(root, "commit", "-qm", "init");
                File.Delete(Path.Combine(root, Name));

                var preview = GitService.ReadRepo("test", root);
                Assert.Equal(Name, Assert.Single(preview.Files).Path);

                var result = GitService.CommitOne("test", root, new GitCommitRequest
                {
                    Message = "test",
                    Tokens = new(StringComparer.OrdinalIgnoreCase) { ["test"] = preview.Token }
                });

                Assert.True(result.Ok, result.Error);
                Assert.Empty(GitService.ReadRepo("test", root).Files);
            }
            finally
            {
                // git 的对象文件是只读的，直接递归删会被拒绝
                foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                }
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static string Temp()
        {
            string root = Path.Combine(Path.GetTempPath(), "ol-git-enc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            return root;
        }

        private static void Run(string root, params string[] args)
        {
            var info = new ProcessStartInfo("git")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            using var process = Process.Start(info);
            process.WaitForExit(30_000);
        }
    }
}
