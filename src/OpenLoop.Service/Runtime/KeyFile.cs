using System.Text;

namespace OpenLoop.Service.Runtime
{
    /// <summary>
    /// 进程间共享密钥。首次以原子移动创建，其余进程读同一个文件；两边永远用同一把 key。
    /// </summary>
    public static class KeyFile
    {
        public static string ReadOrCreate(string path)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                var existing = Read(path);
                if (!string.IsNullOrEmpty(existing)) return existing;

                AppPaths.EnsureDirectory(Path.GetDirectoryName(path));
                string key = Guid.NewGuid().ToString("N");
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, key, new UTF8Encoding(false));
                try
                {
                    File.Move(temp, path);
                    return key;
                }
                catch (IOException)
                {
                    // 对端先创建成功：删自己的临时文件，下一轮读它的
                    try { File.Delete(temp); } catch { }
                    Thread.Sleep(20);
                }
            }

            string last = Read(path);
            if (!string.IsNullOrEmpty(last)) return last;
            throw new IOException("无法创建或读取 bus key：" + path);
        }

        private static string Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string text = File.ReadAllText(path).Trim();
                return text.Length == 0 ? null : text;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
