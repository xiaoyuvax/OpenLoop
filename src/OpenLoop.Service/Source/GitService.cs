using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenLoop.Service.Bus;

namespace OpenLoop.Service.Source
{
    public sealed class GitFile
    {
        public string Status { get; set; }
        public string Path { get; set; }
    }

    public sealed class GitRepoStatus
    {
        public string Id { get; set; }
        public string Path { get; set; }
        public string Branch { get; set; }
        public List<GitFile> Files { get; set; } = [];
        public string DiffStat { get; set; } = "";

        /// <summary>文件清单的哈希；提交时必须原样带回来，防止把别人同时改的东西一起提交。</summary>
        public string Token { get; set; }
        public string Error { get; set; }
    }

    public sealed class GitStatusResult
    {
        public string At { get; set; } = DateTimeOffset.Now.ToString("O");
        public List<GitRepoStatus> Repos { get; set; } = [];
        public string Hint { get; set; } = "";
    }

    public sealed class GitCommitRequest
    {
        public List<string> Repos { get; set; } = [];
        public string Message { get; set; }
        public bool Push { get; set; }

        /// <summary>仓库 id → 预览时拿到的 token。</summary>
        public Dictionary<string, string> Tokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Force { get; set; }
    }

    public sealed class GitRepoResult
    {
        public string Id { get; set; }
        public bool Ok { get; set; }
        public string Commit { get; set; }
        public bool Pushed { get; set; }
        public string Error { get; set; }
    }

    public sealed class GitCommitResult
    {
        public List<GitRepoResult> Repos { get; set; } = [];
    }

    /// <summary>
    /// 人工触发的提交/推送：只认白名单里的仓库，只提交预览里列出的文件（token 校验），
    /// 不 force、不 rebase、不自动 push。
    /// </summary>
    public static class GitService
    {
        /// <summary>白名单：OpenLoop 源码根 + 托管组件仓库；不在表里的一律不认。</summary>
        public static IReadOnlyList<(string Id, string Root)> Repos()
        {
            var all = new (string Id, string Root)[]
            {
                ("openloop", SourceSync.SourceRoot),
                ("puppetcore", SourceSync.PuppetRoot),
                ("chatflow", SourceSync.ChatFlowRoot)
            };
            return all.Where(r => !string.IsNullOrWhiteSpace(r.Root)
                                  && Directory.Exists(Path.Combine(r.Root, ".git"))).ToList();
        }

        public static GitStatusResult Status()
        {
            var result = new GitStatusResult();
            foreach (var (id, root) in Repos())
            {
                var repo = ReadRepo(id, root);
                result.Repos.Add(repo);
            }
            result.Hint = result.Repos.Any(r => r.Files.Count > 0)
                ? "选定仓库与文件后提交；token 来自这份清单，清单变了会被拒绝"
                : "工作树干净";
            return result;
        }

        public static GitCommitResult Commit(GitCommitRequest request, bool lastRunFailed)
        {
            var result = new GitCommitResult();
            var selected = (request?.Repos ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (selected.Count == 0)
            {
                result.Repos.Add(new GitRepoResult { Ok = false, Error = "没有选仓库" });
                return result;
            }
            if (lastRunFailed && request.Force != true)
            {
                result.Repos.Add(new GitRepoResult { Ok = false, Error = "最近一轮迭代失败：先修好或显式 force" });
                return result;
            }

            foreach (string id in selected)
            {
                var repo = Repos().FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
                if (repo.Root == null)
                {
                    result.Repos.Add(new GitRepoResult { Id = id, Ok = false, Error = "不在白名单里" });
                    continue;
                }
                result.Repos.Add(CommitOne(id, repo.Root, request));
            }
            return result;
        }

        internal static GitRepoResult CommitOne(string id, string root, GitCommitRequest request)
        {
            var repo = ReadRepo(id, root);
            string expected = null;
            request.Tokens?.TryGetValue(id, out expected);
            if (repo.Files.Count == 0)
                return new GitRepoResult { Id = id, Ok = false, Error = "没有改动" };
            if (!string.Equals(repo.Token, expected, StringComparison.Ordinal))
                return new GitRepoResult { Id = id, Ok = false, Error = "文件清单已变化：重新预览再提交" };

            string message = string.IsNullOrWhiteSpace(request.Message) ? DefaultMessage() : request.Message.Trim();
            // 只提交刚才预览到的那批路径：这段时间别人新写的文件不能顺手带进这个提交
            var add = Git(root, ["add", "-A", "--pathspec-from-file=-", "--pathspec-file-nul"],
                null, string.Join('\0', repo.Files.Select(f => f.Path)) + "\0");
            if (!add.Ok) return new GitRepoResult { Id = id, Ok = false, Error = "add 失败：" + add.Error };

            var commit = Git(root, ["commit", "-m", message]);
            if (!commit.Ok) return new GitRepoResult { Id = id, Ok = false, Error = "commit 失败：" + commit.Error };

            var head = Git(root, ["rev-parse", "--short", "HEAD"]);
            var outcome = new GitRepoResult { Id = id, Ok = true, Commit = head.Output.Trim() };
            if (!request.Push) return outcome;

            var push = Git(root, ["push"], TimeSpan.FromMinutes(2));
            outcome.Pushed = push.Ok;
            if (!push.Ok) outcome.Error = "本地已提交，推送失败：" + push.Error;
            return outcome;
        }

        internal static GitRepoStatus ReadRepo(string id, string root)
        {
            var repo = new GitRepoStatus { Id = id, Path = root };
            var status = Git(root, ["status", "--porcelain", "-z"]);
            if (!status.Ok)
            {
                repo.Error = status.Error;
                return repo;
            }

            repo.Files = ParsePorcelain(status.Output);
            repo.Token = TokenOf(repo.Files);
            var branch = Git(root, ["rev-parse", "--abbrev-ref", "HEAD"]);
            if (branch.Ok) repo.Branch = branch.Output.Trim();

            var stat = Git(root, ["diff", "--stat"]);
            if (stat.Ok)
            {
                var lines = stat.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                repo.DiffStat = lines.Length == 0 ? "" : lines[^1].Trim();
            }
            return repo;
        }

        /// <summary>git status --porcelain -z：`XY path\0`，重命名再多一个旧路径。</summary>
        internal static List<GitFile> ParsePorcelain(string output)
        {
            var files = new List<GitFile>();
            if (string.IsNullOrEmpty(output)) return files;

            var items = output.Split('\0');
            for (int i = 0; i < items.Length; i++)
            {
                string item = items[i].TrimEnd('\r', '\n');
                if (item.Length < 4) continue;
                string status = item[..2];
                files.Add(new GitFile { Status = status, Path = item[3..] });
                if (status.IndexOf('R') >= 0 || status.IndexOf('C') >= 0) i++;
            }
            return files;
        }

        internal static string TokenOf(IEnumerable<GitFile> files)
        {
            var text = new StringBuilder();
            foreach (var file in (files ?? []).OrderBy(f => f.Path, StringComparer.Ordinal))
                text.Append(file.Status).Append(' ').Append(file.Path).Append('\n');
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
        }

        /// <summary>默认提交信息由版本基准生成：一眼能看出这次提交对应哪一组版本。</summary>
        private static string DefaultMessage()
        {
            try
            {
                var state = VersionState.Read();
                if (state != null && state.Count > 0)
                    return "iteration: " + string.Join(", ", state.OrderBy(s => s.Key, StringComparer.Ordinal)
                        .Select(s => s.Key + " " + s.Value.Version));
            }
            catch
            {
            }
            return "iteration: manual commit";
        }

        private sealed record GitOutput(bool Ok, string Output, string Error);

        private static GitOutput Git(string root, string[] args, TimeSpan? timeout = null, string stdin = null)
        {
            var info = new ProcessStartInfo("git")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin != null,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            info.Environment["GIT_TERMINAL_PROMPT"] = "0";
            info.Environment["GCM_INTERACTIVE"] = "never";
            if (stdin != null) info.StandardInputEncoding = new UTF8Encoding(false);

            try
            {
                using var process = Process.Start(info);
                if (process == null) return new GitOutput(false, "", "git 启动失败");

                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (stdin != null)
                {
                    process.StandardInput.Write(stdin);
                    process.StandardInput.Close();
                }
                if (!process.WaitForExit((int)(timeout ?? TimeSpan.FromSeconds(60)).TotalMilliseconds))
                {
                    try { process.Kill(true); } catch { }
                    return new GitOutput(false, "", "git 超时");
                }
                process.WaitForExit();
                string output = stdout.GetAwaiter().GetResult();
                string error = stderr.GetAwaiter().GetResult();
                return new GitOutput(process.ExitCode == 0, output, error.Trim());
            }
            catch (Exception ex)
            {
                return new GitOutput(false, "", ex.Message);
            }
        }
    }
}
