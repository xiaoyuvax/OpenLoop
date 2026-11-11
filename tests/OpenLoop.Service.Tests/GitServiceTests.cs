using System.Diagnostics;
using OpenLoop.Service.Source;
using Xunit;

namespace OpenLoop.Service.Tests
{
    public class GitServiceTests
    {
        [Fact]
        public void ParsePorcelain_keeps_renames_to_the_new_path_only()
        {
            var files = GitService.ParsePorcelain(" M a.cs\0R  new.cs\0old.cs\0?? b.txt\0");

            Assert.Equal(["a.cs", "new.cs", "b.txt"], files.Select(f => f.Path));
            Assert.Equal("R ", files[1].Status);
        }

        [Fact]
        public void Token_depends_on_the_file_set_not_on_order()
        {
            var one = new List<GitFile> { new() { Status = " M", Path = "a" }, new() { Status = "??", Path = "b" } };
            var two = new List<GitFile> { new() { Status = "??", Path = "b" }, new() { Status = " M", Path = "a" } };
            var changed = new List<GitFile> { new() { Status = " M", Path = "a" } };

            Assert.Equal(GitService.TokenOf(one), GitService.TokenOf(two));
            Assert.NotEqual(GitService.TokenOf(one), GitService.TokenOf(changed));
        }

        [Fact]
        public void Commit_rejects_a_stale_token_and_accepts_the_previewed_one()
        {
            string root = Temp();
            try
            {
                Run(root, "init", "-q");
                Run(root, "config", "user.email", "test@example.com");
                Run(root, "config", "user.name", "test");
                File.WriteAllText(Path.Combine(root, "a.txt"), "1");

                var preview = GitService.ReadRepo("test", root);
                var request = new GitCommitRequest { Message = "test", Tokens = new(StringComparer.OrdinalIgnoreCase) { ["test"] = "stale" } };

                var rejected = GitService.CommitOne("test", root, request);
                Assert.False(rejected.Ok);
                Assert.Contains("重新预览", rejected.Error);

                request.Tokens["test"] = preview.Token;
                var accepted = GitService.CommitOne("test", root, request);
                Assert.True(accepted.Ok, accepted.Error);
                Assert.False(string.IsNullOrWhiteSpace(accepted.Commit));

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

        [Fact]
        public void Commit_refuses_repos_outside_the_whitelist()
        {
            var result = GitService.Commit(new GitCommitRequest
            {
                Repos = ["C:\\Windows"],
                Tokens = new(StringComparer.OrdinalIgnoreCase)
            }, lastRunFailed: false);

            Assert.False(result.Repos.Single().Ok);
            Assert.Contains("白名单", result.Repos.Single().Error);
        }

        private static string Temp()
        {
            string root = Path.Combine(Path.GetTempPath(), "ol-git-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
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
