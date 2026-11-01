using System.Diagnostics;
using System.Text;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Shell
{
    /// <summary>
    /// 壳的进程监管：拉起 A/I、盯退出、按防抖重启、处理更新交接、退出时收摊。
    /// 壳是唯一 spawner——A/I 自己不会拉起对方，避免互相拉起成环；
    /// 更新也只是「写下交接单然后退出」，换不换、换成什么由壳决定。
    /// </summary>
    public sealed class Supervisor : IDisposable
    {
        private readonly Dictionary<string, Process> _children = new();
        private readonly Dictionary<string, string> _exePaths = new();
        private readonly HashSet<string> _updating = new();
        private readonly RestartGuard _guard = new(3, TimeSpan.FromMinutes(1));
        private readonly RestartGuard _updates = new(2, TimeSpan.FromMinutes(10));
        private readonly object _lock = new();
        private readonly Action<string> _log;
        private readonly VersionStore _store;
        private bool _stopping;

        public Supervisor(Action<string> log = null, VersionStore store = null)
        {
            _log = log ?? Console.WriteLine;
            _store = store ?? new VersionStore();
        }

        /// <summary>监督日志同时进 logs/shell.log：GUI 壳没有控制台，出事只能靠文件。</summary>
        private void Log(string message)
        {
            Capture(RoleLog("shell"), message);
            try { _log(message); } catch { }
        }

        public RuntimeInfo Ensure(string role, string exePath, TimeSpan? wait = null)
        {
            _exePaths[role] = exePath;

            var existing = RuntimeInfo.TryLoad(AppPaths.RuntimeFile(role));
            if (existing is { IsAlive: true })
            {
                Log($"[shell] {role} 已在运行（pid {existing.Pid}，{existing.Bus}）");
                return existing;
            }

            RuntimeInfo.RemoveStale(AppPaths.RuntimeFile(role));
            return Spawn(role, exePath, wait ?? TimeSpan.FromSeconds(15));
        }

        public void StopAll()
        {
            _stopping = true;

            List<KeyValuePair<string, Process>> snapshot;
            lock (_lock) snapshot = _children.ToList();

            string key = KeyFile.ReadOrCreate(AppPaths.BusKeyPath);
            foreach (var (role, process) in snapshot)
            {
                try
                {
                    var info = RuntimeInfo.TryLoad(AppPaths.RuntimeFile(role));
                    if (info is { IsAlive: true })
                        using (var client = new BusClient(info.Bus, key))
                            client.ShutdownAsync("shell exit").Wait(2000);
                }
                catch (Exception ex)
                {
                    Log($"[shell] {role} 优雅退出失败：{ex.Message}");
                }
            }

            foreach (var (role, process) in snapshot)
            {
                try
                {
                    if (!process.WaitForExit(3000)) process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    Log($"[shell] {role} 结束失败：{ex.Message}");
                }
            }

            lock (_lock) _children.Clear();
        }

        private RuntimeInfo Spawn(string role, string exePath, TimeSpan wait)
        {
            if (!File.Exists(exePath))
                throw new FileNotFoundException($"找不到 {role} 的可执行文件：{exePath}");

            // 坏包不交给操作系统去执行：非 PE/ELF 的文件在这里就判死，走回滚路径。
            if (!LooksRunnable(exePath))
                throw new InvalidOperationException($"不是可执行文件：{exePath}");

            var startInfo = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("--parent");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add("--version-dir");
            startInfo.ArgumentList.Add(Path.GetDirectoryName(Path.GetFullPath(exePath)) ?? "");

            RuntimeInfo.RemoveStale(AppPaths.RuntimeFile(role));

            Log($"[shell] {role} spawn: {exePath}");
            var process = Process.Start(startInfo);
            if (process == null) throw new InvalidOperationException($"无法启动 {exePath}");
            Log($"[shell] {role} spawn ok: pid {process.Id}");

            string log = RoleLog(role);
            Capture(log, $"[shell] {role} 启动 {exePath}（pid {process.Id}，{DateTime.Now:HH:mm:ss}）");
            process.OutputDataReceived += (_, e) => { if (e.Data != null) Capture(log, e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) Capture(log, e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            lock (_lock) _children[role] = process;
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => OnExited(role, process, exePath);

            var deadline = DateTime.UtcNow + wait;
            while (DateTime.UtcNow < deadline)
            {
                var info = RuntimeInfo.TryLoad(AppPaths.RuntimeFile(role));
                if (info != null && info.Pid == process.Id)
                {
                    Log($"[shell] {role} 已启动（pid {info.Pid}，{info.Bus}，{Path.GetFileName(Path.GetDirectoryName(exePath))}）");
                    return info;
                }
                if (process.HasExited) break;
                Thread.Sleep(100);
            }

            Log($"[shell] {role} 未在 {wait.TotalSeconds:0}s 内就绪（pid {process.Id}）");
            return null;
        }

        /// <summary>A/I 的 stdout/stderr 落到 logs/&lt;role&gt;.log：GUI 壳没有控制台，不留日志就只能猜。</summary>
        // shortcut: 日志不轮转，长跑会一直涨；要控体积时再加按大小截断
        private static string RoleLog(string role)
            => Path.Combine(AppPaths.DataRoot, "logs", role + ".log");

        /// <summary>PE / ELF / shebang 才当可执行文件；坏包不交给操作系统，免得卡在 Process.Start 里。</summary>
        private static bool LooksRunnable(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                var head = new byte[4];
                if (stream.Read(head, 0, head.Length) < head.Length) return false;
                if (OperatingSystem.IsWindows()) return head[0] == (byte)'M' && head[1] == (byte)'Z';
                if (head[0] == 0x7f && head[1] == (byte)'E' && head[2] == (byte)'L' && head[3] == (byte)'F') return true;
                return head[0] == (byte)'#' && head[1] == (byte)'!';
            }
            catch
            {
                return false;
            }
        }

        private static void Capture(string path, string line)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                lock (_logGate) File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        private static readonly object _logGate = new();

        private void OnExited(string role, Process process, string exePath)
        {
            Log($"[shell] {role} 进程退出（pid {process.Id}，code {SafeExitCode(process)}）");
            lock (_lock)
            {
                if (_children.TryGetValue(role, out var current) && current == process)
                    _children.Remove(role);

                // 更新流程自己在盯这个进程：起没起来由 ApplyUpdate 判断，这里不要再抢着重启
                if (_updating.Contains(role)) return;
            }

            if (_stopping) return;

            RuntimeInfo.RemoveStale(AppPaths.RuntimeFile(role));

            var update = UpdateRequest.TryLoad(role);
            if (update != null)
            {
                ApplyUpdate(role, exePath, update);
                return;
            }

            if (!_guard.Allow(role))
            {
                Log($"[shell] {role} 反复退出，停止自动重启（崩溃循环保护）");
                return;
            }

            Log($"[shell] {role} 退出（code {process.ExitCode}），1s 后重启");
            Task.Delay(1000).ContinueWith(_ => Restart(role, exePath));
        }

        /// <summary>
        /// 换版本：拉起交接单里的目录，10 秒内报到就记账、清交接单、剪旧目录；
        /// 起不来就退回原来的可执行文件，current.json 不动。
        /// </summary>
        private void ApplyUpdate(string role, string previousExe, UpdateRequest update)
        {
            string target = string.IsNullOrWhiteSpace(update.Dir) ? null : VersionStore.ExePath(update.Dir, role);

            if (!_updates.Allow(role))
            {
                Log($"[shell] {role} 10 分钟内更新过多次，跳过本次（{update.Version}）");
                _store.Log(role, "skipped", update.Version, update.Dir, "too many updates");
                UpdateRequest.Clear(role);
                Restart(role, previousExe);
                return;
            }

            if (target == null || !File.Exists(target))
            {
                Log($"[shell] {role} 交接单里的目录不可用：{update.Dir}，退回原版本");
                _store.Log(role, "rollback", update.Version, update.Dir, "missing executable");
                UpdateRequest.Clear(role);
                Restart(role, previousExe);
                return;
            }

            Log($"[shell] {role} 换到 {update.Version}（{update.Dir}）");
            RuntimeInfo info;
            lock (_lock) _updating.Add(role);
            try
            {
                info = TrySpawn(role, target, TimeSpan.FromSeconds(10));
            }
            finally
            {
                lock (_lock) _updating.Remove(role);
            }
            if (info != null)
            {
                _exePaths[role] = target;
                _store.Activate(role, new VersionEntry
                {
                    Version = update.Version,
                    Dir = update.Dir,
                    Sha256 = update.Sha256,
                    At = DateTimeOffset.Now.ToString("O")
                });
                _store.Log(role, "ok", update.Version, update.Dir);
                UpdateRequest.Clear(role);
                _store.Prune(role);
                return;
            }

            Log($"[shell] {role} 新版本没起来，退回原版本");
            _store.Log(role, "rollback", update.Version, update.Dir, "did not report within 10s");
            UpdateRequest.Clear(role);
            _exePaths[role] = previousExe;
            Restart(role, previousExe);
        }

        private RuntimeInfo TrySpawn(string role, string exe, TimeSpan wait)
        {
            try
            {
                return Spawn(role, exe, wait);
            }
            catch (Exception ex)
            {
                Log($"[shell] {role} 启动失败：{ex.Message}");
                return null;
            }
        }

        private static string SafeExitCode(Process process)
        {
            try { return process.ExitCode.ToString(); } catch { return "?"; }
        }

        private void Restart(string role, string exe)
        {
            if (_stopping) return;
            try
            {
                Spawn(role, exe, TimeSpan.FromSeconds(15));
            }
            catch (Exception ex)
            {
                Log($"[shell] {role} 重启失败：{ex.Message}");
            }
        }

        public void Dispose() => StopAll();
    }
}
