namespace OpenLoop.App
{
    /// <summary>进程内模型目录。启动时刷新一次，所有会话共享。</summary>
    internal static class ModelCatalog
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
    }
}
