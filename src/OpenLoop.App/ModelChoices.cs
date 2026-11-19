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
                .Where(m => settings == null || !settings.ModelDisabled(m.Id))
                .Where(m => !OpenLoop.Service.Media.MediaModels.IsImageModel(m.Id))
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

        /// <summary>每个提供商在下拉顶部最多列几个最近用过的模型。</summary>
        private const int RecentPerProvider = 3;

        private static readonly object Divider = new();

        /// <summary>最近用过的置顶（每个提供商最多 3 个）、隔一条线，下面才是全量列表。</summary>
        public static void Fill(AntdUI.Select select, IEnumerable<ModelInfo> models, AppSettings settings)
        {
            var all = models as IList<ModelInfo> ?? models.ToList();
            var recent = Recent(all, settings);
            var recentIds = recent.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            select.DrawItem -= DrawDivider;
            select.DrawItem += DrawDivider;
            select.Items.Clear();
            foreach (var model in recent) select.Items.Add(Item(model));
            if (recent.Count > 0 && recent.Count < all.Count)
                select.Items.Add(new AntdUI.SelectItem("") { Tag = Divider, Enable = false });
            foreach (var model in all.Where(m => !recentIds.Contains(m.Id))) select.Items.Add(Item(model));
        }

        private static AntdUI.SelectItem Item(ModelInfo model) => new(model.Name)
        {
            Tag = model.Id,
            IconSvg = model.IsFree ? UiIcons.Dot(UiStyle.Success) : null,
            SubText = model.IsFree ? "免费" : null
        };

        /// <summary>分隔条不用文本行渲染，改画一条细线，占位保持一行高。</summary>
        private static void DrawDivider(object sender, AntdUI.DrawItemEventArgs e)
        {
            if (!ReferenceEquals(e.Item?.Tag, Divider)) return;
            // 不设 Handled，AntdUI 随后还会画一遍条目本身（底色）把线盖掉，所以必须自己接管。
            e.Handled = true;
            int y = e.Rect.Y + e.Rect.Height / 2;
            e.Canvas.DrawLine(UiStyle.Border, 1, e.Rect.Left + 8, y, e.Rect.Right - 8, y);
        }

        private static List<ModelInfo> Recent(IEnumerable<ModelInfo> models, AppSettings settings)
        {
            var result = new List<ModelInfo>();
            var recent = settings?.RecentModels;
            if (recent == null || recent.Count == 0) return result;

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in recent)
            {
                var model = models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
                if (model == null) continue;

                string owner = settings.ProviderFor(id)?.Name
                    ?? (OpenCodeHub.IsModel(id) ? "opencode" : "");
                int used = counts.TryGetValue(owner, out int n) ? n : 0;
                if (used >= RecentPerProvider) continue;
                counts[owner] = used + 1;
                result.Add(model);
            }
            return result;
        }

        /// <summary>记一次模型使用：挪到最前、去重、超上限截掉最早的。</summary>
        public static void RecordUse(AppSettings settings, string modelId)
            => settings?.RecordRecentModel(modelId);

        private static bool LooksFree(string id)
            => !string.IsNullOrWhiteSpace(id)
                && id.Contains("free", StringComparison.OrdinalIgnoreCase);

        /// <summary>模型上下文上限：设置覆盖 → 目录/OpenCode → 内置表 → 保守默认。</summary>
        public static int ContextLimit(string model, AppSettings settings) => ModelCatalog.ContextLimit(model, settings);

        /// <summary>工具栏的「生图模型」下拉：只在存在生图模型时显示，「只看免费」同样生效。</summary>
        public static List<ModelInfo> Images(AppSettings settings, bool onlyFree)
            => OpenLoop.Service.Media.MediaModels.ImageModels(settings)
                .Where(m => !onlyFree || m.IsFree).ToList();

        /// <summary>按提供商筛选模型；provider 为 null 表示全部。</summary>
        public static List<ModelInfo> FilterByProvider(List<ModelInfo> models, ProviderConfig provider)
        {
            if (provider == null) return models;
            var ids = new HashSet<string>(provider.Models ?? [], StringComparer.OrdinalIgnoreCase);
            return models.Where(m => ids.Contains(m.Id)).ToList();
        }
    }
}
