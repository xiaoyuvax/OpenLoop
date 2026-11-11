using OpenLoop.Service.Bus;
using OpenLoop.Service.Shell;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>版本目录清单：包自己声明协议号与整目录哈希，stage 侧据此核对。</summary>
    public class VersionManifestTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "openloop-manifest-" + Guid.NewGuid().ToString("N"));

        public VersionManifestTests()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(VersionStore.ExePath(_dir, "a"), "stub");
            File.WriteAllText(Path.Combine(_dir, "OpenLoop.Service.dll"), "payload");
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void Write_then_read_round_trips_role_version_and_protocol()
        {
            var written = VersionManifest.Write(_dir, "a", "1.2.3", BusProtocol.Version);

            var read = VersionManifest.TryRead(_dir);

            Assert.NotNull(read);
            Assert.Equal("a", read.Role);
            Assert.Equal("1.2.3", read.Version);
            Assert.Equal(BusProtocol.Version, read.Protocol);
            Assert.Equal(written.FilesSha256, read.FilesSha256);
            Assert.False(string.IsNullOrWhiteSpace(read.ExeSha256));
        }

        [Fact]
        public void Files_hash_changes_when_a_payload_file_is_tampered()
        {
            var manifest = VersionManifest.Write(_dir, "a", "1.2.3", BusProtocol.Version);
            File.WriteAllText(Path.Combine(_dir, "OpenLoop.Service.dll"), "tampered");

            Assert.NotEqual(manifest.FilesSha256, VersionManifest.ComputeFilesSha256(_dir));
        }

        [Fact]
        public void Manifest_and_source_ref_do_not_count_as_payload()
        {
            string before = VersionManifest.ComputeFilesSha256(_dir);
            VersionManifest.Write(_dir, "a", "1.2.3", BusProtocol.Version);
            File.WriteAllText(Path.Combine(_dir, "source-ref.txt"), "ref refs/openloop/version/a/1.2.3");

            Assert.Equal(before, VersionManifest.ComputeFilesSha256(_dir));
        }

        [Fact]
        public void Missing_manifest_is_not_an_error()
            => Assert.Null(VersionManifest.TryRead(_dir));
    }
}
