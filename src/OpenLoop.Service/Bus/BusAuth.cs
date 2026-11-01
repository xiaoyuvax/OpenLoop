using System.Security.Cryptography;
using System.Text;
using Wima.Web;

namespace OpenLoop.Service.Bus
{
    /// <summary>
    /// bus.key 校验。/bus/* 与 /api/* 共用同一把 key。
    /// 浏览器没法加请求头，所以 /api/* 另认 <c>?key=</c> 换来的 HttpOnly Cookie；/bus/* 不认（防 CSRF）。
    /// </summary>
    public static class BusAuth
    {
        public static bool IsAuthorized(WebRequest req, string key, bool allowCookie = false)
        {
            if (req?.Headers == null || string.IsNullOrEmpty(key)) return false;

            string supplied = req.Headers[BusProtocol.KeyHeader];
            if (!string.IsNullOrEmpty(supplied)) return Same(supplied, key);

            return allowCookie && Same(Cookie(req, Web.WebUiHandler.CookieName), key);
        }

        public static bool Same(string supplied, string key) =>
            !string.IsNullOrEmpty(supplied) && !string.IsNullOrEmpty(key) &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(key));

        public static string Cookie(WebRequest req, string name)
        {
            string header = req?.Headers?["Cookie"];
            if (string.IsNullOrEmpty(header)) return null;

            foreach (string part in header.Split(';'))
            {
                string item = part.Trim();
                int eq = item.IndexOf('=');
                if (eq <= 0) continue;
                if (item[..eq].Trim() == name) return item[(eq + 1)..].Trim();
            }
            return null;
        }
    }
}
