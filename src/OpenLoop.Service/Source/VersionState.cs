using System.Text.Json;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Source
{
    /// <summary>一个节点上次「成功发布时」的号与指纹；这是升版判断的基准，不是 git 提交。</summary>
    public sealed class NodeState
    {
        public string Version { get; set; }
        public string Fingerprint { get; set; }
        public List<NodeDep> Deps { get; set; } = [];
        public string At { get; set; }
    }

    /// <summary>
    /// 运行态版本基准：<c>versions/state.json</c>，只有 I 写。
    /// 计划阶段把本轮指纹写成 <c>state.pending.json</c>，整轮成功后由 <see cref="Promote"/> 提升；
    /// 失败不提升，下一轮按同样的指纹重试同一个号（号不会连跳）。
    /// </summary>
    public static class VersionState
    {
        public static string FilePath => Path.Combine(AppPaths.VersionsDir, "state.json");
        public static string PendingPath => Path.Combine(AppPaths.VersionsDir, "state.pending.json");

        /// <summary>读基准；文件不存在或读不动返回 null（调用方按「全部污染」处理）。</summary>
        public static Dictionary<string, NodeState> Read() => ReadFile(FilePath);

        public static Dictionary<string, NodeState> ReadFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var state = JsonSerializer.Deserialize<Dictionary<string, NodeState>>(
                    File.ReadAllBytes(path), BusProtocol.Json);
                return state == null || state.Count == 0 ? null : state;
            }
            catch
            {
                return null;
            }
        }

        public static void SavePending(Dictionary<string, NodeState> state) => WriteFile(PendingPath, state);

        /// <summary>整轮成功：pending → state.json。没有 pending 时什么也不做。</summary>
        public static bool Promote()
        {
            try
            {
                if (!File.Exists(PendingPath)) return false;
                Directory.CreateDirectory(AppPaths.VersionsDir);
                File.Move(PendingPath, FilePath, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static void WriteFile(string path, Dictionary<string, NodeState> state)
        {
            try
            {
                if (state == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temp = path + ".tmp";
                File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(state, BusProtocol.Json));
                File.Move(temp, path, true);
            }
            catch
            {
            }
        }
    }
}
