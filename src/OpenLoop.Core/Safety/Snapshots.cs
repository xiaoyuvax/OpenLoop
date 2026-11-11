using System.Diagnostics;

namespace OpenLoop.Core.Safety
{
    /// <summary>
    /// 每轮开始前的工作区快照：把整个工作树（已跟踪 + 未被 .gitignore 忽略的未跟踪文件）
    /// 写成一个 git tree 对象，不触碰真实索引、工作区和分支。
    /// 不是 git 仓库/git 不可用时返回 null，调用方照常执行。
    /// </summary>
    public static class Snapshots
    {
        public static string Create(string workspace, string label = "checkpoint", string refName = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace)) return null;
                if (RunGit(workspace, "rev-parse", "--is-inside-work-tree")?.Trim() != "true") return null;

                string tree = CaptureTree(workspace);
                if (string.IsNullOrEmpty(tree)) return null;

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                RunGit(workspace, "update-ref", refName ?? $"refs/openloop/{stamp}", tree);
                return tree;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>只把工作树写成一个 tree 对象（不挂 ref）；用于记录「这份产物由哪份源码编出来」。</summary>
        public static string Tree(string workspace)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace)) return null;
                if (RunGit(workspace, "rev-parse", "--is-inside-work-tree")?.Trim() != "true") return null;
                return CaptureTree(workspace);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>tree 里某个目录下所有条目的 (blob sha, 仓库相对路径)；目录为空表示整棵树。</summary>
        public static List<(string Sha, string Path)> ListTree(string workspace, string tree, string relativeDir)
        {
            var entries = new List<(string, string)>();
            if (string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(tree)) return entries;
            try
            {
                string dir = (relativeDir ?? "").Trim().Trim('/').Replace('\\', '/');
                string output = dir.Length == 0
                    ? RunGit(workspace, "ls-tree", "-r", "-z", tree)
                    : RunGit(workspace, "ls-tree", "-r", "-z", tree, "--", dir);
                if (output == null) return entries;

                foreach (string item in output.Split('\0'))
                {
                    // 100644 blob <sha>\t<path>
                    int tab = item.IndexOf('\t');
                    if (tab <= 0) continue;
                    string[] head = item[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (head.Length < 3 || head[1] != "blob") continue;
                    entries.Add((head[2], item[(tab + 1)..].TrimEnd('\r', '\n')));
                }
            }
            catch
            {
            }
            return entries;
        }

        /// <summary>把已有 tree 挂到指定 ref（约定放 refs/openloop/ 下，不进分支也不影响工作区）。</summary>
        public static bool Pin(string workspace, string refName, string tree)
            => !string.IsNullOrWhiteSpace(workspace) && !string.IsNullOrWhiteSpace(refName) && !string.IsNullOrWhiteSpace(tree)
               && RunGit(workspace, "update-ref", refName, tree) != null;

        /// <summary>
        /// 把 tree 包成游离提交（父 = 当前 HEAD）：不动分支/索引/工作区，但能 git show / git log / git diff HEAD &lt;提交&gt;。
        /// 没有 git 身份（user.name/email）时 commit-tree 会失败，此时退回裸 tree。
        /// </summary>
        public static (string Object, bool IsCommit) CommitTree(string workspace, string tree, string message)
        {
            if (string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(tree)) return (null, false);
            try
            {
                var args = new List<string> { "commit-tree", tree };
                string head = RunGit(workspace, "rev-parse", "HEAD")?.Trim();
                if (!string.IsNullOrEmpty(head))
                {
                    args.Add("-p");
                    args.Add(head);
                }
                args.Add("-m");
                args.Add(string.IsNullOrWhiteSpace(message) ? "iteration" : message);

                string commit = RunGit(workspace, args.ToArray())?.Trim();
                return string.IsNullOrEmpty(commit) ? (tree, false) : (commit, true);
            }
            catch
            {
                return (tree, false);
            }
        }

        /// <summary>
        /// 把工作区还原到快照状态：恢复快照里的文件内容，并删掉快照之后新建的文件
        /// （未被 .gitignore 忽略的；忽略文件不动）。只写工作区，不碰真实索引。
        /// </summary>
        public static bool Restore(string workspace, string snapshot)
        {
            if (string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(snapshot)) return false;
            if (!Directory.Exists(workspace)) return false;

            HashSet<string> keep = ListSnapshotFiles(workspace, snapshot);
            if (keep == null) return false;

            foreach (string path in ListWorktreeFiles(workspace))
            {
                if (keep.Contains(path)) continue;
                try
                {
                    string full = Path.Combine(workspace, path);
                    if ((File.GetAttributes(full) & FileAttributes.Directory) != 0)
                        Directory.Delete(full, recursive: false);
                    else
                        File.Delete(full);
                }
                catch { }
            }

            string index = TempIndexPath();
            try
            {
                return RunGitWithIndex(workspace, index, "read-tree", snapshot) != null
                    && RunGitWithIndex(workspace, index, "checkout-index", "-a", "-f") != null;
            }
            finally
            {
                TryDelete(index);
            }
        }

        /// <summary>用临时索引把工作树写成 tree（git add -A 天然跳过 .gitignore）。</summary>
        private static string CaptureTree(string workspace)
        {
            // shortcut: 不设单文件大小上限，超大未跟踪文件也会进快照；需要时按 OpenCode 的 2 MiB 规则过滤
            string index = TempIndexPath();
            try
            {
                if (RunGitWithIndex(workspace, index, "add", "-A") == null) return null;
                return RunGitWithIndex(workspace, index, "write-tree")?.Trim();
            }
            finally
            {
                TryDelete(index);
            }
        }

        private static HashSet<string> ListSnapshotFiles(string workspace, string snapshot)
        {
            string output = RunGit(workspace, "ls-tree", "-r", "-z", "--name-only", snapshot);
            return output == null ? null : SplitZ(output);
        }

        private static HashSet<string> ListWorktreeFiles(string workspace)
        {
            string output = RunGit(workspace, "ls-files", "-z", "-c", "-o", "--exclude-standard");
            return output == null ? [] : SplitZ(output);
        }

        private static HashSet<string> SplitZ(string output)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (string item in output.Split('\0'))
            {
                string path = item.TrimEnd('\r', '\n');
                if (path.Length > 0) set.Add(path);
            }
            return set;
        }

        private static string TempIndexPath()
            => Path.Combine(Path.GetTempPath(), "openloop-snapshot-" + Guid.NewGuid().ToString("N"));

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static string RunGit(string workspace, params string[] args)
            => RunGitCore(workspace, null, args);

        private static string RunGitWithIndex(string workspace, string indexFile, params string[] args)
            => RunGitCore(workspace, indexFile, args);

        private static string RunGitCore(string workspace, string indexFile, string[] args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workspace,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string arg in args) psi.ArgumentList.Add(arg);
            ChildEnvironment.Apply(psi);
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            if (!string.IsNullOrEmpty(indexFile)) psi.Environment["GIT_INDEX_FILE"] = indexFile;

            using var process = Process.Start(psi);
            if (process == null) return null;
            var stdout = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }
            process.WaitForExit();
            return process.ExitCode == 0 ? stdout.GetAwaiter().GetResult() : null;
        }
    }
}
