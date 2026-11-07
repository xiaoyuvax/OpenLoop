using OpenLoop.Core.Acp;
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
        public static IReasoner Create(AppSettings settings, string model, string effort, string workspace = null,
            ApprovalHandler approve = null)
        {
            if (DshAcpHub.IsDsh(model))
                return DshAcpHub.Reasoner(settings, model, workspace);

            if (model != null && model.StartsWith(OpenCodeModelId.Prefix, StringComparison.OrdinalIgnoreCase))
                OpenCodeHub.EnsureFresh();

            if (OpenCodeHub.Resolve(model) is { } reference)
                return new OpenCodeReasoner(OpenCodeHub.RequireClient(), reference,
                    string.IsNullOrWhiteSpace(workspace) ? settings.Workspace : workspace, approve);

            string pluginWorkspace = string.IsNullOrWhiteSpace(workspace) ? settings.Workspace : workspace;
            if (PluginCatalog.TryProvider(pluginWorkspace, model, out var provider))
                return new OpenAiCompatibleReasoner(
                    provider.BaseUrl, provider.WireModel(model), provider.ResolveKey(), effort);

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
        /// <summary>额度/可用性冷却：撞过 402 之类就先别把工具摆给 agent，否则它会反复硬撞。</summary>
        internal static readonly QuotaGate Gate = new("OpenCode", TimeSpan.FromMinutes(10));

        public static IEnumerable<ITool> For(string workspace, ApprovalHandler approve = null, bool askAllPermissions = true)
            => OpenCodeHub.UiProvider == null || !Gate.Open
                ? []
                : [new OpenCodeTaskTool(() => OpenCodeHub.TryClient(), workspace, OpenCodeHub.Resolve,
                    approve, askAllPermissions, Gate.Note)];
    }

    /// <summary>dsh 的任务工具。与 <see cref="OpenCodeTools"/> 对称：可用、额度够才暴露。</summary>
    public static class DshTools
    {
        // shortcut: 闸门按链路不分模型，一个免费模型限额会把整条 dsh 任务工具一起藏 10 分钟；
        // 等真的频繁撞限额、且同链路里还有别的可用模型时再拆成按模型计。
        internal static readonly QuotaGate Gate = new("dsh", TimeSpan.FromMinutes(10));

        /// <summary>
        /// 可用性只看缓存：起 dsh 探测要几秒，不能在建工具表时同步等。
        /// 缓存为空 = 没装 dsh 或还没探测成功，都不暴露。
        /// </summary>
        public static IEnumerable<ITool> For(AppSettings settings, string workspace, ApprovalHandler approve = null)
        {
            if (settings == null || !Gate.Open || DshAcpHub.CachedModels(settings).Count == 0) return [];
            return [new DshTaskTool(
                (task, model, decide, ct) => DshAcpHub.RunTask(settings, workspace, model, task, decide, ct),
                workspace, approve, Gate.Note)];
        }
    }
}
