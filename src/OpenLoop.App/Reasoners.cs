using OpenLoop.Core.OpenCode;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Safety;
using OpenLoop.Core.Tools;

namespace OpenLoop.App
{
    /// <summary>按模型 id 选择通道：opencode/* 走本机 OpenCode，其余走 OpenAI 兼容接口。</summary>
    internal static class Reasoners
    {
        public static IReasoner Create(AppSettings settings, string model, string effort, string workspace = null)
        {
            if (OpenCodeHub.Resolve(model) is { } reference)
                return new OpenCodeReasoner(OpenCodeHub.RequireClient(), reference,
                    string.IsNullOrWhiteSpace(workspace) ? settings.Workspace : workspace);

            var (baseUrl, apiKey) = settings.EndpointFor(model);
            return new OpenAiCompatibleReasoner(baseUrl, model, apiKey, effort);
        }

        public static void AttachProgress(
            IReasoner reasoner, Action<OpenAiCompatibleReasoner.ReasonerProgress> handler)
        {
            switch (reasoner)
            {
                case OpenAiCompatibleReasoner http: http.OnProgress = handler; break;
                case OpenCodeReasoner opencode: opencode.OnProgress = handler; break;
            }
        }
    }

    internal static class OpenCodeTools
    {
        public static IEnumerable<ITool> For(string workspace, ApprovalHandler approve = null, bool askAllPermissions = true)
            => [new OpenCodeTaskTool(() => OpenCodeHub.TryClient(), workspace, OpenCodeHub.Resolve, approve, askAllPermissions)];
    }
}
