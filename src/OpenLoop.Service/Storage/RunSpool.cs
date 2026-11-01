using System.Text.Json;
using OpenLoop.Core.Store;
using OpenLoop.Service.Bus;

namespace OpenLoop.Service.Storage
{
    /// <summary>
    /// Worker 运行证据的投递箱（<c>.openloop/spool/&lt;run id&gt;.json</c>）：A 写、I 收。
    /// 账本只有 I 一个写者，A 不再直接 <c>AppendRun</c>；按 run id 幂等，重复投递不会重复记。
    /// </summary>
    public static class RunSpool
    {
        public static string Dir(string workspace) => Path.Combine(workspace ?? "", ".openloop", "spool");

        public static string Write(string workspace, RunRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.Id))
                throw new ArgumentException("运行记录必须有 id", nameof(record));

            string dir = Dir(workspace);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, record.Id + ".json");
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(record, BusProtocol.Json));
            File.Move(temp, path, true);
            return path;
        }

        /// <summary>还没被 I 收走的运行记录（坏文件会被清掉，不阻塞队列）。</summary>
        public static IReadOnlyList<(string Path, RunRecord Record)> Pending(string workspace)
        {
            var list = new List<(string, RunRecord)>();
            string dir = Dir(workspace);
            if (!Directory.Exists(dir)) return list;

            foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                RunRecord record = null;
                try
                {
                    record = JsonSerializer.Deserialize<RunRecord>(File.ReadAllBytes(file), BusProtocol.Json);
                }
                catch (JsonException)
                {
                }

                if (record == null || string.IsNullOrWhiteSpace(record.Id))
                {
                    Remove(file);
                    continue;
                }
                list.Add((file, record));
            }
            return list;
        }

        public static void Remove(string path)
        {
            try { File.Delete(path); } catch { }
        }
    }
}
