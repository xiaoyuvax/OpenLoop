using System.Globalization;
using System.Text.Json;
using OpenLoop.Service.Runtime;

namespace OpenLoop.App
{
    /// <summary>
    /// 一条 Loop 会话。Id 是对话文件的 kind：旧索引没有 Id，当时每个工作区只存一份（kind="loop"）。
    /// </summary>
    internal sealed record LoopRef(string Workspace, DateTimeOffset UpdatedAt, string Id = null, string Title = null,
        int Steps = 0)
    {
        /// <summary>标题只是建会话时的占位（「反馈 15:19」）：自动总结可以覆盖，不算已有标题。</summary>
        public bool Seed { get; init; }

        public const string LegacyId = "loop";

        [System.Text.Json.Serialization.JsonIgnore]
        public string SessionId => string.IsNullOrWhiteSpace(Id) ? LegacyId : Id;
    }

    /// <summary>
    /// Loop 会话小账本：对话正文仍按「工作区|会话 id」存，这里只记有哪些 Loop 会话，
    /// 供项目列表底部的 [Loop] 页列出全部。同一个工作区可以有多条（新建会话 = 新 id = 新对话）。
    /// </summary>
    internal static class LoopIndex
    {
        private static string FilePath => Path.Combine(AppPaths.DataRoot, "loops.json");

        public static void Touch(string workspace, string sessionId, int? steps = null)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return;
            string id = string.IsNullOrWhiteSpace(sessionId) ? LoopRef.LegacyId : sessionId;
            try
            {
                var items = List();
                var existing = items.FirstOrDefault(i => Same(i, workspace, id));
                items = items.Where(i => !Same(i, workspace, id)).ToList();
                items.Add(new LoopRef(workspace, DateTimeOffset.Now, id, existing?.Title,
                    steps ?? existing?.Steps ?? 0) { Seed = existing?.Seed ?? false });
                Write(items);
            }
            catch
            {
            }
        }

        /// <summary>旧小账本没有步数：每份对话文件读一次取最后一轮步数写回（不动最后活动时间）。</summary>
        public static int BackfillSteps()
        {
            var items = List();
            int filled = 0;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Steps > 0 || string.IsNullOrWhiteSpace(item.Workspace)) continue;
                int steps = OpenLoop.Service.Storage.ChatHistory.Read(item.Workspace, item.SessionId)
                    .LastOrDefault()?.Steps ?? 0;
                if (steps <= 0) continue;
                items[i] = item with { Steps = steps };
                filled++;
            }
            if (filled > 0) Write(items);
            return filled;
        }

        /// <summary>总结出来的标题：存在小账本里，重开这个 Loop 会话时页头直接用它。</summary>
        public static string Title(string workspace, string sessionId)
        {
            string id = string.IsNullOrWhiteSpace(sessionId) ? LoopRef.LegacyId : sessionId;
            return List().FirstOrDefault(i => Same(i, workspace, id))?.Title;
        }

        /// <summary>真正总结出来的标题：有它就不再自动覆盖（新会话开跑 5 秒那次除外）。</summary>
        public static bool HasTitle(string workspace, string sessionId)
        {
            string title = Title(workspace, sessionId);
            return !string.IsNullOrWhiteSpace(title) && !IsSeedTitle(workspace, sessionId);
        }

        /// <summary>标题是建会话时的占位（「反馈 15:19」「转交 09:05」）：自动总结可以盖掉它。</summary>
        public static bool IsSeedTitle(string workspace, string sessionId)
        {
            string id = string.IsNullOrWhiteSpace(sessionId) ? LoopRef.LegacyId : sessionId;
            var item = List().FirstOrDefault(i => Same(i, workspace, id));
            return item != null && (item.Seed || LooksSeeded(item.Title));
        }

        /// <summary>旧账本没记 Seed：按形状认占位标题（短前缀 + 空格 + 时:分），只可能撞上真实标题的极小概率。</summary>
        private static bool LooksSeeded(string title)
            => title != null && title.Length > 6 && title[^6] == ' '
               && DateTime.TryParseExact(title[^5..], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        public static void Rename(string workspace, string sessionId, string title, bool seed = false)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return;
            string id = string.IsNullOrWhiteSpace(sessionId) ? LoopRef.LegacyId : sessionId;
            try
            {
                var items = List();
                var existing = items.FirstOrDefault(i => Same(i, workspace, id));
                if (existing == null)
                {
                    items.Add(new LoopRef(workspace, DateTimeOffset.Now, id, title) { Seed = seed });
                }
                else
                {
                    items[items.IndexOf(existing)] = existing with { Title = title, Seed = seed };
                }
                Write(items);
            }
            catch
            {
            }
        }

        public static void Remove(string workspace, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return;
            string id = string.IsNullOrWhiteSpace(sessionId) ? LoopRef.LegacyId : sessionId;
            try
            {
                Write(List().Where(i => !Same(i, workspace, id)).ToList());
            }
            catch
            {
            }
        }

        public static List<LoopRef> List()
        {
            try
            {
                if (!File.Exists(FilePath)) return [];
                return JsonSerializer.Deserialize<List<LoopRef>>(File.ReadAllText(FilePath))
                       ?? [];
            }
            catch
            {
                return [];
            }
        }

        private static bool Same(LoopRef item, string workspace, string sessionId)
            => Paths.Same(item.Workspace, workspace)
               && string.Equals(item.SessionId, sessionId, StringComparison.Ordinal);

        private static void Write(List<LoopRef> items)
        {
            Directory.CreateDirectory(AppPaths.DataRoot);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp,
                JsonSerializer.Serialize(items.OrderByDescending(i => i.UpdatedAt).ToList()),
                new System.Text.UTF8Encoding(false));
            File.Move(temp, FilePath, true);
        }
    }
}
