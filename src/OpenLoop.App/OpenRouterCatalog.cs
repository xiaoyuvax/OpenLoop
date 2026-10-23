using System.Net.Http.Headers;
using System.Text.Json;

namespace OpenLoop.App
{
    /// <summary>
    /// OpenRouter 免费模型目录：直接查 /api/v1/models，按 pricing 为 0 过滤。
    /// 不硬编码模型 id——免费清单会变，硬编码必然过期。
    /// </summary>
    internal sealed class ModelInfo
    {
        public string Id { get; init; }
        public string Name { get; init; }
        public int ContextLength { get; init; }
        public bool IsFree { get; init; }
        public bool SupportsTools { get; init; }

        public override string ToString() => Name;
    }

    internal static class OpenRouterCatalog
    {
        private static readonly HttpClient Http = new(new SocketsHttpHandler())
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static async Task<List<ModelInfo>> FetchModelsAsync(string apiKey)
        {
            var models = await QueryAsync(apiKey);
            return models
                .Where(m => m.SupportsTools)
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static async Task<List<string>> FetchFreeModelsAsync(string apiKey)
        {
            var models = await QueryAsync(apiKey);
            return models
                .Where(m => m.IsFree)
                .Select(m => m.Id)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static async Task<List<ModelInfo>> QueryAsync(string apiKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/models");
            if (!string.IsNullOrWhiteSpace(apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await Http.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"模型目录返回 {(int)response.StatusCode}：{Trim(body)}");

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("模型目录格式异常：缺少 data 数组");

            var models = new List<ModelInfo>();
            foreach (var model in data.EnumerateArray())
            {
                if (!model.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String) continue;

                var pricing = model.TryGetProperty("pricing", out var p) ? p : default;
                string promptPrice = pricing.ValueKind == JsonValueKind.Object ? ReadString(pricing, "prompt") : null;
                string completionPrice = pricing.ValueKind == JsonValueKind.Object ? ReadString(pricing, "completion") : null;

                models.Add(new ModelInfo
                {
                    Id = idEl.GetString(),
                    Name = model.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                        ? nameEl.GetString()
                        : idEl.GetString(),
                    ContextLength = model.TryGetProperty("context_length", out var ctx)
                        && ctx.ValueKind == JsonValueKind.Number && ctx.TryGetInt32(out int c) ? c : 0,
                    IsFree = promptPrice == "0" && completionPrice == "0",
                    SupportsTools = SupportsTools(model)
                });
            }

            return models;
        }

        private static bool SupportsTools(JsonElement model)
        {
            if (!model.TryGetProperty("supported_parameters", out var parameters)
                || parameters.ValueKind != JsonValueKind.Array)
                return false;
            foreach (var item in parameters.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String
                    && string.Equals(item.GetString(), "tools", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static string ReadString(JsonElement element, string name)
            => element.TryGetProperty(name, out var value) ? value.ToString() : null;

        private static string Trim(string text)
            => string.IsNullOrEmpty(text) ? "" : (text.Length <= 300 ? text : text[..300] + "…");
    }
}
