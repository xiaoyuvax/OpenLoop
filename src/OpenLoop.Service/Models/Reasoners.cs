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
        /// <param name="keepTools">null = 默认（免费模型保留工具表过上游门禁）；false = 一次性短问答（标题等）不保留。</param>
        public static IReasoner Create(AppSettings settings, string model, string effort, string workspace = null,
            ApprovalHandler approve = null, bool? keepTools = null)
        {
            if (DshAcpHub.IsDsh(model))
                return DshAcpHub.Reasoner(settings, model, workspace);

            if (model != null && model.StartsWith(OpenCodeModelId.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                OpenCodeHub.EnsureFresh();
                string scope = string.IsNullOrWhiteSpace(workspace) ? settings.Workspace : workspace;
                if (OpenCodeHub.Resolve(model) is { } reference)
                    return new OpenCodeReasoner(OpenCodeHub.RequireClient(), reference, scope, approve,
                        keepTools: keepTools ?? OpenCodeHub.IsFree(model));

                // 目录还没加载好：首轮等就绪再调用，别把 opencode/* 的请求发到别的端点去换一个「模型不存在」。
                string pendingId = model.Trim();
                return new OpenCodeReasoner(ct => OpenCodeHub.WaitModelAsync(pendingId, ct), pendingId, scope,
                    approve, () => keepTools ?? OpenCodeHub.IsFree(pendingId));
            }

            string pluginWorkspace = string.IsNullOrWhiteSpace(workspace) ? settings.Workspace : workspace;
            if (PluginCatalog.TryProvider(pluginWorkspace, model, out var provider))
                return new OpenAiCompatibleReasoner(
                    provider.BaseUrl, provider.WireModel(model), provider.ResolveKey(), effort);

            var (baseUrl, apiKey) = settings.EndpointFor(model);
            return new OpenAiCompatibleReasoner(baseUrl, model, apiKey, effort);
        }

    }
}
