namespace OpenLoop.App
{
    /// <summary>把提供商里登记的模型与 OpenRouter 目录合并成下拉项；免费过滤只作用于目录里的模型。</summary>
    internal static class ModelChoices
    {
        public static List<ModelInfo> Build(AppSettings settings, bool onlyFree)
        {
            var catalog = ModelCatalog.All;
            var result = new List<ModelInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var providers = settings?.Providers ?? [];
            bool hasOpenRouter = providers.Any(p =>
                string.Equals(p.Kind, "openrouter", StringComparison.OrdinalIgnoreCase));

            foreach (var provider in providers)
            {
                foreach (string id in provider.Models ?? [])
                {
                    if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) continue;
                    var known = catalog.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (known != null)
                    {
                        if (!onlyFree || known.IsFree) result.Add(known);
                    }
                    else
                    {
                        result.Add(new ModelInfo { Id = id, Name = id, SupportsTools = true });
                    }
                }
            }

            if (hasOpenRouter || result.Count == 0)
            {
                foreach (var model in catalog)
                {
                    if (onlyFree && !model.IsFree) continue;
                    if (seen.Add(model.Id)) result.Add(model);
                }
            }

            return result
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>按提供商筛选模型；provider 为 null 表示全部。</summary>
        public static List<ModelInfo> FilterByProvider(List<ModelInfo> models, ProviderConfig provider)
        {
            if (provider == null) return models;
            var ids = new HashSet<string>(provider.Models ?? [], StringComparer.OrdinalIgnoreCase);
            return models.Where(m => ids.Contains(m.Id)).ToList();
        }
    }
}
