using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenLoop.Service.Config;

namespace OpenLoop.Service.Media
{
    /// <summary>
    /// 生图 / 图编辑调用：OpenAI 兼容的 chat/completions（modalities 含 image），
    /// 没返回图片时对 gpt-image / dall-e 这类专用端点回退 /images/generations、/images/edits。
    /// </summary>
    public static class ImageClient
    {
        private static readonly HttpClient Http = new(new SocketsHttpHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        public sealed class Result
        {
            public byte[] Data { get; init; }
            public string Error { get; init; }
        }

        public static async Task<Result> GenerateAsync(AppSettings settings, string model, string prompt,
            IReadOnlyList<string> imagePaths, CancellationToken ct)
        {
            var sources = LoadImages(imagePaths, out string loadError);
            if (sources == null) return new Result { Error = loadError };

            var (baseUrl, key) = settings.EndpointFor(model);
            if (string.IsNullOrWhiteSpace(baseUrl)) return new Result { Error = "模型没有可用的接口地址" };

            try
            {
                string viaChat = await ChatAsync(baseUrl, key, model, prompt, sources, ct);
                if (!string.IsNullOrEmpty(viaChat)) return Decode(viaChat);
                if (LooksDedicated(model))
                {
                    string viaImages = await ImagesAsync(baseUrl, key, model, prompt, sources, ct);
                    if (!string.IsNullOrEmpty(viaImages)) return Decode(viaImages);
                }
                return new Result { Error = "接口没有返回图片（模型可能不支持生图/图编辑）" };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new Result { Error = ex.Message };
            }
        }

        private static async Task<string> ChatAsync(string baseUrl, string key, string model, string prompt,
            List<(string Mime, byte[] Data)> sources, CancellationToken ct)
        {
            var content = new List<object> { new Dictionary<string, object> { ["type"] = "text", ["text"] = prompt } };
            foreach (var (mime, data) in sources)
            {
                content.Add(new Dictionary<string, object>
                {
                    ["type"] = "image_url",
                    ["image_url"] = new Dictionary<string, object>
                    {
                        ["url"] = "data:" + mime + ";base64," + Convert.ToBase64String(data)
                    }
                });
            }

            var body = new Dictionary<string, object>
            {
                ["model"] = model,
                ["stream"] = false,
                ["modalities"] = new[] { "image", "text" },
                ["messages"] = new object[]
                {
                    new Dictionary<string, object> { ["role"] = "user", ["content"] = content }
                }
            };

            string json = await PostAsync(baseUrl.TrimEnd('/') + "/chat/completions", key, body, ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var choice in choices.EnumerateArray())
            {
                if (!choice.TryGetProperty("message", out var message)) continue;
                string data = ReadImageUrl(message);
                if (data != null) return data;
            }
            return null;
        }

        private static async Task<string> ImagesAsync(string baseUrl, string key, string model, string prompt,
            List<(string Mime, byte[] Data)> sources, CancellationToken ct)
        {
            string url = baseUrl.TrimEnd('/');
            if (sources.Count == 0)
            {
                string json = await PostAsync(url + "/images/generations", key, new Dictionary<string, object>
                {
                    ["model"] = model,
                    ["prompt"] = prompt,
                    ["n"] = 1,
                    ["response_format"] = "b64_json"
                }, ct);
                return ReadImageUrl(json);
            }

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(model), "model");
            form.Add(new StringContent(prompt), "prompt");
            form.Add(new StringContent("b64_json"), "response_format");
            foreach (var (mime, data) in sources)
            {
                var part = new ByteArrayContent(data);
                part.Headers.ContentType = new MediaTypeHeaderValue(mime);
                form.Add(part, "image[]", "image." + (mime.Contains("jpeg") ? "jpg" : "png"));
            }
            using var response = await SendAsync(url + "/images/edits", key, form, ct);
            string body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(Trim(body));
            return ReadImageUrl(body);
        }

        /// <summary>从 message.images / content 数组里取第一张 data URL。</summary>
        private static string ReadImageUrl(JsonElement message)
        {
            if (message.ValueKind == JsonValueKind.String) return null;
            if (message.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
                foreach (var image in images.EnumerateArray())
                {
                    string url = image.ValueKind == JsonValueKind.String ? image.GetString() : ReadUrlObject(image);
                    if (!string.IsNullOrEmpty(url)) return url;
                }
            if (message.TryGetProperty("content", out var content))
            {
                if (content.ValueKind == JsonValueKind.Array)
                    foreach (var part in content.EnumerateArray())
                    {
                        if (!part.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) continue;
                        string kind = type.GetString() ?? "";
                        if (kind != "image_url" && kind != "output_image" && kind != "image") continue;
                        string url = ReadUrlObject(part);
                        if (!string.IsNullOrEmpty(url)) return url;
                    }
            }
            return null;
        }

        private static string ReadImageUrl(string json)
        {
            using var doc = JsonDocument.Parse(json ?? "{}");
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("b64_json", out var b64) && b64.ValueKind == JsonValueKind.String)
                        return "data:image/png;base64," + b64.GetString();
                    string url = ReadUrlObject(item);
                    if (!string.IsNullOrEmpty(url)) return url;
                }
            return null;
        }

        private static string ReadUrlObject(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String) return element.GetString();
            if (element.TryGetProperty("image_url", out var imageUrl))
            {
                if (imageUrl.ValueKind == JsonValueKind.String) return imageUrl.GetString();
                if (imageUrl.TryGetProperty("url", out var nested)) return nested.GetString();
            }
            if (element.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
                return url.GetString();
            if (element.TryGetProperty("b64_json", out var b64) && b64.ValueKind == JsonValueKind.String)
                return "data:image/png;base64," + b64.GetString();
            return null;
        }

        private static Result Decode(string dataUrl)
        {
            int comma = dataUrl.IndexOf(',');
            if (!dataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || comma < 0)
                return new Result { Error = "接口返回的不是内联图片（需要 data URL）" };
            try
            {
                string header = dataUrl[..comma];
                string mime = header[5..header.IndexOf(';')];
                return new Result { Data = Convert.FromBase64String(dataUrl[(comma + 1)..]), Error = null };
            }
            catch (Exception ex)
            {
                return new Result { Error = "图片解码失败：" + ex.Message };
            }
        }

        private static List<(string Mime, byte[] Data)> LoadImages(IReadOnlyList<string> paths, out string error)
        {
            error = null;
            var list = new List<(string, byte[])>();
            foreach (string path in paths ?? [])
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    list.Add((Mime(bytes), bytes));
                }
                catch (Exception ex)
                {
                    error = $"读取来源图片失败：{Path.GetFileName(path)}（{ex.Message}）";
                    return null;
                }
            }
            return list;
        }

        private static string Mime(byte[] b)
            => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 ? "image/jpeg"
            : b.Length > 12 && b[8] == 'W' && b[9] == 'E' ? "image/webp"
            : b.Length > 2 && b[0] == 'B' && b[1] == 'M' ? "image/bmp"
            : "image/png";

        private static bool LooksDedicated(string model)
            => model.Contains("gpt-image", StringComparison.OrdinalIgnoreCase)
               || model.Contains("dall-e", StringComparison.OrdinalIgnoreCase);

        private static async Task<string> PostAsync(string url, string key, object body, CancellationToken ct)
        {
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await SendAsync(url, key, content, ct);
            string text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(Trim(text));
            return text;
        }

        private static async Task<HttpResponseMessage> SendAsync(string url, string key, HttpContent content,
            CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            if (!string.IsNullOrWhiteSpace(key))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            return await Http.SendAsync(request, ct);
        }

        private static string Trim(string text)
            => string.IsNullOrEmpty(text) ? "" : (text.Length <= 400 ? text : text[..400] + "…");
    }
}
