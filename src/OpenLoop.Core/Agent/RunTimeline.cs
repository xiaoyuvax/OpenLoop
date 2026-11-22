using System.Text;

namespace OpenLoop.Core.Agent
{
    /// <summary>
    /// 一次运行里「思维片段 → 工具调用 → 正文」的先后顺序。旧数据只存了 ReasoningText / ToolLines
    /// 两段文本，时序在采集时就丢了；这里在回调发生的当下按块记下来，供聊天窗口交替展示。
    /// </summary>
    public sealed class RunTimeline
    {
        /// <summary>顺序块：思维（默认）、工具行、正文（作为分段边界）。</summary>
        private sealed class Block
        {
            public bool Tool;
            public bool Answer;
            public readonly StringBuilder Text = new();
        }

        private readonly List<Block> _blocks = [];

        public bool IsEmpty => _blocks.Count == 0;

        /// <summary>追加思维流片段；与上一块连续时并入同一块，正文之后的思维另起一块。</summary>
        public void Thinking(string piece)
        {
            if (string.IsNullOrEmpty(piece)) return;
            if (_blocks.Count == 0 || _blocks[^1].Tool || _blocks[^1].Answer)
                _blocks.Add(new Block());
            _blocks[^1].Text.Append(piece);
        }

        /// <summary>追加正文片段；正文之前的思维/工具到此收尾，之后再出现思维就落到新的一段。</summary>
        public void Answer(string piece)
        {
            if (string.IsNullOrEmpty(piece)) return;
            if (_blocks.Count == 0 || !_blocks[^1].Answer)
                _blocks.Add(new Block { Answer = true });
            _blocks[^1].Text.Append(piece);
        }

        /// <summary>记一次已完成的工具调用（一行）。</summary>
        public void Tool(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            var block = new Block { Tool = true };
            block.Text.Append(line);
            _blocks.Add(block);
        }

        /// <summary>流式重发前丢弃已生成的思维片段，避免重发后重复。</summary>
        public void DropThinking()
        {
            if (_blocks.Count > 0 && !_blocks[^1].Tool) _blocks.RemoveAt(_blocks.Count - 1);
        }

        /// <summary>按发生顺序拼成文本（只含思维与工具，正文另走分段渲染）；工具块用空行隔开。</summary>
        public string Render()
        {
            var sb = new StringBuilder();
            foreach (var block in _blocks)
            {
                if (block.Answer) continue;
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append(block.Text.ToString().TrimEnd());
            }
            return sb.ToString();
        }

        /// <summary>
        /// 按「正文出现即分段」切分：（本段的思维与工具过程，本段的正文）。
        /// 正文之后又出现思维/工具就另起一段，末段正文可能为空串（仍在流）。始终至少一段。
        /// 正文停在句子中间时不分段：模型常在答案写到一半时插一次工具调用，断在这里会把一句话劈到两个折叠区之间。
        /// </summary>
        public List<(string Thinking, string Answer)> RenderPhases()
        {
            var phases = new List<(string Thinking, string Answer)>();
            var thinking = new StringBuilder();
            var answer = new StringBuilder();
            foreach (var block in _blocks)
            {
                if (block.Answer)
                {
                    answer.Append(block.Text);
                    continue;
                }
                // 本段已有正文，又回到思维/工具：停在自然断点才收尾，否则并入本段（正文继续接在后面）
                if (answer.Length > 0 && EndsAtBreak(answer))
                {
                    phases.Add((thinking.ToString().TrimEnd(), answer.ToString()));
                    thinking.Clear();
                    answer.Clear();
                }
                if (thinking.Length > 0) thinking.Append("\n\n");
                thinking.Append(block.Text.ToString().TrimEnd());
            }
            phases.Add((thinking.ToString().TrimEnd(), answer.ToString()));
            return phases;
        }

        /// <summary>正文是否停在自然断点（换行、句末标点或代码块收尾）；停在词/半句中间就不让工具把正文切断。</summary>
        private static bool EndsAtBreak(StringBuilder text)
        {
            string raw = text.ToString();
            if (raw.EndsWith("\n", StringComparison.Ordinal)) return true;
            string trimmed = raw.TrimEnd();
            return trimmed.Length == 0
                || trimmed.EndsWith("```", StringComparison.Ordinal)
                || "。！？!?；;：:.…".Contains(trimmed[^1]);
        }

        /// <summary>
        /// 收尾补齐：流式节流窗口里最后一段正文没喂进时间线，按最终正文把尾巴补进最后一个正文块。
        /// 最终正文与已采集正文对不上（工具调用夹在中间、供应商清洗等）时不补，保持已采的样子。
        /// </summary>
        public void FeedAnswerTail(string finalAnswer)
        {
            if (string.IsNullOrEmpty(finalAnswer)) return;
            var concat = new StringBuilder();
            foreach (var block in _blocks)
                if (block.Answer) concat.Append(block.Text);
            string have = concat.ToString();
            if (finalAnswer.Length <= have.Length) return;
            if (!finalAnswer.StartsWith(have, StringComparison.Ordinal)) return;
            Answer(finalAnswer[have.Length..]);
        }
    }
}
