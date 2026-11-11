using OpenLoop.Core.Safety;
using OpenLoop.Core.Tools;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Source
{
    /// <summary>
    /// Loop 的读写边界（章程 §2）：读得宽（任意路径、克隆仓库、查配置），
    /// 写得窄（配置目录、源码仓库、二进制安装路径，以及与源码同级、参与编译的组件仓库）。
    /// </summary>
    public static class LoopScope
    {
        /// <summary>源码工作树：优先用 clone 来的那份；开发环境下退回当前仓库。</summary>
        public static string SourceRoot
        {
            get
            {
                string cloned = SourceSync.SourceRoot;
                if (Directory.Exists(Path.Combine(cloned, ".git"))) return cloned;

                string cwd = Directory.GetCurrentDirectory();
                return Directory.Exists(Path.Combine(cwd, "src", "OpenLoop.App")) ? cwd : cloned;
            }
        }

        /// <summary>
        /// 与源码同级、随本仓库一起编译的组件仓库：PuppetCore 两平台都要，ChatFlow 只有 Windows 版本编译。
        /// 开发布局这是同级检出；安装布局这是 <c>&lt;安装根&gt;\source\</c> 下的克隆（本来就在 InstallRoot 里）。
        /// </summary>
        public static IEnumerable<string> IterableComponents()
        {
            foreach (string folder in ComponentFolders())
            {
                string root = SourceSync.SiblingOf(SourceSync.SourceRoot, folder);
                if (!string.IsNullOrEmpty(root) && Directory.Exists(Path.Combine(root, ".git")))
                    yield return root;
            }
        }

        private static IEnumerable<string> ComponentFolders()
        {
            yield return "PuppetCore";
            if (OperatingSystem.IsWindows()) yield return "ChatFlow";
        }

        /// <param name="ask">越界写入的审批通道（模型位/工具位的用户确认）；为 null 时直接拒绝。</param>
        /// <param name="writableComponents">额外可写的组件仓库；不传时按 <see cref="IterableComponents"/> 判定。</param>
        public static (WorkspaceScope Read, WorkspaceScope Write) Create(
            ApprovalHandler ask = null, IEnumerable<string> writableComponents = null)
        {
            string root = SourceRoot;
            var components = (writableComponents ?? IterableComponents()).ToList();

            var read = new WorkspaceScope(root)
            {
                // 读：与 Worker 相同，不设边界
                Approve = _ => ApprovalDecision.AllowAlways
            };

            var write = new WorkspaceScope(root)
            {
                Approve = request =>
                {
                    if (request.Kind != ApprovalKind.OutsideWorkspace)
                        return ask?.Invoke(request) ?? ApprovalDecision.Deny;

                    // 配置目录（含 logs/chats/runtime）、安装路径（源码、二进制）与开发布局下的同级组件仓库可写；
                    // 其它越界写一律拒绝
                    return Under(request.Detail, AppPaths.DataRoot)
                        || Under(request.Detail, SourceSync.InstallRoot)
                        || components.Any(c => Under(request.Detail, c))
                        ? ApprovalDecision.AllowAlways
                        : ask?.Invoke(request) ?? ApprovalDecision.Deny;
                }
            };

            return (read, write);
        }

        private static bool Under(string path, string root)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
                string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
                return full.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
