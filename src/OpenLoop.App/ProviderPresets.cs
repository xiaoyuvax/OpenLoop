namespace OpenLoop.App
{
    internal sealed record ProviderPreset(string Kind, string Name, string BaseUrl);

    /// <summary>内置提供商：只预填地址，模型仍由用户拉取/添加。</summary>
    internal static class ProviderPresets
    {
        public static IReadOnlyList<ProviderPreset> All { get; } =
        [
            new("openrouter", "OpenRouter", "https://openrouter.ai/api/v1"),
            new("openai", "OpenAI", "https://api.openai.com/v1"),
            new("deepseek", "DeepSeek", "https://api.deepseek.com/v1"),
            new("groq", "Groq", "https://api.groq.com/openai/v1"),
            new("mistral", "Mistral", "https://api.mistral.ai/v1"),
            new("xai", "xAI", "https://api.x.ai/v1"),
            new("siliconflow", "SiliconFlow", "https://api.siliconflow.cn/v1"),
            new("teamorouter", "TeamRouter", "https://teamorouter.com/v1")
        ];

        public static ProviderPreset Match(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return null;
            string url = baseUrl.Trim().TrimEnd('/');
            return All.FirstOrDefault(p =>
                string.Equals(p.BaseUrl, url, StringComparison.OrdinalIgnoreCase));
        }
    }
}
