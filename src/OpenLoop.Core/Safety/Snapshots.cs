using System.Diagnostics;
using System.Text;

namespace OpenLoop.Core.Safety
{
    /// <summary>
    /// 每轮开始前的工作区快照：用 git 记录已跟踪文件的当前内容，不触碰工作区。
    /// 不是 git 仓库/没有改动/git 不可用时返回 null，调用方照常执行。
    /// </summary>
    public static class Snapshots
    {
        public static string Create(string workspace, string label = "checkpoint")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace)) return null;
                if (RunGit(workspace, "rev-parse", "--is-inside-work-tree")?.Trim() != "true") return null;

                string sha = RunGit(workspace, "stash", "create", "openloop " + label)?.Trim();
                if (string.IsNullOrEmpty(sha)) return null;

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                RunGit(workspace, "update-ref", $"refs/openloop/{stamp}", sha);
                return sha;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>把快照内容写回工作区（显式回滚入口，不在任务流程里自动调用）。</summary>
        public static bool Restore(string workspace, string sha)
        {
            if (string.IsNullOrWhiteSpace(sha)) return false;
            string output = RunGit(workspace, "checkout", sha, "--", ".");
            return output != null;
        }

        private static string RunGit(string workspace, params string[] args)
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

            using var process = Process.Start(psi);
            if (process == null) return null;
            var stdout = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(15000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }
            process.WaitForExit();
            return process.ExitCode == 0 ? stdout.ToString() : null;
        }
    }
}
