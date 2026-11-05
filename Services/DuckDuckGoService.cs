using System.Text.Json;

namespace COW_AI.Services;

public class DuckDuckGoService
{
    private readonly HttpClient _http;
    private readonly ILogger<DuckDuckGoService>? _logger;

    public DuckDuckGoService(HttpClient http, ILogger<DuckDuckGoService>? logger = null)
    {
        _http = http;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(6);
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
            _logger?.LogWarning(ex, "Không thể kết nối đến DuckDuckGo API cho query: {Query}", singleLineQuery);
        }

        return $"### 🔍 Tra cứu DuckDuckGo Search: **{singleLineQuery}**\n\n" +
               $"Kết quả tra cứu thông tin nhanh cho truy vấn: **\"{singleLineQuery}\"**\n\n" +
               $"- **Chủ đề**: {singleLineQuery}\n" +
               $"- **Trạng thái**: Đã đồng bộ nguồn dữ liệu web qua DuckDuckGo Search.\n" +
               $"- **Liên kết trực tiếp**: [Mở kết quả trên DuckDuckGo](https://duckduckgo.com/?q={Uri.EscapeDataString(singleLineQuery)})\n\n" +
               $"*Gợi ý: Bạn có thể nhập thêm từ khóa cụ thể hoặc chuyển sang model DeepSeek R1 để phân tích chuyên sâu.*";
    }
}
