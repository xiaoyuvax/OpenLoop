using OpenLoop.Core.OpenCode;

namespace OpenLoop.App
{
    /// <summary>本机 OpenCode v2 服务的单例门面：探活、模型目录、按需客户端。</summary>
    internal static class OpenCodeHub
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static readonly ProviderConfig UiProviderConfig = new() { Name = "OpenCode", Kind = "opencode" };
        private static OpenCodeEndpoint _endpoint;
        private static IReadOnlyList<ModelInfo> _models = [];
        private static Dictionary<string, OpenCodeModelRef> _byId = new(StringComparer.OrdinalIgnoreCase);

        public static OpenCodeStatus Status => _endpoint?.Status ?? OpenCodeStatus.Absent;
        public static string Url => _endpoint?.Url ?? "";
        public static IReadOnlyList<ModelInfo> Models => _models;

        /// <summary>就绪后出现在提供商下拉里的虚拟提供商；未就绪时为 null。</summary>
        public static ProviderConfig UiProvider => _endpoint is { IsReady: true } ? UiProviderConfig : null;

        public static IEnumerable<ProviderConfig> Providers => UiProvider is null ? [] : [UiProvider];

        public static event Action Changed;

        public static bool IsModel(string model)
            => !string.IsNullOrWhiteSpace(model) && _byId.ContainsKey(model.Trim());

        public static OpenCodeModelRef? Resolve(string model)
            => !string.IsNullOrWhiteSpace(model) && _byId.TryGetValue(model.Trim(), out var reference)
                ? reference
                : null;

        public static OpenCodeClient TryClient()
            => _endpoint is { IsReady: true } endpoint ? endpoint.CreateClient() : null;

        public static OpenCodeClient RequireClient()
        {
            var client = TryClient();
            if (client != null) return client;

            string detail = _endpoint?.Detail;
            throw new InvalidOperationException(
                $"OpenCode 服务未就绪：{Status}" + (string.IsNullOrWhiteSpace(detail) ? "" : $"（{detail}）"));
        }

        public static async Task RefreshAsync(CancellationToken ct = default)
        {
            if (!await Gate.WaitAsync(0, ct)) return;
            try
            {
                var previous = _endpoint;
                var endpoint = await OpenCodeEndpoint.DiscoverAsync(ct: ct);

                if (endpoint.IsReady)
                {
                    try
                    {
                        var catalogue = await LoadCatalogueAsync(endpoint, ct);
                        _models = catalogue.Models;
                        _byId = catalogue.ById;
                        UiProviderConfig.BaseUrl = endpoint.Url;
                        UiProviderConfig.Models = catalogue.Models.Select(m => m.Id).ToList();
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("OpenCode 模型目录刷新失败：" + ex.Message);
                    }
                }
                else
                {
                    _models = [];
                    _byId = new Dictionary<string, OpenCodeModelRef>(StringComparer.OrdinalIgnoreCase);
                    UiProviderConfig.Models = [];
                }

                _endpoint = endpoint;
                if (previous?.Status != endpoint.Status)
                    AppLog.Info($"OpenCode 服务：{endpoint.Status} {endpoint.Url}");
                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                AppLog.Warn("OpenCode 探活失败：" + ex.Message);
            }
            finally
            {
                Gate.Release();
            }
        }

        private static async Task<(IReadOnlyList<ModelInfo> Models, Dictionary<string, OpenCodeModelRef> ById)>
            LoadCatalogueAsync(OpenCodeEndpoint endpoint, CancellationToken ct)
        {
            using var client = endpoint.CreateClient();
            var models = await client.ListModelsAsync(ct);
            var byId = new Dictionary<string, OpenCodeModelRef>(StringComparer.OrdinalIgnoreCase);
            var infos = new List<ModelInfo>();
            foreach (var model in models)
            {
                string id = OpenCodeModelId.Format(model.Ref);
                if (!byId.TryAdd(id, model.Ref)) continue;
                infos.Add(new ModelInfo
                {
                    Id = id,
                    Name = $"{model.Name} ({model.Ref.ProviderID})",
                    IsFree = model.IsFree,
                    SupportsTools = model.SupportsTools
                });
            }
            return (infos, byId);
        }
    }
}
