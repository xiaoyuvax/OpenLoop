using OpenLoop.Core.OpenCode;
using OpenLoop.Service.Config;
using OpenLoop.Service.Logging;

namespace OpenLoop.Service.Models
{
    /// <summary>本机 OpenCode v2 服务的单例门面：探活、模型目录、按需客户端。</summary>
    public static class OpenCodeHub
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

        /// <summary>免费模型走 OpenCode Zen 的门禁更严，调用方据此保留原生工具表。</summary>
        public static bool IsFree(string model)
            => !string.IsNullOrWhiteSpace(model)
               && _models.Any(m => m.IsFree && string.Equals(m.Id, model.Trim(), StringComparison.OrdinalIgnoreCase));

        public static OpenCodeModelRef? Resolve(string model)
            => !string.IsNullOrWhiteSpace(model) && _byId.TryGetValue(model.Trim(), out var reference)
                ? reference
                : null;

        public static OpenCodeClient TryClient()
            => _endpoint is { IsReady: true } endpoint ? endpoint.CreateClient() : null;

        private static DateTimeOffset _nextRetry = DateTimeOffset.MinValue;

        /// <summary>
        /// A/I 是无人刷新模型目录的进程（只有壳会定时刷新）：解析 opencode/* 之前按需触发一次后台刷新，
        /// 不阻塞调用线程（此前的同步等待在服务半死时会拖住调用方几十秒）。
        /// 失败后 30 秒内不再重试；刷新完成走 <see cref="Changed"/> 事件，调用方下次进来即命中就绪缓存。
        /// </summary>
        public static void EnsureFresh()
        {
            if (_endpoint is { IsReady: true }) return;
            if (DateTimeOffset.Now < _nextRetry) return;
            _nextRetry = DateTimeOffset.Now.AddSeconds(30);
            _ = Task.Run(async () => await RefreshAsync());
        }

        /// <summary>
        /// opencode/* 在目录还没加载好时（A/I 刚起、opencode 刚重启）用它等就绪：异步轮询探活，不占线程、
        /// 不判失败。服务没在运行（没有 service.json）或已就绪却没有这个模型时给出可执行的错误，不空等。
        /// </summary>
        public static async Task<(OpenCodeClient Client, OpenCodeModelRef Model)> WaitModelAsync(
            string model, CancellationToken ct = default)
        {
            string id = (model ?? "").Trim();
            while (true)
            {
                EnsureFresh();
                if (_endpoint is { IsReady: true })
                {
                    if (_byId.TryGetValue(id, out var reference)) return (_endpoint.CreateClient(), reference);
                    throw new InvalidOperationException($"OpenCode 没有提供这个模型：{id}");
                }
                if (_endpoint is { Status: OpenCodeStatus.Absent })
                    throw new InvalidOperationException("OpenCode 服务没在运行（没有 service.json），先启动 opencode"
                        + (string.IsNullOrWhiteSpace(_endpoint.Detail) ? "" : $"：{_endpoint.Detail}"));
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }

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
            // 共享 HttpClient 超时是 30 分钟（单次调用没有总时限）：目录刷新只是本机接口，卡住 20 秒就
            // 放掉刷新闸门，下轮 EnsureFresh 还能重试，不会因为一次半死的服务长期占着不刷新。
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var models = await client.ListModelsAsync(timeout.Token);
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
                    ContextLength = model.Context > int.MaxValue ? int.MaxValue : (int)model.Context,
                    IsFree = model.IsFree,
                    SupportsTools = model.SupportsTools
                });
            }
            return (infos, byId);
        }
    }
}
