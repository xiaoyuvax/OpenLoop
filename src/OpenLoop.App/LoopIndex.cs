using System.Text.Json;
using OpenLoop.Service.Runtime;

namespace OpenLoop.App
{
    internal sealed record LoopRef(string Workspace, DateTimeOffset UpdatedAt);

    /// <summary>
    /// Loop 会话小账本：对话正文仍按「工作区|loop」存，这里只记「哪些项目有 Loop 会话」，
    /// 供项目列表底部的 [Loop] 页列出全部。
    /// </summary>
    internal static class LoopIndex
    {
        private static string FilePath => Path.Combine(AppPaths.DataRoot, "loops.json");

        public static void Touch(string workspace)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return;
            try
            {
                var items = List().Where(i => !Paths.Same(i.Workspace, workspace)).ToList();
                items.Add(new LoopRef(workspace, DateTimeOffset.Now));
                Directory.CreateDirectory(AppPaths.DataRoot);
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp,
                    JsonSerializer.Serialize(items.OrderByDescending(i => i.UpdatedAt).ToList()),
                    new System.Text.UTF8Encoding(false));
                File.Move(temp, FilePath, true);
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
    }
}
