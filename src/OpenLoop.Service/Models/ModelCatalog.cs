using OpenLoop.Service.Config;

namespace OpenLoop.Service.Models
{
    /// <summary>进程内模型目录。启动时刷新一次，所有会话共享。</summary>
    public static class ModelCatalog
    {
        private static readonly List<ModelInfo> Items = new();

        public static event Action Changed;

        public static IReadOnlyList<ModelInfo> All => Items;

        public static void Set(IEnumerable<ModelInfo> models)
        {
            Items.Clear();
            Items.AddRange(models);
            Changed?.Invoke();
        }

        /// <summary>
        /// 上下文上限，优先级：设置里的逐模型覆盖 → 目录/OpenCode 报的值 →
        /// 内置表（服务商不报上下文的模型）→ 保守默认值。
        /// </summary>
        public static int ContextLimit(string model, AppSettings settings = null)
        {
            string id = model?.Trim();
            if (string.IsNullOrEmpty(id)) return Core.Agent.AgentContext.DefaultContextLimit;

            int overridden = settings?.PropsFor(id)?.ContextLimit ?? 0;
            if (overridden > 0) return overridden;

            var known = Exact(id) ?? Leaf(id);
            if (known?.ContextLength > 0) return known.ContextLength;

            int builtin = KnownContext.Lookup(id);
            return builtin > 0 ? builtin : Core.Agent.AgentContext.DefaultContextLimit;
        }

        private static ModelInfo Exact(string id)
            => Items.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase))
               ?? OpenCodeHub.Models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// 服务商可能只给不带厂商前缀的 id（TeamRouter 的 glm-5.3-flash 在目录里是 z-ai/glm-5.3-flash），
        /// 精确匹配不到时按最后一段再找一次；名字下有多条不同来源就不猜，免得串台。
        /// </summary>
        private static ModelInfo Leaf(string id)
            => UniqueLeaf(Items, id) ?? UniqueLeaf(OpenCodeHub.Models, id);

        private static ModelInfo UniqueLeaf(IReadOnlyList<ModelInfo> models, string id)
        {
            string leaf = id[(id.LastIndexOf('/') + 1)..];
            if (leaf.Length == 0) return null;
            ModelInfo found = null;
            foreach (var model in models)
            {
                string candidate = model?.Id;
                if (string.IsNullOrEmpty(candidate)) continue;
                int slash = candidate.LastIndexOf('/');
                if (slash < 0
                    || !string.Equals(candidate[(slash + 1)..], leaf, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (found != null) return null;
                found = model;
            }
            return found;
        }
    }
}
