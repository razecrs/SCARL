using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Scarl.UI
{
    /// <summary>
    /// CLIP BPE tokenizer — matches OpenAI's original implementation.
    /// </summary>
    public class ClipTokenizer
    {
        public const long SotToken = 49406;
        public const long EotToken = 49407;
        private const int MaxLength = 77;

        private readonly Dictionary<int, char> _b2u;   // byte → unicode char
        private readonly Dictionary<string, int> _encoder;
        private readonly Dictionary<(string, string), int> _bpeRanks;
        private readonly Regex _pat;
        private readonly Dictionary<string, string> _cache = new();

        public ClipTokenizer(string vocabPath, string mergesPath)
        {
            _b2u = BuildBytesToUnicode();

            // Load vocab
            var json = File.ReadAllText(vocabPath);
            _encoder = JsonSerializer.Deserialize<Dictionary<string, int>>(json)!;

            // Load BPE merges (skip the first header line)
            _bpeRanks = new();
            int rank = 0;
            foreach (var line in File.ReadAllLines(mergesPath).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var sp = line.Split(' ', 2);
                if (sp.Length == 2) _bpeRanks[(sp[0], sp[1])] = rank++;
            }

            // Official CLIP regex pattern
            _pat = new Regex(
                @"<\|startoftext\|>|<\|endoftext\|>|'s|'t|'re|'ve|'m|'ll|'d|[\p{L}]+|[\p{N}]|[^\s\p{L}\p{N}]+",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        private static Dictionary<int, char> BuildBytesToUnicode()
        {
            // Printable byte ranges that map 1:1
            var bs = Enumerable.Range('!', '~' - '!' + 1)
                .Concat(Enumerable.Range(0xA1, 0xAC - 0xA1 + 1))
                .Concat(Enumerable.Range(0xAE, 0xFF - 0xAE + 1))
                .ToList();

            var cs = new List<int>(bs);
            int n = 0;
            for (int b = 0; b < 256; b++)
                if (!bs.Contains(b)) { bs.Add(b); cs.Add(256 + n++); }

            var d = new Dictionary<int, char>();
            for (int i = 0; i < bs.Count; i++) d[bs[i]] = (char)cs[i];
            return d;
        }

        private string Bpe(string token)
        {
            if (_cache.TryGetValue(token, out var cached)) return cached;

            // Encode each UTF-8 byte as its unicode representative char
            var chars = Encoding.UTF8.GetBytes(token)
                        .Select(b => _b2u[b].ToString())
                        .ToList();

            if (chars.Count == 0) { _cache[token] = token; return token; }
            chars[^1] += "</w>";

            while (chars.Count > 1)
            {
                int bestRank = int.MaxValue, bestIdx = -1;
                for (int i = 0; i < chars.Count - 1; i++)
                {
                    if (_bpeRanks.TryGetValue((chars[i], chars[i + 1]), out int r) && r < bestRank)
                    { bestRank = r; bestIdx = i; }
                }
                if (bestIdx < 0) break;
                chars[bestIdx] = chars[bestIdx] + chars[bestIdx + 1];
                chars.RemoveAt(bestIdx + 1);
            }

            var result = string.Join(" ", chars);
            _cache[token] = result;
            return result;
        }

        public long[] Encode(string text)
        {
            var tokens = new List<long> { SotToken };

            foreach (Match m in _pat.Matches(text.ToLowerInvariant().Trim()))
            {
                foreach (var bpeTok in Bpe(m.Value).Split(' '))
                    if (_encoder.TryGetValue(bpeTok, out int id))
                        tokens.Add(id);
            }
            tokens.Add(EotToken);

            if (tokens.Count > MaxLength) tokens = tokens.Take(MaxLength).ToList();
            while (tokens.Count < MaxLength) tokens.Add(0L);
            return tokens.ToArray();
        }
    }
}
