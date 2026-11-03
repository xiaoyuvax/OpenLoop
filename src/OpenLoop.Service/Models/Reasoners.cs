using OpenLoop.Core.OpenCode;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Safety;
using OpenLoop.Core.Tools;
using OpenLoop.Service.Config;
using OpenLoop.Service.Plugins;

namespace OpenLoop.Service.Models
{
    /// <summary>按模型 id 选择通道：插件贡献 → opencode/* → 其余 OpenAI 兼容接口。</summary>
    public static class Reasoners
    {
        public static IReasoner Create(AppSettings settings, string model, string effort, string workspace = null)
        {
            if (model != null && model.StartsWith(OpenCodeModelId.Prefix, StringComparison.OrdinalIgnoreCase))
                OpenCodeHub.EnsureFresh();

            if (OpenCodeHub.Resolve(model) is { } reference)
                return new OpenCodeReasoner(OpenCodeHub.RequireClient(), reference,
                    string.IsNullOrWhiteSpace(workspace) ? settings.Workspace : workspace);

            string pluginWorkspace = string.IsNullOrWhiteSpace(workspace) ? settings.Workspace : workspace;
            if (PluginCatalog.TryProvider(pluginWorkspace, model, out var provider))
            {
                string key = string.IsNullOrWhiteSpace(provider.KeyEnv)
                    ? ""
                    : Environment.GetEnvironmentVariable(provider.KeyEnv) ?? "";
                return new OpenAiCompatibleReasoner(provider.BaseUrl, model, key, effort);
            }

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

    public static class OpenCodeTools
    {
        public static IEnumerable<ITool> For(string workspace, ApprovalHandler approve = null, bool askAllPermissions = true)
            => [new OpenCodeTaskTool(() => OpenCodeHub.TryClient(), workspace, OpenCodeHub.Resolve, approve, askAllPermissions)];
    }
}
