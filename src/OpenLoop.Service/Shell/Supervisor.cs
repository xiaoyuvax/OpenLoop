using System.Diagnostics;
using System.Text;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Runtime;
using OpenLoop.Service.Source;

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
        private readonly Dictionary<string, DateTime> _started = new();
        private readonly Dictionary<string, int> _shortLived = new();
        private static readonly TimeSpan ShortLife = TimeSpan.FromSeconds(20);
        private const int FlapLimit = 3;
        private readonly object _lock = new();
        private readonly Action<string> _log;
        private readonly VersionStore _store;
        private readonly string _runDir;
        private bool _stopping;

        public Supervisor(Action<string> log = null, VersionStore store = null, string runDir = null)
        {
            _log = log ?? Console.WriteLine;
            _store = store ?? new VersionStore();
            _runDir = string.IsNullOrWhiteSpace(runDir)
                ? Path.Combine(AppContext.BaseDirectory, "backends")
                : Path.GetFullPath(runDir);
        }

        /// <summary>
        /// 运行目录：A/I 的二进制一律从这里启动（与壳同处安装目录）；<c>versions\</c> 只是版本存档与回退源。
        /// </summary>
        public string RunDir => _runDir;

        /// <summary>
        /// 启动时清一次存档：每个角色只留「当前 + 上一版」。A/I 按登记排除正在跑与可回退的，
        /// 壳没有登记（它从安装目录跑），按最新两份留。
        /// </summary>
        public void PruneArchives()
        {
            foreach (string role in new[] { BusRoles.A, BusRoles.I, "shell" })
            {
                try { _store.Prune(role); } catch { }
            }
        }

        /// <summary>换版前把会被覆盖的文件存进 versions\&lt;role&gt;\rollback-&lt;时间&gt;，安装失败时用它还原。</summary>
        private static string BackupDir(string role)
            => Path.Combine(AppPaths.VersionsDir, role, "rollback-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        /// <summary>把存档目录覆盖进运行目录（旧文件先进备份）；返回备份目录，安装失败时交给 <see cref="ShellUpdate.Restore"/>。</summary>
        internal static string InstallToRunDir(string archive, string runDir, string backup)
        {
            ShellUpdate.Copy(archive, runDir, backup);
            return backup;
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
                Adopt(role, existing);
                return existing;
            }

            RuntimeInfo.RemoveStale(AppPaths.RuntimeFile(role));
            return Spawn(role, exePath, wait ?? TimeSpan.FromSeconds(15));
        }

        public void StopAll()
        {
            _stopping = true;

            // 只换壳：旧壳退出前写过交接标记，A/I 留在原地等新壳接手（新壳启动时发布 pid，它们改守护它）
            if (ShellHandoff.IsOwnedByCurrent())
            {
                Log("[shell] 壳交接重启：A/I 保持运行，等新壳接手");
                lock (_lock) _children.Clear();
                return;
            }

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

        /// <summary>
        /// 接手上一个壳留下的 A/I（只换壳的交接）：它们还活着就直接用，但要补上退出监护，
        /// 否则它们以后崩了没人重启。
        /// </summary>
        private void Adopt(string role, RuntimeInfo info)
        {
            lock (_lock)
            {
                if (_children.ContainsKey(role)) return;
            }

            string exe = _exePaths.TryGetValue(role, out var known) ? known : null;
            try
            {
                var process = Process.GetProcessById(info.Pid);
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => OnExited(role, process, exe);
                lock (_lock)
                {
                    _children[role] = process;
                    _started[role] = process.StartTime.ToUniversalTime();
                }
                Log($"[shell] {role} 已接手（pid {info.Pid}）");
            }
            catch (Exception ex)
            {
                Log($"[shell] {role} 接手失败：{ex.Message}");
            }
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
                // 壳是 GUI 进程，子进程是控制台程序：不隐藏就会各弹一个黑窗口，用户随手关掉就砸了 Worker
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("--parent");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add("--version-dir");
            startInfo.ArgumentList.Add(Path.GetDirectoryName(Path.GetFullPath(exePath)) ?? "");

            // A/I 的 exe 在 <安装根>\worker 或 versions\<角色>\ 下，自己推不出源码根；
            // 由壳把这一份源码根传下去，避免它们算成 <exe 目录>\src\openloop
            startInfo.Environment["OPENLOOP_SOURCE"] = SourceSync.SourceRoot;

            RuntimeInfo.RemoveStale(AppPaths.RuntimeFile(role));

            Log($"[shell] {role} spawn: {exePath}");
            var process = Process.Start(startInfo);
            if (process == null) throw new InvalidOperationException($"无法启动 {exePath}");
            Log($"[shell] {role} spawn ok: pid {process.Id}");
            lock (_lock) _started[role] = DateTime.UtcNow;

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

            Log(process.HasExited
                ? $"[shell] {role} 没起来就退出（pid {process.Id}，code {SafeExitCode(process)}）"
                : $"[shell] {role} 未在 {wait.TotalSeconds:0}s 内就绪（pid {process.Id}）");
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

            // 起来就退（20 秒内、非 0 退出）连算三次：判这个版本坏了，回退到换版前的目录。
            if (CountsAsCrash(role, process) && RollbackToPrevious(role)) return;

            if (!_guard.Allow(role))
            {
                Log($"[shell] {role} 反复退出，停止自动重启（崩溃循环保护）");
                return;
            }

            Log($"[shell] {role} 退出（code {process.ExitCode}），1s 后重启");
            Task.Delay(1000).ContinueWith(_ => Restart(role, exePath));
        }

        /// <summary>短命且非正常退出算一次「坏包」；连到 <see cref="FlapLimit"/> 次就回退。</summary>
        private bool CountsAsCrash(string role, Process process)
        {
            DateTime started;
            lock (_lock)
            {
                if (!_started.TryGetValue(role, out started)) return false;
            }

            bool shortLived = DateTime.UtcNow - started < ShortLife;
            bool failed = !string.Equals(SafeExitCode(process), "0", StringComparison.Ordinal);
            if (!shortLived || !failed)
            {
                lock (_lock) _shortLived[role] = 0;
                return false;
            }

            int count;
            lock (_lock)
            {
                count = _shortLived.TryGetValue(role, out int previous) ? previous + 1 : 1;
                _shortLived[role] = count;
            }
            Log($"[shell] {role} 起来 {(DateTime.UtcNow - started).TotalSeconds:0.0}s 就退（第 {count} 次）");
            return count >= FlapLimit;
        }

        /// <summary>
        /// 回退到上一个好版本：把它的存档覆盖回 backends，再重新登记成 current 并拉起；
        /// 没有可回退的就返回 false 交给防抖。
        /// </summary>
        private bool RollbackToPrevious(string role)
        {
            var current = _store.Current(role);
            var previous = _store.Previous(role);
            if (previous == null || string.IsNullOrWhiteSpace(previous.Dir)) return false;

            if (!File.Exists(VersionStore.ExePath(previous.Dir, role))) return false;
            if (current?.Dir != null && string.Equals(
                    Path.GetFullPath(current.Dir).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(previous.Dir).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                InstallToRunDir(previous.Dir, _runDir, BackupDir(role));
            }
            catch (Exception ex)
            {
                Log($"[shell] {role} 回退时安装到 backends 失败：{ex.Message}");
                return false;
            }

            string exe = VersionStore.ExePath(_runDir, role);
            Log($"[shell] {role} 连续 {FlapLimit} 次起来就退，回退到上一版本 {previous.Version}（装回 backends，存档 {previous.Dir}）");
            _store.Log(role, "rollback", previous.Version, previous.Dir, "crash loop fallback");
            _store.Activate(role, previous, rememberPrevious: false);
            _store.Prune(role);
            lock (_lock)
            {
                _shortLived[role] = 0;
                _exePaths[role] = exe;
            }
            Restart(role, exe);
            return true;
        }

        /// <summary>
        /// 换版本：把交接单里的存档覆盖进 backends，再从 backends 拉起；10 秒内报到就记账、清交接单、剪旧存档；
        /// 起不来就还原备份并拉起原版本，current.json 不动。
        /// </summary>
        private void ApplyUpdate(string role, string previousExe, UpdateRequest update)
        {
            if (!_updates.Allow(role))
            {
                Log($"[shell] {role} 10 分钟内更新过多次，跳过本次（{update.Version}）");
                _store.Log(role, "skipped", update.Version, update.Dir, "too many updates");
                GiveUp(role, previousExe, null);
                return;
            }

            bool archiveOk = !string.IsNullOrWhiteSpace(update.Dir) &&
                             File.Exists(VersionStore.ExePath(update.Dir, role));
            if (!archiveOk)
            {
                Log($"[shell] {role} 交接单里的目录不可用：{update.Dir}，退回原版本");
                _store.Log(role, "rollback", update.Version, update.Dir, "missing executable");
                GiveUp(role, previousExe, null);
                return;
            }

            string backup = BackupDir(role);
            try
            {
                // 旧进程已经退出（交接单是它临死前写的），这里覆盖 backends 不会撞文件锁
                InstallToRunDir(update.Dir, _runDir, backup);
            }
            catch (Exception ex)
            {
                Log($"[shell] {role} 安装到 backends 失败：{ex.Message}，退回原版本");
                _store.Log(role, "rollback", update.Version, update.Dir, "install failed: " + ex.Message);
                GiveUp(role, previousExe, backup);
                return;
            }

            string target = VersionStore.ExePath(_runDir, role);
            Log($"[shell] {role} 换到 {update.Version}（装进 backends，存档 {update.Dir}）");
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
            GiveUp(role, previousExe, backup);
        }

        /// <summary>
        /// 换版失败收尾：清交接单 → 按备份还原 → 剪掉失败留在 versions 里的多余目录 → 拉起原版本。
        /// 剪枝不能只在成功时做，否则每次失败都会多留一份（版本目录只增不减）。
        /// </summary>
        private void GiveUp(string role, string previousExe, string backup)
        {
            UpdateRequest.Clear(role);
            if (!string.IsNullOrWhiteSpace(backup))
            {
                try { ShellUpdate.Restore(backup, _runDir); } catch { }
            }
            _store.Prune(role);
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
