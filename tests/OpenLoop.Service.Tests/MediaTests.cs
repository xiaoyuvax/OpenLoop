using System.Text;
using OpenLoop.Service.Media;
using Xunit;

namespace OpenLoop.Service.Tests
{
    public class MediaTests
    {
        [Fact]
        public void EditModelIsFoundBySameFamilyPrefix()
        {
            string[] available = ["seedream-v5-pro", "seedream-v5-pro-edit", "gemini-3-pro-image"];
            Assert.Equal("seedream-v5-pro-edit", MediaModels.ResolveEditModel("seedream-v5-pro", available));
            Assert.Equal("seedream-v5-pro-edit", MediaModels.ResolveEditModel("seedream-v5-pro-edit", available));
        }

        [Fact]
        public void UnifiedImageModelHandlesEditItself()
            => Assert.Equal("gemini-3-pro-image",
                MediaModels.ResolveEditModel("gemini-3-pro-image", ["gemini-3-pro-image"]));

        [Fact]
        public void TextAndImageModelsHaveDifferentAttachmentLimits()
        {
            Assert.Equal(8, MediaModels.AttachmentLimit("deepseek-v4-flash"));
            Assert.Equal(4, MediaModels.AttachmentLimit("gemini-3-pro-image"));
        }

        [Fact]
        public void AttachmentsAreSavedAndCapped()
        {
            string root = Path.Combine(Path.GetTempPath(), "openloop-media-" + Guid.NewGuid().ToString("N"));
            try
            {
                var payload = new AttachmentPayload { Name = "note.txt", Data = Convert.ToBase64String(Encoding.UTF8.GetBytes("hi")) };
                var saved = Attachments.Save(root, "s1", [payload], 1, out string error);
                Assert.Null(error);
                Assert.Single(saved);
                Assert.True(File.Exists(saved[0]));

                var tooMany = Attachments.Save(root, "s1", [payload, payload], 1, out error);
                Assert.Null(tooMany);
                Assert.Contains("最多 1 个", error);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Fact]
        public void AttachmentFilesAreCopiedAndCapped()
        {
            string root = Path.Combine(Path.GetTempPath(), "openloop-attach-" + Guid.NewGuid().ToString("N"));
            string source = Path.Combine(root, "clip.mp4");
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllBytes(source, new byte[1024]);

                var saved = Attachments.SaveFiles(Path.Combine(root, "ws"), "s1", [source], 1, out string error);
                Assert.Null(error);
                Assert.Equal(1024, new FileInfo(Assert.Single(saved)).Length);
                Assert.Equal("clip.mp4", Path.GetFileName(Assert.Single(saved)));

                var again = Attachments.SaveFiles(Path.Combine(root, "ws"), "s1", [source], 1, out error);
                Assert.Equal("clip_1.mp4", Path.GetFileName(Assert.Single(again)));

                Assert.Null(Attachments.SaveFiles(Path.Combine(root, "ws"), "s1", [source, source], 1, out error));
                Assert.Contains("最多 1 个", error);

                Assert.Null(Attachments.SaveFiles(Path.Combine(root, "ws"), "s1", [Path.Combine(root, "nope.mp4")], 1, out error));
                Assert.Contains("附件不存在", error);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Fact]
        public void AttachmentTotalsAreCapped()
        {
            Assert.Null(Attachments.CheckSize([("a.txt", 1024L), ("b.txt", 2048L)]));
            Assert.Contains("附件过大", Attachments.CheckSize([("big.mp4", Attachments.MaxBytes + 1)]));
            Assert.Contains("附件总量过大", Attachments.CheckSize(
            [
                ("a.mp4", 40L << 20), ("b.mp4", 40L << 20),
                ("c.mp4", 40L << 20), ("d.mp4", 40L << 20)
            ]));
        }

        [Fact]
        public void ImageToolsAreInjectedOnlyWhenAnImageModelExists()
        {
            var settings = new OpenLoop.Service.Config.AppSettings
            {
                Providers =
                [
                    new OpenLoop.Service.Config.ProviderConfig
                    {
                        Name = "X", Kind = "custom",
                        Models = ["seedream-v5-pro", "deepseek-v4-flash"]
                    }
                ]
            };
            var names = ImageTools.For(Path.GetTempPath(), settings, "seedream-v5-pro", [])
                .Select(t => t.Name).ToList();
            Assert.Contains("image_generate", names);
            Assert.Contains("image_edit", names);

            Assert.Empty(ImageTools.For(Path.GetTempPath(), new OpenLoop.Service.Config.AppSettings(), "", []));
        }

        [Fact]
        public void ExifPromptIsWrittenIntoPng()
        {
            string path = Path.Combine(Path.GetTempPath(), "openloop-exif-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
                    0, 0, 0, 0, (byte)'I', (byte)'E', (byte)'N', (byte)'D',
                    0xAE, 0x42, 0x60, 0x82];
                File.WriteAllBytes(path, png);

                Assert.True(ImageExif.Write(path, "白鹿在雪地", "gemini-3-pro-image"));

                byte[] result = File.ReadAllBytes(path);
                Assert.True(Has(result, Encoding.ASCII.GetBytes("eXIf")));
                Assert.True(Has(result, Encoding.UTF8.GetBytes("白鹿在雪地")));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void ExifPromptIsWrittenIntoJpeg()
        {
            string path = Path.Combine(Path.GetTempPath(), "openloop-exif-" + Guid.NewGuid().ToString("N") + ".jpg");
            try
            {
                File.WriteAllBytes(path, [0xFF, 0xD8, 0xFF, 0xD9]);
                Assert.True(ImageExif.Write(path, "雪地白鹿", "seedream-v5-pro"));

                byte[] result = File.ReadAllBytes(path);
                Assert.True(Has(result, Encoding.ASCII.GetBytes("Exif")));
                Assert.True(Has(result, Encoding.UTF8.GetBytes("雪地白鹿")));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static bool Has(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                if (match) return true;
            }
            return false;
        }
    }
}
