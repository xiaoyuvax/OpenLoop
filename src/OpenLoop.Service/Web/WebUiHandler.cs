using System.Text;
using Wima.Web;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Web
{
    /// <summary>
    /// 浏览器界面：一套 <c>wwwroot/</c>，A 与 I 各宿主一份。
    /// 鉴权：<c>?key=</c> 换 HttpOnly Cookie，之后靠 Cookie；接口仍认 <c>X-OpenLoop-Key</c>。
    /// </summary>
    public sealed class WebUiHandler
    {
        public const string CookieName = "openloop_key";

        private readonly string _key;
        private readonly string _root;

        public WebUiHandler(string key, string root = null)
        {
            _key = key ?? "";
            _root = Path.GetFullPath(root ?? AppPaths.WwwRoot);
        }

        public bool Handle(WebRequest req)
        {
            string path = req?.Path ?? "";
            if (req == null || path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/bus", StringComparison.OrdinalIgnoreCase))
                return false;

            string queryKey = req.Queries?["key"];
            bool viaQuery = BusAuth.Same(queryKey, _key);
            if (!viaQuery && !BusAuth.Same(BusAuth.Cookie(req, CookieName), _key) &&
                !BusAuth.IsAuthorized(req, _key))
            {
                req.Response.StatusCode = 401;
                req.Response.Headers.Set("Content-Type", "text/plain; charset=utf-8");
                req.Response.Buffer = Encoding.UTF8.GetBytes("未授权：在地址后加 ?key=<bus.key>，或带 X-OpenLoop-Key 请求头。");
                return true;
            }

            if (viaQuery)
                req.Response.Headers.Set("Set-Cookie", $"{CookieName}={_key}; Path=/; HttpOnly; SameSite=Strict");

            string rel = path.Trim('/');
            if (rel.Length == 0) rel = "index.html";

            string file = Resolve(rel);
            if (file == null)
            {
                req.Response.StatusCode = 404;
                req.Response.Buffer = Encoding.UTF8.GetBytes("not found");
                return true;
            }

            req.Response.Headers.Set("Content-Type", ContentType(file));
            req.Response.Headers.Set("Cache-Control", "no-cache");
            req.Response.Buffer = File.ReadAllBytes(file);
            return true;
        }

        private string Resolve(string relative)
        {
            if (relative.Contains("..", StringComparison.Ordinal)) return null;
            try
            {
                string full = Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));
                string prefix = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
                return File.Exists(full) ? full : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".ico" => "image/x-icon",
            _ => "application/octet-stream"
        };
    }
}
