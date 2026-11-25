using System;
using System.Text;
using System.Text.RegularExpressions;

namespace ValennaBattle
{
    internal static class ValennaRuleWrapping
    {
        // 与 tools/format_rules.ps1 的规则短语边界对应。测量及显示时都移除边界。
        private const char Open = '\u2063', Close = '\u2064';
        private const string ClosingPunctuation = "，。；：、！？）》】」％%";
        private static readonly Regex GlyphOrTag = new Regex("<[^>]+>|.", RegexOptions.Compiled);
        private static readonly Regex CoreQuantity = new Regex(
            @"^[+＋−-]?[0-9]+(?:[dD.][0-9]+)?(?:[-~～][0-9]+)?(?:点|层|发|颗|次|段|倍|面|小时|回合|[%％])?|[+＋−-][0-9]+(?:\.[0-9]+)?[%％]?$",
            RegexOptions.Compiled);

        internal static bool IsMarked(string text) => text != null && text.IndexOf(Open) >= 0;
        internal static string Unmark(string text) => text.Replace(Open.ToString(), "").Replace(Close.ToString(), "");

        internal static string Wrap(string text, Func<string, int> lines)
        {
            if (!IsMarked(text)) return text;
            if (lines == null) return Unmark(text);
            string[] paragraphs = text.Split('\n');
            for (int i = 0; i < paragraphs.Length; ++i)
            {
                string marked = paragraphs[i];
                if (!IsMarked(marked)) continue;
                string plain = Unmark(marked);
                if (lines(plain) <= 1) { paragraphs[i] = plain; continue; }

                var result = new StringBuilder(marked.Length);
                int cursor = 0, lineStart = 0;
                int open;
                while ((open = marked.IndexOf(Open, cursor)) >= 0)
                {
                    int end = marked.IndexOf(Close, open + 1);
                    if (end < 0) break;
                    result.Append(marked, cursor, open - cursor);
                    string phrase = marked.Substring(open + 1, end - open - 1);
                    int next = end + 1;
                    while (next < marked.Length && ClosingPunctuation.IndexOf(marked[next]) >= 0) next++;
                    string punctuated = phrase + marked.Substring(end + 1, next - end - 1);
                    if (phrase.Length != 0)
                    {
                        string prefix = result.ToString(lineStart, result.Length - lineStart);
                        int firstLine = lines(prefix + FirstGlyph(phrase));
                        // 标点随词收尾。极窄面板优先保住数量短语本身。
                        string probe = punctuated != phrase && lines(punctuated) == 1 ? punctuated : phrase;
                        if (firstLine > 0 && lines(prefix + probe) > firstLine)
                        {
                            if (lines(phrase) == 1)
                            {
                                result.Append('\n');
                                lineStart = result.Length;
                            }
                            else
                            {
                                // 整组比一行还宽时，至少让「10点」或「+10%」一同移动。
                                Match core = CoreQuantity.Match(phrase);
                                if (core.Success && lines(core.Value) == 1)
                                {
                                    string before = prefix + phrase.Substring(0, core.Index);
                                    if (lines(before + core.Value) > lines(before + core.Value[0]))
                                    {
                                        punctuated = punctuated.Insert(core.Index, "\n");
                                        lineStart = result.Length + core.Index + 1;
                                    }
                                }
                            }
                        }
                    }
                    result.Append(punctuated);
                    cursor = next;
                }
                result.Append(marked, cursor, marked.Length - cursor);
                paragraphs[i] = result.ToString();
            }
            return string.Join("\n", paragraphs);
        }

        private static string FirstGlyph(string phrase)
        {
            if (phrase[0] != '<') return phrase.Substring(0, 1);
            // 保留成对的富文本标签，让第一个字也按实际字体样式测量。
            bool found = false;
            return GlyphOrTag.Replace(phrase, match =>
            {
                if (match.Value[0] == '<') return match.Value;
                if (found) return "";
                found = true;
                return match.Value;
            });
        }
    }
}
