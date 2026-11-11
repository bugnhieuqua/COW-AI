using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace COW_AI.Services;

public class PromptService
{
    private const string FallbackTranslatorClient = "PollinationsText";
    private const int MaxTranslatedPromptLength = 1500;
    private const int MaxCacheEntries = 256;

    private static readonly HttpClient SharedFallbackHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    private static readonly ConcurrentDictionary<string, string> TranslationCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _promptsPath;
    private readonly OpenRouterService _openRouter;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly string _fallbackTranslatorUrl;
    private readonly string _neuralTranslateUrl;
    private readonly ILogger<PromptService>? _logger;
    private readonly ConcurrentDictionary<string, string> _templateCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex VietnameseRegex = new(
        @"[\u00E0\u00E1\u1EA3\u00E3\u1EA1\u0103\u1EB1\u1EAF\u1EB3\u1EB5\u1EB7\u00E2\u1EA7\u1EA5\u1EA9\u1EAB\u1EAD\u00E8\u00E9\u1EBB\u1EBD\u1EB9\u00EA\u1EC1\u1EBF\u1EC3\u1EC5\u1EC7\u00EC\u00ED\u1EC9\u0129\u1ECB\u00F2\u00F3\u1ECF\u00F5\u1ECD\u00F4\u1ED3\u1ED1\u1ED5\u1ED7\u1ED9\u01A1\u1EDD\u1EDB\u1EDF\u1EE1\u1EE3\u00F9\u00FA\u1EE7\u0169\u1EE5\u01B0\u1EEB\u1EE9\u1EED\u1EEF\u1EF1\u1EF3\u00FD\u1EF7\u1EF9\u1EF5\u0111\u0110]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex VietnameseKeywordsRegex = new(
        @"\b(con|anh|chu|bac|em|buc|tranh|hinh|cho|meo|ngua|bo|hoa|cay|dong|nha|pho|song|bien|troi|nui|nguoi|co|gai|trai)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>
    /// Nhận diện cụm chất liệu/trường phái hội họa đi kèm chữ "tranh ..." (ví dụ: "tranh sơn dầu", "tranh màu nước")
    /// để không gọt mất chữ "tranh" khiến "tranh sơn dầu" bị biến dạng thành "sơn dầu".
    /// </summary>
    private static readonly Regex PreservedArtMediumRegex = new(
        @"^tranh\s+(?:sơn\s+dầu|màu\s+nước|thủy\s+mặc|sơn\s+mài|lụa|chì|than|phấn|acrylic|cổ\s+điển|trừu\s+tượng|siêu\s+thực|ấn\s+tượng|dân\s+gian|đông\s+hồ|khắc\s+gỗ|tường|mosaic|ghép|kỹ\s+thuật\s+số|digital|3d|anime|manga|pixel|biếm\s+họa|minh\s+họa)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>
    /// Chuẩn hóa cụm "<animal> astronaut" hoặc "astronaut <animal>" để danh từ chính luôn là con vật, tránh diffusion model vẽ mặt người.
    /// </summary>
    private static readonly Regex AnimalAstronautRegex = new(
        @"\b(?:(?<animal>cow|cattle|bull|calf|cat|kitten|dog|puppy|shiba|corgi|bear|panda|fox|rabbit|bunny|pig|duck|frog|monkey|tiger|lion|elephant)\s+astronaut|astronaut\s+(?<animal>cow|cattle|bull|calf|cat|kitten|dog|puppy|shiba|corgi|bear|panda|fox|rabbit|bunny|pig|duck|frog|monkey|tiger|lion|elephant))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly string[] LeadingFillerPhrases =
    [
        "bạn ơi hãy", "bạn ơi", "làm ơn hãy", "làm ơn", "vui lòng",
        "hãy vẽ cho tôi", "hãy vẽ giúp tôi", "tạo cho tôi một bức ảnh", "tạo giúp tôi một bức ảnh",
        "hãy tạo một bức ảnh", "vẽ một bức ảnh", "tạo một bức ảnh",
        "tạo cho tôi bức ảnh", "tạo giúp tôi bức ảnh", "tạo giùm tôi bức ảnh", "tạo cho mình bức ảnh",
        "hãy vẽ một bức", "vẽ một bức", "tạo một bức",
        "vẽ cho tôi", "vẽ giúp tôi", "vẽ hộ tôi", "vẽ giùm tôi", "vẽ cho mình",
        "tạo ảnh cho tôi", "tạo cho tôi ảnh", "tạo giúp tôi ảnh", "tạo giùm tôi ảnh", "tạo cho mình ảnh",
        "tạo giúp tôi", "tạo cho tôi", "tạo giùm tôi", "tạo cho mình",
        "hãy tạo ảnh", "tạo hình ảnh", "tạo ảnh", "sinh ảnh",
        "hãy vẽ hình", "vẽ hình",
        "phác họa cho tôi", "hãy phác họa", "phác họa",
        "thiết kế cho tôi", "thiết kế hình ảnh", "thiết kế ảnh", "render ảnh",
        "tạo wallpaper", "tạo hình nền", "vẽ wallpaper", "vẽ hình nền",
        "hãy vẽ một", "hãy vẽ",
        "generate an image of", "generate image of", "generate image",
        "create an image of", "create image of",
        "draw a picture of", "draw for me", "picture of", "photo of"
    ];

    public PromptService(
        IWebHostEnvironment env,
        OpenRouterService openRouter,
        IConfiguration? config = null,
        IHttpClientFactory? httpClientFactory = null,
        ILogger<PromptService>? logger = null)
    {
        _promptsPath = Path.Combine(env.ContentRootPath, "Prompts");
        _openRouter = openRouter;
        _httpClientFactory = httpClientFactory;
        _fallbackTranslatorUrl = config?["Pollinations:TextUrl"] ?? "https://text.pollinations.ai/";
        _neuralTranslateUrl = config?["Pollinations:FallbackTranslateUrl"] ?? "https://translate.googleapis.com/translate_a/single";
        _logger = logger;
    }

    /// <summary>
    /// Kiểm tra xem chuỗi bắt đầu bằng cụm "tranh + chất liệu nghệ thuật" cần bảo toàn hay không.
    /// </summary>
    public static bool StartsWithPreservedArtMedium(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && PreservedArtMediumRegex.IsMatch(text.TrimStart());
    }

    /// <summary>
    /// Kiểm tra xem chuỗi văn bản có chứa tiếng Việt hay không.
    /// </summary>
    public static bool ContainsVietnamese(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return VietnameseRegex.IsMatch(text) || VietnameseKeywordsRegex.IsMatch(text);
    }

    /// <summary>
    /// Đọc template từ file ngoài trong thư mục Prompts/ có hỗ trợ bộ nhớ đệm cache.
    /// </summary>
    public string GetTemplate(string fileName, string fallbackTemplate = "{0}")
    {
        if (_templateCache.TryGetValue(fileName, out var cached))
        {
            return cached;
        }

        var filePath = Path.Combine(_promptsPath, fileName);
        if (File.Exists(filePath))
        {
            try
            {
                var content = File.ReadAllText(filePath).Trim();
                _templateCache[fileName] = content;
                return content;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Không thể đọc file template prompt {FileName}. Sử dụng fallback.", fileName);
            }
        }

        return fallbackTemplate;
    }

    /// <summary>
    /// Dịch yêu cầu tạo ảnh từ tiếng Việt sang tiếng Anh qua kiến trúc 3 tầng dự phòng:
    /// 1. OpenRouter (template PromptTranslation.txt) khi đã cấu hình API key.
    /// 2. Pollinations Text API miễn phí (openai-fast, template PromptTranslationFallback.txt).
    /// 3. Neural Machine Translation tốc độ cao khi cả hai LLM trên quá tải hoặc hết hạn mức.
    /// </summary>
    public async Task<string> TranslateToEnglishAsync(string vietnameseText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(vietnameseText))
            return string.Empty;

        var cacheKey = vietnameseText.Trim();
        if (TranslationCache.TryGetValue(cacheKey, out var cachedTranslation))
        {
            return cachedTranslation;
        }

        // Tầng 1: OpenRouter LLM
        try
        {
            var template = GetTemplate("PromptTranslation.txt", "{0}");
            var promptPayload = FormatSinglePlaceholder(template, cacheKey);
            var translated = await _openRouter.ChatAsync(promptPayload, "gemini", cancellationToken);
            if (TryAcceptTranslation(translated, out var accepted))
            {
                return CacheAndReturn(cacheKey, EnsureAnimalSubjectGrounding(accepted));
            }
            _logger?.LogWarning("OpenRouter trả về bản dịch không hợp lệ cho: {Text}. Chuyển sang tầng 2.", cacheKey);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger?.LogWarning(ex, "OpenRouter không dịch được prompt: {Text}. Chuyển sang tầng 2.", cacheKey);
        }

        // Tầng 2: Pollinations Text API (openai-fast với reasoning_effort = low)
        try
        {
            var translated = await TranslateWithPollinationsTextAsync(cacheKey, cancellationToken);
            if (TryAcceptTranslation(translated, out var accepted))
            {
                return CacheAndReturn(cacheKey, EnsureAnimalSubjectGrounding(accepted));
            }
            _logger?.LogWarning("Pollinations Text API trả về kết quả không hợp lệ cho: {Text}. Chuyển sang tầng 3.", cacheKey);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger?.LogWarning(ex, "Pollinations Text API thất bại cho: {Text}. Chuyển sang tầng 3.", cacheKey);
        }

        // Tầng 3: Dịch máy Neural tốc độ cao (đảm bảo luôn có tiếng Anh chuẩn cho mô hình tạo ảnh)
        try
        {
            var translated = await TranslateWithNeuralFallbackAsync(cacheKey, cancellationToken);
            if (TryAcceptTranslation(translated, out var accepted))
            {
                return CacheAndReturn(cacheKey, EnsureAnimalSubjectGrounding(accepted));
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger?.LogWarning(ex, "Tầng dịch dự phòng thứ 3 thất bại cho: {Text}.", cacheKey);
        }

        _logger?.LogError("Không dịch được prompt tạo ảnh sang tiếng Anh, dùng bản bỏ dấu dự phòng: {Text}", cacheKey);
        return FoldVietnameseDiacritics(cacheKey);
    }

    private static string CacheAndReturn(string key, string value)
    {
        if (TranslationCache.Count >= MaxCacheEntries)
        {
            TranslationCache.Clear();
        }
        TranslationCache[key] = value;
        return value;
    }

    private async Task<string?> TranslateWithPollinationsTextAsync(string vietnameseText, CancellationToken cancellationToken)
    {
        var template = GetTemplate("PromptTranslationFallback.txt", "{0}");
        var instruction = FormatSinglePlaceholder(template, vietnameseText);
        var client = _httpClientFactory?.CreateClient(FallbackTranslatorClient) ?? SharedFallbackHttpClient;
        var baseUrl = _fallbackTranslatorUrl.TrimEnd('/');

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));

        var openAiUrl = baseUrl.EndsWith("/openai", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : baseUrl + "/openai";

        var requestBody = JsonSerializer.Serialize(new
        {
            model = "openai-fast",
            reasoning_effort = "low",
            messages = new[]
            {
                new { role = "user", content = instruction }
            },
            temperature = 0.3
        });

        using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(openAiUrl, content, timeout.Token);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync(timeout.Token);
        return ExtractOpenAiContentOrRaw(raw);
    }

    private async Task<string?> TranslateWithNeuralFallbackAsync(string vietnameseText, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory?.CreateClient(FallbackTranslatorClient) ?? SharedFallbackHttpClient;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        var url = $"{_neuralTranslateUrl}?client=gtx&sl=vi&tl=en&dt=t&q={Uri.EscapeDataString(vietnameseText)}";
        using var response = await client.GetAsync(url, timeout.Token);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(timeout.Token);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            return null;

        var sentences = doc.RootElement[0];
        if (sentences.ValueKind != JsonValueKind.Array)
            return null;

        var sb = new StringBuilder();
        foreach (var segment in sentences.EnumerateArray())
        {
            if (segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0)
            {
                var part = segment[0].GetString();
                if (!string.IsNullOrEmpty(part))
                {
                    sb.Append(part);
                }
            }
        }

        return sb.ToString().Trim();
    }

    private static string? ExtractOpenAiContentOrRaw(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
            return trimmed;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var msg)
                    && msg.TryGetProperty("content", out var contentProp))
                {
                    return contentProp.GetString();
                }
            }
        }
        catch
        {
            // Bỏ qua nếu JSON không đúng định dạng OpenAI
        }

        return null;
    }

    /// <summary>
    /// Làm sạch nhãn markdown thừa từ LLM, chuẩn hóa khoảng trắng/ký tự đặc biệt và kiểm tra tỷ lệ tiếng Anh.
    /// </summary>
    private static bool TryAcceptTranslation(string? translated, out string accepted)
    {
        accepted = string.Empty;
        if (string.IsNullOrWhiteSpace(translated))
            return false;

        var cleaned = translated.Trim();

        // Gọt các tiêu đề dẫn nhập kiểu "**Oil-painting prompt:**" hoặc "Prompt:" mà LLM đôi khi thêm vào
        cleaned = Regex.Replace(
            cleaned,
            @"^\s*(?:\*{1,2}[^*\n]{1,50}prompt\s*:?\*{1,2}|(?:english\s+(?:visual\s+)?)?prompt\s*:)\s*",
            "",
            RegexOptions.IgnoreCase);

        cleaned = cleaned
            .Replace('\u202F', ' ')
            .Replace('\u00A0', ' ')
            .Replace('\u2011', '-')
            .Replace('\u2013', '-')
            .Replace('\u2014', ',')
            .Replace('“', '"')
            .Replace('”', '"')
            .Replace('‘', '\'')
            .Replace('’', '\'');

        var normalized = Regex.Replace(cleaned, @"\s+", " ").Trim();
        normalized = normalized.Trim('"', '\'', '`', '*', ' ').TrimEnd('.').Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > MaxTranslatedPromptLength)
            return false;

        if (!VietnameseRegex.IsMatch(normalized))
        {
            accepted = normalized;
            return true;
        }

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return false;

        int accentedWords = 0;
        foreach (var word in words)
        {
            if (VietnameseRegex.IsMatch(word))
            {
                accentedWords++;
            }
        }

        // Nếu trên 20% từ vẫn mang dấu tiếng Việt -> model chưa dịch sang tiếng Anh
        if ((double)accentedWords / words.Length > 0.20)
            return false;

        accepted = FoldVietnameseDiacritics(normalized);
        return !string.IsNullOrWhiteSpace(accepted);
    }

    /// <summary>
    /// Đảm bảo khi người dùng yêu cầu động vật đóng vai phi hành gia, con vật luôn là danh từ chính (tránh model ảnh vẽ khuôn mặt người).
    /// </summary>
    private static string EnsureAnimalSubjectGrounding(string englishPrompt)
    {
        if (string.IsNullOrWhiteSpace(englishPrompt) || !AnimalAstronautRegex.IsMatch(englishPrompt))
            return englishPrompt;

        return AnimalAstronautRegex.Replace(englishPrompt, match =>
        {
            var animal = match.Groups["animal"].Value.ToLowerInvariant();
            return animal is "cow" or "cattle" or "calf"
                ? "anthropomorphic black-and-white spotted dairy cow wearing a white NASA spacesuit and clear spherical glass helmet with actual bovine animal face, pink cow snout, and curved horns (no human face)"
                : $"anthropomorphic {animal} wearing a white NASA spacesuit and clear spherical glass helmet with actual {animal} animal face and muzzle (no human face)";
        });
    }

    private static string FoldVietnameseDiacritics(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var decomposed = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            sb.Append(ch switch
            {
                'đ' => 'd',
                'Đ' => 'D',
                _ => ch
            });
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string FormatSinglePlaceholder(string template, string value)
    {
        if (string.IsNullOrEmpty(template))
            return value;

        return template.Contains("{0}", StringComparison.Ordinal)
            ? template.Replace("{0}", value, StringComparison.Ordinal)
            : string.Format(template, value);
    }

    /// <summary>
    /// Sinh prompt tạo ảnh hoàn chỉnh qua template ngoài ImageGeneration.txt
    /// </summary>
    public async Task<string> GetImageGenerationPromptAsync(string userPrompt, CancellationToken cancellationToken = default)
    {
        var cleanPrompt = CleanUserIntent(userPrompt);
        if (string.IsNullOrWhiteSpace(cleanPrompt))
            return string.Empty;

        string englishPrompt = cleanPrompt;
        if (ContainsVietnamese(cleanPrompt))
        {
            englishPrompt = await TranslateToEnglishAsync(cleanPrompt, cancellationToken);
        }
        else
        {
            englishPrompt = EnsureAnimalSubjectGrounding(englishPrompt);
        }

        var template = GetTemplate("ImageGeneration.txt", "{0}");
        return FormatSinglePlaceholder(template, englishPrompt);
    }

    /// <summary>
    /// Sinh prompt tóm tắt tài liệu qua template ngoài DocumentSummary.json (Zero hardcoding)
    /// </summary>
    public string GetSummaryPrompt(string content, string? length)
    {
        var jsonContent = GetTemplate("DocumentSummary.json", "{}");
        var selectedStyle = string.IsNullOrWhiteSpace(length) ? "bullets" : length.ToLowerInvariant().Trim();

        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            if (doc.RootElement.TryGetProperty(selectedStyle, out var styleProp))
            {
                var template = styleProp.GetString() ?? "{0}";
                return FormatSinglePlaceholder(template, content);
            }
            if (doc.RootElement.TryGetProperty("bullets", out var defaultProp))
            {
                var template = defaultProp.GetString() ?? "{0}";
                return FormatSinglePlaceholder(template, content);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Lỗi khi phân tích template DocumentSummary.json. Sử dụng fallback.");
        }

        return content;
    }

    /// <summary>
    /// Sinh prompt viết code qua template ngoài CodeGeneration.txt (Zero hardcoding)
    /// </summary>
    public string GetCodeGenPrompt(string prompt, string language, string? standards)
    {
        var template = GetTemplate("CodeGeneration.txt", "{1}");
        var targetLanguage = string.IsNullOrWhiteSpace(language) ? "phù hợp" : language.Trim();
        var standardDetails = string.IsNullOrWhiteSpace(standards)
            ? "Mã nguồn sạch, tối ưu hóa và có chú thích tiếng Việt."
            : standards.Trim();

        return string.Format(template, targetLanguage, prompt, standardDetails);
    }

    /// <summary>
    /// Gọt bỏ tiền tố mệnh lệnh ở đầu câu nhưng bảo toàn các chất liệu hội họa như "tranh sơn dầu", "tranh màu nước".
    /// </summary>
    public static string CleanUserIntent(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return string.Empty;

        var clean = prompt.Trim();
        bool changed = true;

        while (changed && !string.IsNullOrWhiteSpace(clean))
        {
            changed = false;
            clean = clean.TrimStart(':', '-', ',', ' ', '\t');

            // Xử lý đặc biệt cho "hãy vẽ tranh ..." / "vẽ tranh ..." để bảo toàn "tranh sơn dầu", "tranh màu nước", ...
            var tranhPrefixMatch = Regex.Match(clean, @"^(?:hãy\s+)?vẽ\s+(tranh\b.*)$", RegexOptions.IgnoreCase);
            if (tranhPrefixMatch.Success)
            {
                var tranhRemainder = tranhPrefixMatch.Groups[1].Value.TrimStart();
                if (StartsWithPreservedArtMedium(tranhRemainder))
                {
                    clean = tranhRemainder;
                    changed = true;
                    continue;
                }

                var afterTranh = Regex.Replace(tranhRemainder, @"^tranh\s*:?\s*", "", RegexOptions.IgnoreCase).TrimStart();
                if (!string.IsNullOrWhiteSpace(afterTranh))
                {
                    clean = afterTranh;
                    changed = true;
                    continue;
                }
            }

            foreach (var filler in LeadingFillerPhrases)
            {
                if (clean.StartsWith(filler, StringComparison.OrdinalIgnoreCase))
                {
                    var nextCharIndex = filler.Length;
                    if (nextCharIndex == clean.Length || char.IsWhiteSpace(clean[nextCharIndex]) || clean[nextCharIndex] is ':' or '-' or ',')
                    {
                        var remainder = clean.Substring(nextCharIndex).TrimStart(':', '-', ',', ' ', '\t');
                        if (!string.IsNullOrWhiteSpace(remainder))
                        {
                            clean = remainder;
                            changed = true;
                            break;
                        }
                    }
                }
            }

            if (changed)
                continue;

            // Gọt động từ đơn lẻ ở đầu câu (trừ khi đã bắt đầu bằng "tranh <chất liệu>")
            if (!StartsWithPreservedArtMedium(clean))
            {
                var singleVerbMatch = Regex.Match(clean, @"^(?:tạo|vẽ|sinh|render|draw|paint|sketch)\s+(.+)$", RegexOptions.IgnoreCase);
                if (singleVerbMatch.Success)
                {
                    var remainder = singleVerbMatch.Groups[1].Value.TrimStart(':', '-', ' ', '\t');
                    if (!string.IsNullOrWhiteSpace(remainder))
                    {
                        clean = remainder;
                        changed = true;
                    }
                }
            }
        }

        // Nếu sau khi gọt "vẽ cho tôi" còn "một bức tranh sơn dầu..." thì giữ từ "tranh sơn dầu..."
        var bucTranhMatch = Regex.Match(clean, @"^(?:một\s+)?bức\s+(tranh\b.*)$", RegexOptions.IgnoreCase);
        if (bucTranhMatch.Success)
        {
            var tranhPart = bucTranhMatch.Groups[1].Value.TrimStart();
            clean = StartsWithPreservedArtMedium(tranhPart)
                ? tranhPart
                : Regex.Replace(tranhPart, @"^tranh\s+", "", RegexOptions.IgnoreCase);
        }

        clean = Regex.Replace(clean, @"\s{2,}", " ").Trim().TrimStart(':', '-', ' ', '\t');
        return string.IsNullOrWhiteSpace(clean) ? prompt.Trim() : clean.Trim();
    }
}
