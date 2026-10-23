using System.Net.Http.Headers;
using System.Text.Json;

namespace OpenLoop.App
{
    internal static class ProviderCatalog
    {
        private static readonly HttpClient Http = new(new SocketsHttpHandler())
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        /// <summary>OpenAI 兼容的 /models 列表，只取 id；内置提供商才调用。</summary>
        public static async Task<List<string>> FetchModelsAsync(ProviderConfig provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            string url = provider.BaseUrl.Trim().TrimEnd('/') + "/models";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            string key = AppSettings.ResolveKey(provider);
            if (!string.IsNullOrWhiteSpace(key))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

            using var response = await Http.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"模型列表返回 {(int)response.StatusCode}：{Trim(body)}");

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("模型列表格式异常：缺少 data 数组");

            var ids = new List<string>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) continue;
                string value = id.GetString();
                if (!string.IsNullOrWhiteSpace(value)) ids.Add(value);
            }
            return ids.Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string Trim(string text)
            => string.IsNullOrEmpty(text) ? "" : (text.Length <= 200 ? text : text[..200] + "…");
    }
}
