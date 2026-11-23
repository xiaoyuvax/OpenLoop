using OpenLoop.Core.Safety;
using OpenLoop.Core.Tools;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Source
{
    /// <summary>
    /// Loop 的读写边界（章程 §2）：读只到安装/配置路径与来源会话的工作区（/complain、/debug、worker 转来），
    /// 写得窄（配置目录、源码仓库、安装路径的配置与文档，以及与源码同级、参与编译的组件仓库）；
    /// 受保护目录里只放开 <c>.openloop/tmp</c>，二进制（backends/versions）只读。
    /// </summary>
    public static class LoopScope
    {
        /// <summary>Loop 的临时产物目录（附件、生图等）：受保护目录里唯一可读写的子路径。</summary>
        private const string TempDir = ".openloop/tmp";

        /// <summary>运行中的二进制目录：只读，换版由迭代流程与壳完成。</summary>
        private static string BinariesDir => Path.Combine(SourceSync.InstallRoot, "backends");

        private static readonly string[] BinaryExtensions =
            [".exe", ".dll", ".pdb", ".so", ".dylib", ".a", ".lib", ".obj", ".nupkg"];

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
        /// 开发布局这是同级检出；安装布局这是 <c>&lt;安装根&gt;\src\</c> 下的克隆（本来就在 InstallRoot 里）。
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
            => ComponentCatalog.Iterable().Select(s => s.Dir);

        /// <param name="ask">越界写入的审批通道（模型位/工具位的用户确认）；为 null 时直接拒绝。</param>
        /// <param name="writableComponents">额外可写的组件仓库；不传时按 <see cref="IterableComponents"/> 判定。</param>
        /// <param name="sessionWorkspace">本 Loop 会话来源会话的工作区（/complain、/debug、worker 转来）：额外只读。</param>
        public static (WorkspaceScope Read, WorkspaceScope Write) Create(
            ApprovalHandler ask = null, IEnumerable<string> writableComponents = null, string sessionWorkspace = null)
        {
            string root = SourceRoot;
            var components = (writableComponents ?? IterableComponents()).ToList();

            var read = new WorkspaceScope(root, allowed: [TempDir])
            {
                // 读：安装路径、配置目录、参与编译的组件仓库，以及来源会话的工作区；
                // 其它工作区不读（不弹审批，免得变成"问一句就能看"）
                Approve = request =>
                    Under(request.Detail, SourceSync.InstallRoot)
                    || Under(request.Detail, AppPaths.DataRoot)
                    || Under(request.Detail, sessionWorkspace)
                    || components.Any(c => Under(request.Detail, c))
                        ? ApprovalDecision.AllowAlways
                        : ApprovalDecision.Deny
            };

            var write = new WorkspaceScope(root, allowed: [TempDir])
            {
                Approve = request =>
                {
                    if (request.Kind != ApprovalKind.OutsideWorkspace)
                        return ask?.Invoke(request) ?? ApprovalDecision.Deny;

                    // 二进制只经迭代/换版流程产出与替换，工具不直接改
                    if (Under(request.Detail, BinariesDir) || Under(request.Detail, AppPaths.VersionsDir)
                        || (Under(request.Detail, SourceSync.InstallRoot) && IsBinary(request.Detail)))
                        return ApprovalDecision.Deny;

                    // 来源会话的工作区只写它自己的临时目录（附件、生图等产物），别的仍只读
                    if (!string.IsNullOrWhiteSpace(sessionWorkspace)
                        && Under(request.Detail, Path.Combine(sessionWorkspace, ".openloop", "tmp")))
                        return ApprovalDecision.AllowAlways;

                    // 配置目录（含 logs/chats/runtime）、安装路径（源码、文档）与开发布局下的同级组件仓库可写；
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

        private static bool IsBinary(string path)
        {
            string ext = Path.GetExtension(path ?? "");
            return BinaryExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
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
