using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TsuruLauncher.Services
{
    /// <summary>
    /// 资源简介翻译（详情页 hero 的「翻译」按钮）。
    ///
    /// 美西螈那边走的是自己的翻译后端；Tsuru 没有后端，所以这里用 MyMemory 的公开接口
    /// （<c>api.mymemory.translated.net</c>，免密钥）。它单次请求正文上限 500 字节，
    /// 所以按句子切块并发/串行提交，再拼回去。
    ///
    /// 失败一律返回 <c>null</c>，由调用方决定要不要提示 —— 不抛异常、不阻塞 UI。
    /// </summary>
    public static class TranslationService
    {
        private const int MaxChunkChars = 460; // MyMemory 的 500 字节上限，留点余量给 URL 编码

        private static readonly HttpClient _http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "TsuruLauncher/1.0");
            return c;
        }

        /// <summary>把 <paramref name="text"/> 翻成中文；失败返回 null。</summary>
        public static async Task<string?> TranslateToChineseAsync(string? text, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var chunks = SplitChunks(text!, MaxChunkChars);
            var results = new List<string>(chunks.Count);

            foreach (var chunk in chunks)
            {
                ct.ThrowIfCancellationRequested();
                var translated = await TranslateChunkAsync(chunk, ct).ConfigureAwait(false);
                if (translated == null) return null;   // 任意一块失败就整体失败，不返回半截译文
                results.Add(translated);
            }

            return string.Concat(results);
        }

        private static async Task<string?> TranslateChunkAsync(string chunk, CancellationToken ct)
        {
            // 纯空白/纯符号的块不浪费一次请求
            if (string.IsNullOrWhiteSpace(chunk)) return chunk;

            try
            {
                string url = "https://api.mymemory.translated.net/get?q=" +
                             Uri.EscapeDataString(chunk) + "&langpair=en|zh-CN";

                using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;

                string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("responseData", out var rd)) return null;
                if (!rd.TryGetProperty("translatedText", out var tt)) return null;

                string? result = tt.GetString();
                if (string.IsNullOrWhiteSpace(result)) return null;

                // 接口限流时会回一段英文提示，别把它当成译文塞进正文
                if (result.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase)) return null;

                return result;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 按「段落 → 句子 → 硬切」三级降级把正文切成 ≤<paramref name="max"/> 字符的块，
        /// 保证不会把一句话从中间劈开（劈开翻译质量会明显变差）。
        /// </summary>
        private static List<string> SplitChunks(string text, int max)
        {
            var chunks = new List<string>();
            var sb = new StringBuilder();

            // 保留换行：先按行拆，行内再按句末标点拆
            var lines = text.Replace("\r\n", "\n").Split('\n');
            foreach (var line in lines)
            {
                foreach (var sentence in SplitSentences(line))
                {
                    if (sentence.Length > max)
                    {
                        // 单句超长：硬切
                        if (sb.Length > 0) { chunks.Add(sb.ToString()); sb.Clear(); }
                        for (int i = 0; i < sentence.Length; i += max)
                            chunks.Add(sentence.Substring(i, Math.Min(max, sentence.Length - i)));
                        continue;
                    }

                    if (sb.Length + sentence.Length > max)
                    {
                        chunks.Add(sb.ToString());
                        sb.Clear();
                    }
                    sb.Append(sentence);
                }

                // 换行也算分隔，让译文保持段落结构
                if (sb.Length > max) { chunks.Add(sb.ToString()); sb.Clear(); }
                sb.Append('\n');
                if (sb.Length >= max) { chunks.Add(sb.ToString()); sb.Clear(); }
            }

            if (sb.Length > 0) chunks.Add(sb.ToString());

            // 去掉纯空白块
            chunks.RemoveAll(c => string.IsNullOrWhiteSpace(c));
            return chunks;
        }

        private static IEnumerable<string> SplitSentences(string line)
        {
            if (string.IsNullOrEmpty(line)) { yield return string.Empty; yield break; }

            int start = 0;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c != '.' && c != '!' && c != '?' && c != '。' && c != '！' && c != '？') continue;

                // 句末后紧跟空格/结束才算一句，避免把 "1.21.4" 拆开
                bool atEnd = i == line.Length - 1;
                bool followedBySpace = !atEnd && char.IsWhiteSpace(line[i + 1]);
                if (!atEnd && !followedBySpace) continue;

                yield return line.Substring(start, i - start + 1);
                start = i + 1;
            }

            if (start < line.Length) yield return line.Substring(start);
        }
    }
}
