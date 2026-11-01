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

        /// <summary>上下文上限：目录里查得到就用模型自己的，否则用默认值。</summary>
        public static int ContextLimit(string model)
        {
            string id = model?.Trim();
            if (string.IsNullOrEmpty(id)) return Core.Agent.AgentContext.DefaultContextLimit;

            var known = Items.FirstOrDefault(m =>
                            string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase))
                        ?? OpenCodeHub.Models.FirstOrDefault(m =>
                            string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
            return known?.ContextLength > 0 ? known.ContextLength : Core.Agent.AgentContext.DefaultContextLimit;
        }
    }
}
