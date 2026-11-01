using OpenLoop.Service.Runtime;
using Xunit;

namespace OpenLoop.Service.Tests
{
    public class RuntimeInfoTests
    {
        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "openloop-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void Save_and_load_round_trip()
        {
            string path = Path.Combine(NewTempDir(), "a.json");
            var info = new RuntimeInfo
            {
                Role = "a",
                Pid = Environment.ProcessId,
                Version = "0.1.0",
                Protocol = 1,
                Bus = "127.0.0.1:19180",
                StartedAt = DateTimeOffset.Now.ToString("O")
            };

            info.Save(path);
            var loaded = RuntimeInfo.TryLoad(path);

            Assert.NotNull(loaded);
            Assert.Equal("a", loaded.Role);
            Assert.Equal(info.Bus, loaded.Bus);
            Assert.True(loaded.IsAlive);
        }

        [Fact]
        public void Dead_pid_is_not_alive_and_stale_file_is_removed()
        {
            string path = Path.Combine(NewTempDir(), "i.json");
            var info = new RuntimeInfo { Role = "i", Pid = int.MaxValue, StartedAt = "" };
            info.Save(path);

            Assert.False(RuntimeInfo.TryLoad(path).IsAlive);

            RuntimeInfo.RemoveStale(path);
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void RemoveOwn_keeps_the_file_of_a_successor()
        {
            string path = Path.Combine(NewTempDir(), "a.json");
            var successor = new RuntimeInfo { Role = "a", Pid = Environment.ProcessId };
            successor.Save(path);

            var predecessor = new RuntimeInfo { Role = "a", Pid = int.MaxValue };
            predecessor.RemoveOwn(path);

            Assert.True(File.Exists(path));
            Assert.Equal(Environment.ProcessId, RuntimeInfo.TryLoad(path).Pid);
        }
    }
}
