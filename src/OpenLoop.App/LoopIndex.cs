using System.Text.Json;
using OpenLoop.Service.Runtime;

namespace OpenLoop.App
{
    /// <summary>
    /// 一条 Loop 会话。Id 是对话文件的 kind：旧索引没有 Id，当时每个工作区只存一份（kind="loop"）。
    /// </summary>
    internal sealed record LoopRef(string Workspace, DateTimeOffset UpdatedAt, string Id = null, string Title = null)
    {
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

        public static void Touch(string workspace, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return;
            string id = string.IsNullOrWhiteSpace(sessionId) ? LoopRef.LegacyId : sessionId;
            try
            {
                var items = List();
                string title = items.FirstOrDefault(i => Same(i, workspace, id))?.Title;
                items = items.Where(i => !Same(i, workspace, id)).ToList();
                items.Add(new LoopRef(workspace, DateTimeOffset.Now, id, title));
                Write(items);
            }
            catch
            {
            }
        }

        /// <summary>总结出来的标题：存在小账本里，重开这个 Loop 会话时页头直接用它。</summary>
        public static string Title(string workspace, string sessionId)
        {
            string id = string.IsNullOrWhiteSpace(sessionId) ? LoopRef.LegacyId : sessionId;
            return List().FirstOrDefault(i => Same(i, workspace, id))?.Title;
        }

        public static void Rename(string workspace, string sessionId, string title)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return;
            string id = string.IsNullOrWhiteSpace(sessionId) ? LoopRef.LegacyId : sessionId;
            try
            {
                var items = List();
                var existing = items.FirstOrDefault(i => Same(i, workspace, id));
                if (existing == null)
                {
                    items.Add(new LoopRef(workspace, DateTimeOffset.Now, id, title));
                }
                else
                {
                    items[items.IndexOf(existing)] = existing with { Title = title };
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
