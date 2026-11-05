using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace COW_AI.Services;

public class PromptService
{
    private readonly string _promptsPath;
    private readonly OpenRouterService _openRouter;
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

    public PromptService(IWebHostEnvironment env, OpenRouterService openRouter, ILogger<PromptService>? logger = null)
    {
        _promptsPath = Path.Combine(env.ContentRootPath, "Prompts");
        _openRouter = openRouter;
        _logger = logger;
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
    /// Dịch yêu cầu tạo ảnh từ tiếng Việt sang tiếng Anh súc tích qua template ngoài.
    /// </summary>
    public async Task<string> TranslateToEnglishAsync(string vietnameseText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(vietnameseText))
            return string.Empty;

        var template = GetTemplate("PromptTranslation.txt", "{0}");
        var prompt = string.Format(template, vietnameseText);

        try
        {
            var translated = await _openRouter.ChatAsync(prompt, "gemini", cancellationToken);
            var clean = translated?.Trim().Trim('\"', '\'', '`', '.', ' ') ?? string.Empty;
            return string.IsNullOrWhiteSpace(clean) ? vietnameseText : clean;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Lỗi khi dịch prompt tiếng Việt sang tiếng Anh cho: {Text}. Dùng prompt gốc.", vietnameseText);
            return vietnameseText;
        }
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

        var template = GetTemplate("ImageGeneration.txt", "{0}");
        return string.Format(template, englishPrompt);
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
                return string.Format(template, content);
            }
            if (doc.RootElement.TryGetProperty("bullets", out var defaultProp))
            {
                var template = defaultProp.GetString() ?? "{0}";
                return string.Format(template, content);
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

    public static string CleanUserIntent(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return string.Empty;

        string[] fillers = [
            "hãy vẽ cho tôi", "tạo cho tôi một bức ảnh", "hãy tạo một bức ảnh",
            "vẽ một bức ảnh", "tạo một bức ảnh", "hãy vẽ một",
            "vẽ cho tôi", "tạo ảnh cho tôi", "tạo ảnh", "vẽ hình",
            "vẽ tranh", "sinh ảnh", "tạo hình ảnh", "hãy vẽ",
            "generate image of", "generate image", "create an image of",
            "picture of", "draw for me", "draw a picture of", "draw"
        ];

        var clean = prompt;
        foreach (var filler in fillers)
        {
            clean = Regex.Replace(
                clean,
                Regex.Escape(filler),
                "",
                RegexOptions.IgnoreCase
            );
        }

        clean = clean.Trim().TrimStart(':', '-', ' ', '\t');
        return string.IsNullOrWhiteSpace(clean) ? prompt.Trim() : clean.Trim();
    }
}
