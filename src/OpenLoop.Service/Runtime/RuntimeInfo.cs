using System.Diagnostics;
using System.Text.Json;
using OpenLoop.Service.Bus;

namespace OpenLoop.Service.Runtime
{
    /// <summary>
    /// 一个进程的自我描述，也是 runtime/&lt;role&gt;.json 发现文件的内容。
    /// 发现文件原子替换写入；是否还活着以 pid 为准，不信文件本身。
    /// </summary>
    public sealed class RuntimeInfo
    {
        public string Role { get; set; }
        public int Pid { get; set; }
        public string Version { get; set; }
        public int Protocol { get; set; }
        public string Bus { get; set; }
        public string Web { get; set; }

        /// <summary>自己所在的版本目录（壳用 --version-dir 传进来）。</summary>
        public string Dir { get; set; }
        public string StartedAt { get; set; }
        public bool Busy { get; set; }

        public bool IsAlive
        {
            get
            {
                try
                {
                    using var process = Process.GetProcessById(Pid);
                    return !process.HasExited;
                }
                catch
                {
                    return false;
                }
            }
        }

        public void Save(string path)
        {
            AppPaths.EnsureDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(this, BusProtocol.Json));
            File.Move(temp, path, overwrite: true);
        }

        public static RuntimeInfo TryLoad(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<RuntimeInfo>(File.ReadAllBytes(path), BusProtocol.Json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>只删除仍是自己的那份，避免删掉接替者的发现文件。</summary>
        public void RemoveOwn(string path)
        {
            var current = TryLoad(path);
            if (current != null && current.Pid != Pid) return;
            try { File.Delete(path); } catch { }
        }

        /// <summary>删掉已死进程留下的发现文件；活着的不动。</summary>
        public static void RemoveStale(string path)
        {
            var current = TryLoad(path);
            if (current == null || current.IsAlive) return;
            try { File.Delete(path); } catch { }
        }
    }
}
