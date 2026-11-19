using System.Text;

namespace OpenLoop.Service.Media
{
    /// <summary>
    /// 生成结果的 EXIF 写入（与 VeniceMan 同契约，按最小实现）：生成提示词进 UserComment，
    /// 同时回填标准 ImageDescription，方便按主题/提示词检索。只处理 PNG / JPEG。
    /// </summary>
    public static class ImageExif
    {
        /// <summary>写入 EXIF；格式不认识或超长时返回 false（不阻断保存）。</summary>
        public static bool Write(string path, string prompt, string model)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                byte[] tiff = BuildTiff(prompt, model);
                byte[] result =
                    IsPng(bytes) ? InsertPngExif(bytes, tiff) :
                    IsJpeg(bytes) ? InsertJpegExif(bytes, tiff) : null;
                if (result == null) return false;
                File.WriteAllBytes(path, result);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPng(byte[] b)
            => b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;

        private static bool IsJpeg(byte[] b)
            => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

        private static byte[] InsertPngExif(byte[] png, byte[] tiff)
        {
            int iend = -1;
            int pos = 8;
            while (pos + 8 <= png.Length)
            {
                long len = ReadBe(png, pos);
                if (pos + 12L + len > png.Length) return null;
                if (Encoding.ASCII.GetString(png, pos + 4, 4) == "IEND") { iend = pos; break; }
                pos += (int)(12 + len);
            }
            if (iend < 0) return null;

            using var chunk = new MemoryStream();
            WriteBe(chunk, tiff.Length);
            byte[] type = Encoding.ASCII.GetBytes("eXIf");
            chunk.Write(type);
            chunk.Write(tiff);
            WriteBe(chunk, Crc32(type, tiff));

            byte[] data = chunk.ToArray();
            var result = new byte[png.Length + data.Length];
            Array.Copy(png, 0, result, 0, iend);
            Array.Copy(data, 0, result, iend, data.Length);
            Array.Copy(png, iend, result, iend + data.Length, png.Length - iend);
            return result;
        }

        private static byte[] InsertJpegExif(byte[] jpeg, byte[] tiff)
        {
            int payload = 6 + tiff.Length;
            if (payload + 2 > 65535) return null;
            var segment = new byte[4 + payload];
            segment[0] = 0xFF;
            segment[1] = 0xE1;
            segment[2] = (byte)((payload + 2) >> 8);
            segment[3] = (byte)(payload + 2);
            Encoding.ASCII.GetBytes("Exif").CopyTo(segment, 4);
            segment[8] = 0;
            segment[9] = 0;
            Array.Copy(tiff, 0, segment, 10, tiff.Length);

            var result = new byte[jpeg.Length + segment.Length];
            Array.Copy(jpeg, 0, result, 0, 2);
            Array.Copy(segment, 0, result, 2, segment.Length);
            Array.Copy(jpeg, 2, result, 2 + segment.Length, jpeg.Length - 2);
            return result;
        }

        /// <summary>最小 TIFF/EXIF：ImageDescription(270) + Software(305) + DateTime(306) + UserComment(37510)。</summary>
        private static byte[] BuildTiff(string prompt, string model)
        {
            string text = string.IsNullOrWhiteSpace(prompt) ? "" : prompt.Trim();
            string json = "{\"generationParams\":{\"prompt\":"
                + System.Text.Json.JsonSerializer.Serialize(text)
                + ",\"modelName\":" + System.Text.Json.JsonSerializer.Serialize(model ?? "") + "}}";
            byte[] comment = Encoding.ASCII.GetBytes("UNICODE\0")
                .Concat(Encoding.UTF8.GetBytes(json)).ToArray();
            byte[] desc = Encoding.UTF8.GetBytes(text + "\0");
            byte[] software = Encoding.UTF8.GetBytes("OpenLoop\0");
            byte[] date = Encoding.ASCII.GetBytes(DateTime.Now.ToString("yyyy:MM:dd HH:mm:ss") + "\0");

            var entries = new (ushort Tag, ushort Type, uint Count, byte[] Data)[]
            {
                (270, 2, (uint)desc.Length, desc),
                (305, 2, (uint)software.Length, software),
                (306, 2, (uint)date.Length, date),
                (37510, 7, (uint)comment.Length, comment),
            };

            using var ms = new MemoryStream();
            ms.Write([0x49, 0x49, 42, 0]);
            WriteLe(ms, 8);
            WriteLe(ms, (ushort)entries.Length);

            int entriesEnd = 8 + 2 + entries.Length * 12 + 4;
            int dataOffset = entriesEnd;
            var payloads = new List<(int Offset, byte[] Data)>();
            foreach (var entry in entries)
            {
                WriteLe(ms, entry.Tag);
                WriteLe(ms, entry.Type);
                WriteLe(ms, entry.Count);
                if (entry.Data.Length <= 4)
                {
                    var inline = new byte[4];
                    Array.Copy(entry.Data, inline, entry.Data.Length);
                    ms.Write(inline);
                }
                else
                {
                    if (dataOffset % 2 == 1) dataOffset++;
                    WriteLe(ms, (uint)dataOffset);
                    payloads.Add((dataOffset, entry.Data));
                    dataOffset += entry.Data.Length;
                }
            }
            WriteLe(ms, 0); // 无下一个 IFD

            long position = ms.Position;
            foreach (var (offset, data) in payloads)
            {
                if (offset > position) ms.Write(new byte[offset - position]);
                ms.Write(data);
                position = ms.Position;
            }
            return ms.ToArray();
        }

        private static void WriteLe(Stream s, ushort value)
        {
            s.WriteByte((byte)value);
            s.WriteByte((byte)(value >> 8));
        }

        private static void WriteLe(Stream s, uint value)
        {
            s.WriteByte((byte)value);
            s.WriteByte((byte)(value >> 8));
            s.WriteByte((byte)(value >> 16));
            s.WriteByte((byte)(value >> 24));
        }

        private static long ReadBe(byte[] b, int pos)
            => ((long)b[pos] << 24) | ((long)b[pos + 1] << 16) | ((long)b[pos + 2] << 8) | b[pos + 3];

        private static void WriteBe(Stream s, long value)
        {
            s.WriteByte((byte)(value >> 24));
            s.WriteByte((byte)(value >> 16));
            s.WriteByte((byte)(value >> 8));
            s.WriteByte((byte)value);
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] type, byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in type) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }
    }
}
