using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenLoop.Core.Messages;

namespace OpenLoop.Core.Agent
{
    /// <summary>
    /// 冷启动时整理历史：删掉被后续有效结果取代的失败尝试、收敛重复警告、按内容去重工具结果。
    /// 只在会话长时间空闲（服务端前缀缓存必然已过期）或换了模型时调用；会话进行中不得调用，
    /// 否则会改写已提交的前缀、打断前缀缓存。
    /// </summary>
    public static class HistoryPruner
    {
        /// <summary>同一段警告文本最多保留几条。</summary>
        public const int KeepPerWarning = 3;

        /// <summary>短于这个长度的工具结果不值得去重（省不下 token，还会让模型重读）。</summary>
        public const int MinDedupChars = 200;

        private const string WarningPrefix = "（系统提示";
        private const string ToolResultPrefix = "tool-result:";
        private const string DedupedData = "（与后续相同内容重复，已省略；需要时重新读取）";

        /// <summary>整理一轮完整消息列表，返回删除/改写的条数。</summary>
        public static int Prune(
            List<LlmMessage> messages, int keepPerWarning = KeepPerWarning, int minDedupChars = MinDedupChars)
        {
            if (messages == null || messages.Count == 0) return 0;

            int lastAssistant = LastIndex(messages, m => IsRole(m, "assistant"));
            int lastToolResult = LastIndex(messages, m => IsToolResult(m.Content));

            var drop = new HashSet<int>();

            // 1) 失败尝试被后面同一个调用的成功结果取代：成对删除，当作没发生过。
            var pairs = new List<(int Assistant, int Result, string Signature, bool Ok)>();
            var okSignatures = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < messages.Count; i++)
            {
                if (!IsRole(messages[i], "assistant") || !IsToolResult(messages[i + 1].Content)) continue;
                var calls = AgentToolCallParser.TryParseAll(messages[i].Content);
                string signature = calls.Count == 1
                    ? AgentLoop.SignatureOf(calls[0].Tool, calls[0].Args)
                    : null;
                bool ok = IsOk(messages[i + 1].Content);
                pairs.Add((i, i + 1, signature, ok));
                if (ok && signature != null) okSignatures.Add(signature);
            }
            foreach (var pair in pairs)
            {
                if (pair.Ok || pair.Signature == null) continue;
                if (pair.Assistant == lastAssistant || pair.Result == lastToolResult) continue;
                if (!okSignatures.Contains(pair.Signature)) continue;
                drop.Add(pair.Assistant);
                drop.Add(pair.Result);
            }

            // 2) 同一段警告文本只留最后 keepPerWarning 条。
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (!IsWarning(messages[i].Content)) continue;
                string text = messages[i].Content;
                seen.TryGetValue(text, out int count);
                if (count >= keepPerWarning) drop.Add(i);
                seen[text] = count + 1;
            }

            // 3) 没执行工具、只换来一条提醒的空转 assistant 消息：连提醒一起去掉。
            for (int i = 0; i + 1 < messages.Count; i++)
            {
                if (!IsRole(messages[i], "assistant") || i == lastAssistant) continue;
                if (!IsWarning(messages[i + 1].Content)) continue;
                if (AgentToolCallParser.TryParseAll(messages[i].Content).Count > 0) continue;
                drop.Add(i);
            }

            // 4) 重复的工具结果按内容去重：保留最后一份，前面的换成指针（不能删，否则调用没有结果）。
            int replaced = 0;
            var lastByHash = new HashSet<string>(StringComparer.Ordinal);
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (!IsToolResult(messages[i].Content)) continue;
                if (drop.Contains(i)) continue;
                if (!TryReadData(messages[i].Content, out string data) || data.Length < minDedupChars) continue;
                string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
                if (lastByHash.Add(hash)) continue;
                messages[i].Content = $"tool-result: " + JsonSerializer.Serialize(
                    new { ok = true, data = DedupedData }, AgentLoop.ToolResultJson);
                replaced++;
            }

            int removed = drop.Count;
            if (removed == 0) return replaced;
            var kept = messages.Where((m, i) => !drop.Contains(i)).ToList();
            messages.Clear();
            messages.AddRange(kept);
            return removed + replaced;
        }

        private static int LastIndex(List<LlmMessage> messages, Func<LlmMessage, bool> match)
        {
            for (int i = messages.Count - 1; i >= 0; i--)
                if (match(messages[i])) return i;
            return -1;
        }

        private static bool IsRole(LlmMessage message, string role)
            => string.Equals(message?.Role, role, StringComparison.OrdinalIgnoreCase);

        private static bool IsWarning(string content)
            => content != null && content.StartsWith(WarningPrefix, StringComparison.Ordinal);

        private static bool IsToolResult(string content)
            => content != null && content.StartsWith(ToolResultPrefix, StringComparison.Ordinal);

        private static bool TryRead(string content, out JsonElement root)
        {
            root = default;
            try
            {
                using var doc = JsonDocument.Parse(content[ToolResultPrefix.Length..].Trim());
                root = doc.RootElement.Clone();
                return root.ValueKind == JsonValueKind.Object;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool IsOk(string content)
            => TryRead(content, out var root)
               && root.TryGetProperty("ok", out var ok)
               && ok.ValueKind == JsonValueKind.True;

        private static bool TryReadData(string content, out string data)
        {
            data = null;
            if (!TryRead(content, out var root)) return false;
            if (!root.TryGetProperty("data", out var value) || value.ValueKind != JsonValueKind.String) return false;
            data = value.GetString();
            return !string.IsNullOrEmpty(data);
        }
    }
}
