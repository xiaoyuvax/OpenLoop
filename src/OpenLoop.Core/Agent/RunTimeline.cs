using System.Text;

namespace OpenLoop.Core.Agent
{
    /// <summary>
    /// 一次运行里「思维片段 → 工具调用」的先后顺序。旧数据只存了 ReasoningText / ToolLines
    /// 两段文本，时序在采集时就丢了；这里在回调发生的当下按块记下来，供聊天窗口交替展示。
    /// </summary>
    public sealed class RunTimeline
    {
        private readonly List<(bool Tool, StringBuilder Text)> _blocks = [];

        public bool IsEmpty => _blocks.Count == 0;

        /// <summary>追加思维流片段；与上一块连续时并入同一块。</summary>
        public void Thinking(string piece)
        {
            if (string.IsNullOrEmpty(piece)) return;
            if (_blocks.Count == 0 || _blocks[^1].Tool)
                _blocks.Add((false, new StringBuilder()));
            _blocks[^1].Text.Append(piece);
        }

        /// <summary>记一次已完成的工具调用（一行）。</summary>
        public void Tool(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            _blocks.Add((true, new StringBuilder(line)));
        }

        /// <summary>流式重发前丢弃已生成的思维片段，避免重发后重复。</summary>
        public void DropThinking()
        {
            if (_blocks.Count > 0 && !_blocks[^1].Tool) _blocks.RemoveAt(_blocks.Count - 1);
        }

        /// <summary>按发生顺序拼成文本；工具块用空行隔开，形状 tool(args) · 1s 自身可辨。</summary>
        public string Render()
        {
            if (_blocks.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var (_, text) in _blocks)
            {
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append(text.ToString().TrimEnd());
            }
            return sb.ToString();
        }
    }
}
