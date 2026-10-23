using System.Globalization;
using System.Text.Json;

namespace OpenLoop.Core.Tools
{
    /// <summary>工具入参的类型化读取。解析器把原始 JSON 值放成 string/long/double/bool/JsonElement。</summary>
    public sealed class ToolArgs
    {
        private readonly Dictionary<string, object> _raw;

        public ToolArgs(Dictionary<string, object> raw)
        {
            _raw = raw ?? new Dictionary<string, object>(StringComparer.Ordinal);
        }

        public IReadOnlyDictionary<string, object> Raw => _raw;

        public bool Has(string key) => _raw.ContainsKey(key);

        public string Str(string key, string fallback = null)
        {
            if (!_raw.TryGetValue(key, out var v) || v == null) return fallback;
            string s = v is JsonElement je
                ? (je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString())
                : v.ToString();
            return string.IsNullOrWhiteSpace(s) ? fallback : s;
        }

        public IReadOnlyList<string> StrList(string key)
        {
            if (!_raw.TryGetValue(key, out var v) || v == null) return Array.Empty<string>();
            var list = new List<string>();
            switch (v)
            {
                case JsonElement je when je.ValueKind == JsonValueKind.Array:
                    foreach (var item in je.EnumerateArray())
                        list.Add(item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString());
                    break;
                case JsonElement je when je.ValueKind == JsonValueKind.String:
                    list.Add(je.GetString());
                    break;
                case IEnumerable<object> items:
                    foreach (var item in items)
                        list.Add(Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture));
                    break;
                default:
                    list.Add(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture));
                    break;
            }
            return list.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
        }

        public int Int(string key, int fallback)
        {
            if (!_raw.TryGetValue(key, out var v) || v == null) return fallback;
            switch (v)
            {
                case long l: return (int)l;
                case int i: return i;
                case double d: return (int)d;
                case bool b: return b ? 1 : 0;
                case JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out long jl):
                    return (int)jl;
                default:
                    return int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out int parsed)
                        ? parsed
                        : fallback;
            }
        }

        public bool Bool(string key, bool fallback)
        {
            if (!_raw.TryGetValue(key, out var v) || v == null) return fallback;
            switch (v)
            {
                case bool b: return b;
                case long l: return l != 0;
                case double d: return d != 0;
                default:
                    string s = Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim();
                    if (bool.TryParse(s, out bool parsed)) return parsed;
                    if (s == "1") return true;
                    if (s == "0") return false;
                    return fallback;
            }
        }
    }
}
