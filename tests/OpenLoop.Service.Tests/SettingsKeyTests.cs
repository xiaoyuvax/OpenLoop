using System.Text;
using OpenLoop.Service.Config;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>Unix 没有 DPAPI：密钥以 <c>plain:</c> 前缀落盘，读取端两个平台都要认。</summary>
    public class SettingsKeyTests
    {
        [Fact]
        public void Plain_prefixed_key_is_decoded_on_any_platform()
        {
            string encoded = "plain:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("sk-test-123"));

            Assert.Equal("sk-test-123", AppSettings.Unprotect(encoded));
        }

        [Fact]
        public void Garbage_and_empty_values_decode_to_empty()
        {
            Assert.Equal("", AppSettings.Unprotect(""));
            Assert.Equal("", AppSettings.Unprotect(null));
            Assert.Equal("", AppSettings.Unprotect("not-base64"));
            Assert.Equal("", AppSettings.Unprotect("plain:!!!"));
            Assert.Equal("", AppSettings.Protect(""));
        }
    }
}
