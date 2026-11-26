using OpenLoop.Core.Safety;

namespace OpenLoop.Core.OpenCode
{
    /// <summary>OpenCode 权限 → OpenLoop 审批的映射（模型位与任务工具共用一套判定）。</summary>
    internal static class OpenCodePermissions
    {
        /// <summary>没有审批通道就返回 null（调用方据此一律拒绝）。</summary>
        public static Func<OpenCodePermission, bool> Decider(string workspace, ApprovalHandler approve)
            => approve == null
                ? null
                : permission => approve(new ApprovalRequest(
                    KindFor(workspace, permission), "OpenCode", Describe(permission), "OpenCode"))
                    != ApprovalDecision.Deny;

        public static ApprovalKind KindFor(string workspace, OpenCodePermission permission)
        {
            foreach (string resource in permission.Resources ?? [])
            {
                if (!Path.IsPathRooted(resource)) continue;
                if (!IsInsideWorkspace(workspace, resource)) return ApprovalKind.OutsideWorkspace;
            }
            return ApprovalKind.SensitiveAction;
        }

        public static bool IsInsideWorkspace(string workspace, string resource)
        {
            try
            {
                int wildcard = resource.IndexOfAny(['*', '?']);
                string path = wildcard >= 0 ? resource[..wildcard] : resource;
                if (path.Length == 0) return true;

                string root = Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar);
                string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                return full.Equals(root, StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string Describe(OpenCodePermission permission)
        {
            string resources = permission.Resources is { Count: > 0 }
                ? " " + string.Join(", ", permission.Resources)
                : "";
            string message = string.IsNullOrWhiteSpace(permission.Message) ? "" : $"（{permission.Message}）";
            return permission.Action + resources + message;
        }
    }
}
