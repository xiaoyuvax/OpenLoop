using OpenLoop.Core.Store;

namespace OpenLoop.App
{
    /// <summary>
    /// /report：把用户的一条观察记入当前工作区的 findings。
    /// 不启动会话、不调模型、不触发演进——处置留给下一次演进集中分诊。
    /// </summary>
    internal static class FindingCommand
    {
        internal static bool TryRecord(string workspace, string text, out string message)
        {
            var (ok, result) = FindingTriage.Report(workspace, text);
            message = result;
            return ok;
        }
    }
}
