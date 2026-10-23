namespace OpenLoop.Core.Safety
{
    /// <summary>
    /// write_file 的撤销队列：每次写入前记录旧内容；按相反顺序恢复。
    /// 只覆盖 write_file；run_command/技能脚本的改动由 git 快照兜底。
    /// </summary>
    public sealed class FileUndoLog
    {
        private readonly object _gate = new();
        private readonly List<Entry> _entries = [];

        public int Count
        {
            get { lock (_gate) return _entries.Count; }
        }

        public void Record(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return;
            bool existed = false;
            byte[] content = null;
            try
            {
                if (File.Exists(fullPath))
                {
                    content = File.ReadAllBytes(fullPath);
                    existed = true;
                }
            }
            catch
            {
                return;
            }

            lock (_gate) _entries.Add(new Entry(fullPath, existed, content));
        }

        public (int Restored, List<string> Errors) UndoAll()
        {
            Entry[] entries;
            lock (_gate)
            {
                entries = _entries.ToArray();
                _entries.Clear();
            }

            int restored = 0;
            var errors = new List<string>();
            for (int i = entries.Length - 1; i >= 0; i--)
            {
                try
                {
                    var entry = entries[i];
                    if (entry.Existed)
                    {
                        string dir = Path.GetDirectoryName(entry.Path);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        File.WriteAllBytes(entry.Path, entry.Content ?? []);
                    }
                    else if (File.Exists(entry.Path))
                    {
                        File.Delete(entry.Path);
                    }
                    restored++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{entries[i].Path}: {ex.Message}");
                }
            }
            return (restored, errors);
        }

        private sealed record Entry(string Path, bool Existed, byte[] Content);
    }
}
