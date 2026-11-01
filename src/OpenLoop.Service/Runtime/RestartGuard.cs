namespace OpenLoop.Service.Runtime
{
    /// <summary>
    /// 崩溃循环保护：同一角色在窗口期内只允许有限次自动重启，超过就停手等人看。
    /// </summary>
    public sealed class RestartGuard
    {
        private readonly int _max;
        private readonly TimeSpan _window;
        private readonly Dictionary<string, List<DateTime>> _hits = new();
        private readonly object _lock = new();

        public RestartGuard(int max = 3, TimeSpan? window = null)
        {
            _max = max;
            _window = window ?? TimeSpan.FromMinutes(1);
        }

        public bool Allow(string key)
        {
            lock (_lock)
            {
                if (!_hits.TryGetValue(key, out var hits))
                {
                    hits = new List<DateTime>();
                    _hits[key] = hits;
                }

                var now = DateTime.UtcNow;
                hits.RemoveAll(t => now - t > _window);
                if (hits.Count >= _max) return false;

                hits.Add(now);
                return true;
            }
        }
    }
}
