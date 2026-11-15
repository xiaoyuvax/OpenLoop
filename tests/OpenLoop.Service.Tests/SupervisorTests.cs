using OpenLoop.Service.Shell;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>A/I 的运行位置约定：backends 是运行目录，versions 只是存档。</summary>
    public sealed class SupervisorTests : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "ol-supervisor-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void Install_to_run_dir_overwrites_backends_and_backs_up_the_old_files()
        {
            string archive = Path.Combine(_root, "versions", "a", "0.1.10-abc12345");
            string run = Path.Combine(_root, "backends");
            string backup = Path.Combine(_root, "versions", "a", "rollback-1");
            Directory.CreateDirectory(archive);
            Directory.CreateDirectory(run);
            File.WriteAllText(Path.Combine(archive, "OpenLoop.A.exe"), "new");
            File.WriteAllText(Path.Combine(run, "OpenLoop.A.exe"), "old");

            Supervisor.InstallToRunDir(archive, run, backup);

            Assert.Equal("new", File.ReadAllText(Path.Combine(run, "OpenLoop.A.exe")));
            Assert.Equal("old", File.ReadAllText(Path.Combine(backup, "OpenLoop.A.exe")));

            // 新版本起不来时要能靠备份退回原来那份
            ShellUpdate.Restore(backup, run);
            Assert.Equal("old", File.ReadAllText(Path.Combine(run, "OpenLoop.A.exe")));
        }

        [Fact]
        public void Run_dir_defaults_to_the_install_backends_folder()
        {
            using var supervisor = new Supervisor(
                _ => { }, new VersionStore(Path.Combine(_root, "versions")));

            Assert.Equal(Path.Combine(AppContext.BaseDirectory, "backends"), supervisor.RunDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }
    }
}
