using OpenLoop.Core.Agent;
using OpenLoop.Service.Plugins;

namespace OpenLoop.App
{
    /// <summary>
    /// 把提供商里登记的模型与 OpenRouter 目录合并成下拉项。
    /// 免费判定：目录里有的按 pricing；目录里没有的，id 带 free 才算免费，其余默认收费。
    /// </summary>
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
                        // 目录里没有定价信息：只有名称/id 明确带 free 才算免费，其余默认收费。
                        bool free = LooksFree(id);
                        if (!onlyFree || free)
                            result.Add(new ModelInfo { Id = id, Name = id, SupportsTools = true, IsFree = free });
                    }
                }
            }

            foreach (var model in OpenCodeHub.Models)
            {
                if (onlyFree && !model.IsFree) continue;
                if (seen.Add(model.Id)) result.Add(model);
            }

            if (settings != null && DshAcpHub.Installed(settings))
            {
                foreach (string id in DshAcpHub.CachedModels(settings))
                {
                    if (onlyFree && !LooksFree(id)) continue;
                    if (seen.Add(id))
                        result.Add(new ModelInfo { Id = id, Name = id, SupportsTools = true, IsFree = LooksFree(id) });
                }
            }

            // shortcut: provider 贡献不声明价格，这里一律按免费列出来；要区分收费通道再加字段
            using (var plugins = PluginCatalog.Load(settings?.Workspace ?? ""))
            {
                foreach (string id in plugins.Models())
                    if (seen.Add(id))
                        result.Add(new ModelInfo { Id = id, Name = id, SupportsTools = true, IsFree = true });
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
                .Select(m => Rename(m, settings))
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>设置里配了显示名称就用它；其余字段照抄目录项（ModelInfo 是 init-only，只能新建）。</summary>
        private static ModelInfo Rename(ModelInfo model, AppSettings settings)
        {
            string name = settings?.PropsFor(model.Id)?.Name;
            if (string.IsNullOrWhiteSpace(name)) return model;
            return new ModelInfo
            {
                Id = model.Id,
                Name = name.Trim(),
                ContextLength = model.ContextLength,
                IsFree = model.IsFree,
                SupportsTools = model.SupportsTools
            };
        }

        private static bool LooksFree(string id)
            => !string.IsNullOrWhiteSpace(id)
                && id.Contains("free", StringComparison.OrdinalIgnoreCase);

        /// <summary>模型上下文上限：设置覆盖 → 目录/OpenCode → 内置表 → 保守默认。</summary>
        public static int ContextLimit(string model, AppSettings settings) => ModelCatalog.ContextLimit(model, settings);

        /// <summary>按提供商筛选模型；provider 为 null 表示全部。</summary>
        public static List<ModelInfo> FilterByProvider(List<ModelInfo> models, ProviderConfig provider)
        {
            if (provider == null) return models;
            var ids = new HashSet<string>(provider.Models ?? [], StringComparer.OrdinalIgnoreCase);
            return models.Where(m => ids.Contains(m.Id)).ToList();
        }
    }
}
