using System.Text.Json.Nodes;

namespace OpenLoop.Service.Web
{
    /// <summary>
    /// 留言板上的私信（say 带 to）只对发送方与目标可见。
    /// 留言板宿主（Puppet 核心）按 to 记账，但读接口不按观看者过滤，
    /// 所以会话侧的读取路径（Puppet 工具的 chat / book / say 响应）在这里统一摘掉不是我的私信。
    ///
    /// 用户自己看 [Puppet] 页不受限制（整机归用户所有）。
    /// shortcut: 只处理我们自己序列化的 JSON 形状（messages / agents / peers / chat）；
    /// 解析不了就原样返回，不去猜别的格式。
    /// </summary>
    public static class BoardPrivacy
    {
        /// <summary>按观看者摘掉别人的私信；viewer 为空 = 不过滤。</summary>
        public static string Filter(string json, string viewer)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(viewer)) return json;

            JsonNode root;
            try
            {
                root = JsonNode.Parse(json);
            }
            catch (Exception)
            {
                return json;
            }
            if (root is not JsonObject obj) return json;

            if (obj["messages"] is JsonArray messages)
                for (int i = messages.Count - 1; i >= 0; i--)
                    if (messages[i] is JsonObject message && !Visible(message, viewer))
                        messages.RemoveAt(i);

            foreach (string name in new[] { "agents", "peers" })
                if (obj[name] is JsonArray agents)
                    foreach (var node in agents)
                        if (node is JsonObject peer) HideLastMessage(peer, viewer);

            if (obj["chat"] is JsonObject chat && !Visible(chat, viewer)) obj.Remove("chat");

            return obj.ToJsonString();
        }

        /// <summary>这条消息是不是「发送方或目标是我」：没有 to 的是广播，谁都能看。</summary>
        private static bool Visible(JsonObject message, string viewer)
        {
            string to = Text(message, "to");
            if (to.Length == 0) return true;
            return string.Equals(to, viewer, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Text(message, "agentId"), viewer, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>在场会话里的「最后一句」若是发给别人的私信，只留状态、不留内容。</summary>
        private static void HideLastMessage(JsonObject peer, string viewer)
        {
            string to = Text(peer, "messageTo");
            if (to.Length == 0 || string.Equals(to, viewer, StringComparison.OrdinalIgnoreCase)) return;

            peer.Remove("lastMessage");
            peer.Remove("lastMessageAt");
            peer.Remove("messageTo");
            if (peer["summary"] is JsonValue value && value.TryGetValue(out string summary))
            {
                int cut = summary.LastIndexOf("last said:", StringComparison.OrdinalIgnoreCase);
                if (cut > 0) peer["summary"] = summary[..cut].TrimEnd(' ', ';');
            }
        }

        private static string Text(JsonObject node, string name)
        {
            foreach (var pair in node)
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                    return pair.Value is JsonValue value && value.TryGetValue(out string text) ? text ?? "" : "";
            return "";
        }
    }
}
