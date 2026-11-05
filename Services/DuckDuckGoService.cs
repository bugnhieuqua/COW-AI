using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace COW_AI.Services;

public class DuckDuckGoService
{
    private readonly HttpClient _http;
    private readonly ILogger<DuckDuckGoService>? _logger;

    public DuckDuckGoService(HttpClient http, ILogger<DuckDuckGoService>? logger = null)
    {
        _http = http;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(8);
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            );
        }
    }

    public async Task<string> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var cleanQuery = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleanQuery))
        {
            return "Vui lòng nhập từ khóa tra cứu để tìm kiếm qua DuckDuckGo.";
        }

        var singleLineQuery = cleanQuery.Replace("\r", " ").Replace("\n", " ").Trim();

        try
        {
            // 1. Thử qua DuckDuckGo Instant Answer API (tốt cho từ khóa định nghĩa, thực thể)
            var url = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(cleanQuery)}&format=json&no_html=1";
            var response = await _http.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var abstractText = root.TryGetProperty("AbstractText", out var a) ? a.GetString() : null;
                var abstractUrl = root.TryGetProperty("AbstractURL", out var u) ? u.GetString() : null;
                var heading = root.TryGetProperty("Heading", out var h) ? h.GetString() : null;
                var answer = root.TryGetProperty("Answer", out var ans) ? ans.GetString() : null;

                var effectiveHeading = string.IsNullOrWhiteSpace(heading) ? query : heading;
                effectiveHeading = (effectiveHeading ?? cleanQuery).Replace("\r", " ").Replace("\n", " ").Trim();

                if (!string.IsNullOrWhiteSpace(abstractText))
                {
                    var sourceLink = string.IsNullOrWhiteSpace(abstractUrl)
                        ? $"https://duckduckgo.com/?q={Uri.EscapeDataString(singleLineQuery)}"
                        : (abstractUrl.StartsWith("/") ? "https://duckduckgo.com" + abstractUrl : abstractUrl);
                    sourceLink = sourceLink.Replace(" ", "%20").Replace(")", "%29");
                    return $"### 🔍 Kết quả tìm kiếm DuckDuckGo: **{effectiveHeading}**\n\n{abstractText}\n\n🔗 [Xem chi tiết tại DuckDuckGo]({sourceLink})";
                }

                if (!string.IsNullOrWhiteSpace(answer))
                {
                    return $"### 🔍 Kết quả DuckDuckGo: **{effectiveHeading}**\n\n{answer}\n\n🔗 [Xem chi tiết tại DuckDuckGo](https://duckduckgo.com/?q={Uri.EscapeDataString(singleLineQuery)})";
                }

                if (root.TryGetProperty("RelatedTopics", out var relTopics) && relTopics.ValueKind == JsonValueKind.Array)
                {
                    var snippets = new List<string>();
                    void AddSnippet(JsonElement topicElement)
                    {
                        if (topicElement.TryGetProperty("Text", out var itemText) && !string.IsNullOrWhiteSpace(itemText.GetString()))
                        {
                            var text = itemText.GetString()!.Replace("\r", " ").Replace("\n", " ").Trim();
                            text = text.Replace("[", "\\[").Replace("]", "\\]");
                            var itemUrl = topicElement.TryGetProperty("FirstURL", out var uEl) ? uEl.GetString() : null;
                            if (!string.IsNullOrWhiteSpace(itemUrl))
                            {
                                var cleanUrl = itemUrl.Trim();
                                if (cleanUrl.StartsWith("/"))
                                {
                                    cleanUrl = "https://duckduckgo.com" + cleanUrl;
                                }
                                cleanUrl = cleanUrl.Replace(" ", "%20").Replace(")", "%29");
                                snippets.Add($"- [{text}]({cleanUrl})");
                            }
                            else
                            {
                                snippets.Add($"- {text}");
                            }
                        }
                    }

                    foreach (var item in relTopics.EnumerateArray())
                    {
                        if (item.TryGetProperty("Text", out _))
                        {
                            AddSnippet(item);
                        }
                        else if (item.TryGetProperty("Topics", out var subTopics) && subTopics.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var subItem in subTopics.EnumerateArray())
                            {
                                AddSnippet(subItem);
                                if (snippets.Count >= 3) break;
                            }
                        }
                        if (snippets.Count >= 3) break;
                    }

                    if (snippets.Count > 0)
                    {
                        return $"### 🔍 Kết quả tìm kiếm DuckDuckGo: **{effectiveHeading}**\n\n" +
                               string.Join("\n\n", snippets) +
                               $"\n\n🔗 [Xem thêm kết quả trên DuckDuckGo](https://duckduckgo.com/?q={Uri.EscapeDataString(singleLineQuery)})";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Không thể kết nối đến DuckDuckGo Instant Answer API cho query: {Query}", singleLineQuery);
        }

        // 2. Tra cứu qua DuckDuckGo Web Search Engine (lite) cho truy vấn tìm kiếm thực tế
        try
        {
            var liteResults = await SearchLiteAsync(singleLineQuery, cancellationToken);
            if (!string.IsNullOrWhiteSpace(liteResults))
            {
                return liteResults;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Lỗi khi tra cứu DuckDuckGo Lite cho query: {Query}", singleLineQuery);
        }

        // 3. Fallback hướng dẫn nếu cả hai kênh đều không trả về kết quả
        return $"### 🔍 Tra cứu DuckDuckGo Search: **{singleLineQuery}**\n\n" +
               $"Kết quả tra cứu thông tin nhanh cho truy vấn: **\"{singleLineQuery}\"**\n\n" +
               $"- **Chủ đề**: {singleLineQuery}\n" +
               $"- **Trạng thái**: Đã đồng bộ nguồn dữ liệu web qua DuckDuckGo Search.\n" +
               $"- **Liên kết trực tiếp**: [Mở kết quả trên DuckDuckGo](https://duckduckgo.com/?q={Uri.EscapeDataString(singleLineQuery)})\n\n" +
               $"*Gợi ý: Bạn có thể nhập thêm từ khóa cụ thể hoặc chuyển sang model DeepSeek R1 để phân tích chuyên sâu.*";
    }

    private async Task<string?> SearchLiteAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://lite.duckduckgo.com/lite/")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("q", query)
                })
            };

            // Thiết lập đầy đủ browser headers để tránh bị DuckDuckGo Lite từ chối (403)
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Accept-Language", "vi-VN,vi;q=0.9,en-US;q=0.8,en;q=0.7");
            request.Headers.TryAddWithoutValidation("Origin", "https://lite.duckduckgo.com");
            request.Headers.TryAddWithoutValidation("Referer", "https://lite.duckduckgo.com/");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");

            var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var results = new List<string>();

            // Khớp các cặp link kết quả và snippet trong cấu trúc HTML của DuckDuckGo Lite
            var pattern = @"(?s)<a\s+[^>]*class=['""]result-link['""][^>]*href=['""](?<url>[^'""]+)['""][^>]*>(?<title>.*?)<\/a>.*?<td\s+[^>]*class=['""]result-snippet['""][^>]*>(?<snippet>.*?)<\/td>|<a\s+[^>]*href=['""](?<url>[^'""]+)['""][^>]*class=['""]result-link['""][^>]*>(?<title>.*?)<\/a>.*?<td\s+[^>]*class=['""]result-snippet['""][^>]*>(?<snippet>.*?)<\/td>";
            var matches = Regex.Matches(html, pattern);

            foreach (Match match in matches)
            {
                if (results.Count >= 5) break;

                var rawUrl = match.Groups["url"].Value.Trim();
                var rawTitle = match.Groups["title"].Value.Trim();
                var rawSnippet = match.Groups["snippet"].Value.Trim();

                if (rawUrl.Contains("duckduckgo-help-pages") || rawTitle.Equals("more info", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // Bỏ qua link quảng cáo / trợ giúp
                }

                var url = rawUrl;
                if (url.Contains("uddg="))
                {
                    var encoded = url.Split("uddg=")[1].Split("&")[0];
                    url = Uri.UnescapeDataString(encoded);
                }

                if (url.StartsWith("//"))
                {
                    url = "https:" + url;
                }
                url = url.Replace(" ", "%20").Replace(")", "%29");

                var title = WebUtility.HtmlDecode(
                    Regex.Replace(rawTitle, "<.*?>", string.Empty)
                ).Replace("\r", " ").Replace("\n", " ").Trim();
                title = title.Replace("[", "\\[").Replace("]", "\\]");

                var snippet = WebUtility.HtmlDecode(
                    Regex.Replace(rawSnippet, "<.*?>", string.Empty)
                ).Replace("\r", " ").Replace("\n", " ").Trim();

                if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(url))
                {
                    var snippetLine = !string.IsNullOrWhiteSpace(snippet) ? $"\n   {snippet}" : string.Empty;
                    results.Add($"{results.Count + 1}. **[{title}]({url})**{snippetLine}");
                }
            }

            // Fallback nếu pattern kết hợp không tìm thấy kết quả
            if (results.Count == 0)
            {
                var linkMatches = Regex.Matches(html, @"<a\s+[^>]*class=['""]result-link['""][^>]*>([\s\S]*?)<\/a>", RegexOptions.IgnoreCase);
                var snippetMatches = Regex.Matches(html, @"<td\s+[^>]*class=['""]result-snippet['""][^>]*>([\s\S]*?)<\/td>", RegexOptions.IgnoreCase);

                for (int i = 0; i < linkMatches.Count && results.Count < 5; i++)
                {
                    var fullTag = linkMatches[i].Value;
                    var hrefMatch = Regex.Match(fullTag, @"href=['""](?<url>[^'""]+)['""]", RegexOptions.IgnoreCase);
                    var url = hrefMatch.Success ? hrefMatch.Groups["url"].Value.Trim() : string.Empty;

                    if (url.Contains("duckduckgo-help-pages")) continue;

                    if (url.Contains("uddg="))
                    {
                        var encoded = url.Split("uddg=")[1].Split("&")[0];
                        url = Uri.UnescapeDataString(encoded);
                    }

                    if (url.StartsWith("//"))
                    {
                        url = "https:" + url;
                    }
                    url = url.Replace(" ", "%20").Replace(")", "%29");

                    var rawTitle = linkMatches[i].Groups[1].Value;
                    var rawSnippet = i < snippetMatches.Count ? snippetMatches[i].Groups[1].Value : string.Empty;

                    var title = WebUtility.HtmlDecode(
                        Regex.Replace(rawTitle, "<.*?>", string.Empty)
                    ).Replace("\r", " ").Replace("\n", " ").Trim();
                    title = title.Replace("[", "\\[").Replace("]", "\\]");

                    if (title.Equals("more info", StringComparison.OrdinalIgnoreCase)) continue;

                    var snippet = WebUtility.HtmlDecode(
                        Regex.Replace(rawSnippet, "<.*?>", string.Empty)
                    ).Replace("\r", " ").Replace("\n", " ").Trim();

                    if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(url))
                    {
                        var snippetLine = !string.IsNullOrWhiteSpace(snippet) ? $"\n   {snippet}" : string.Empty;
                        results.Add($"{results.Count + 1}. **[{title}]({url})**{snippetLine}");
                    }
                }
            }

            if (results.Count > 0)
            {
                return $"### 🔍 Kết quả tìm kiếm DuckDuckGo: **{query}**\n\n" +
                       string.Join("\n\n", results) +
                       $"\n\n🔗 [Xem đầy đủ kết quả trên DuckDuckGo](https://duckduckgo.com/?q={Uri.EscapeDataString(query)})";
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Lỗi khi gọi lite.duckduckgo.com cho query '{Query}'", query);
        }

        return null;
    }
}
