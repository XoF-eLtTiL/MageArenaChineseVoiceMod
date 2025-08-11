using System;
using System.Collections.Generic;

namespace MageArenaChineseVoice.Patches
{
    /// <summary>
    /// 輕量級多關鍵詞比對器（單執行緒；Trie）
    /// - 單次掃描 text 回傳所有命中的 (keyword, startIndex, endIndex)
    /// - 不是完整 Aho-Corasick（無 fail 邊），但在關鍵詞量不爆炸時速度已非常足夠
    /// - 若未來關鍵詞數量大幅成長，可升級成 AC 完整版
    /// </summary>
    internal sealed class FastKeywordMatcher
    {
        private sealed class Node
        {
            public readonly Dictionary<char, Node> Next = new Dictionary<char, Node>();
            public List<string> Ends; // 該節點結尾的關鍵詞（支援多詞同尾）
        }

        private readonly Node _root = new Node();

        public FastKeywordMatcher(IEnumerable<string> keywords)
        {
            foreach (var kw in keywords)
            {
                if (string.IsNullOrEmpty(kw)) continue;
                var n = _root;
                foreach (var ch in kw)
                {
                    if (!n.Next.TryGetValue(ch, out var nxt))
                        n.Next[ch] = nxt = new Node();
                    n = nxt;
                }
                (n.Ends ??= new List<string>()).Add(kw);
            }
        }

        /// <summary>
        /// 回傳每個命中： (kw, s, e) 代表 kw 命中於 text[s..e]
        /// </summary>
        public List<(string kw, int s, int e)> MatchAll(string text)
        {
            var hits = new List<(string, int, int)>();
            if (string.IsNullOrEmpty(text)) return hits;

            for (int i = 0; i < text.Length; i++)
            {
                var n = _root;
                int j = i;
                while (j < text.Length && n.Next.TryGetValue(text[j], out n))
                {
                    if (n.Ends != null)
                    {
                        foreach (var kw in n.Ends)
                            hits.Add((kw, i, j));
                    }
                    j++;
                }
            }
            return hits;
        }
    }
}
