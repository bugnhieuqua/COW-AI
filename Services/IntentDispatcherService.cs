using System.Text.RegularExpressions;

namespace COW_AI.Services;

public class IntentDispatcherService
{
    private readonly ILogger<IntentDispatcherService>? _logger;

    public IntentDispatcherService(ILogger<IntentDispatcherService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Tự động lựa chọn mô hình AI tối ưu dựa trên nội dung prompt của người dùng.
    /// </summary>
    public string ResolveAutoModel(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "gemini";
        }

        var p = prompt.ToLowerInvariant();

        // Từ khóa lập trình (dùng word boundary cho các từ tiếng Anh ngắn chống nhận diện nhầm; hỗ trợ c# và c++)
        if (Regex.IsMatch(p,
            @"(?:\b(code|function|hàm|lập trình|thuật toán|algorithm|class|python|javascript|typescript|html|css|sql|bug|debug|compile|exception|api|database|git|regex|csharp)\b|\bc#(?!\w)|\bc\+\+(?!\w))",
            RegexOptions.IgnoreCase))
        {
            return "deepseek";
        }

        // Từ khóa tóm tắt / phân tích / dịch thuật
        if (Regex.IsMatch(p,
            @"\b(tóm tắt|summarize|summary|rút gọn|báo cáo|dịch|translate|viết lại|paraphrase)\b",
            RegexOptions.IgnoreCase))
        {
            return "llama3";
        }

        return "gemini";
    }

    /// <summary>
    /// Nhận diện ý định tạo ảnh từ người dùng (qua model lựa chọn, slash command hoặc từ khóa tự nhiên).
    /// </summary>
    public bool DetectImageIntent(string prompt, string? model)
    {
        return DetectImageIntent(prompt, model, out _);
    }

    /// <summary>
    /// Nhận diện ý định tạo ảnh và trích xuất prompt sạch sau khi gọt bỏ slash command / tiền tố.
    /// </summary>
    public bool DetectImageIntent(string prompt, string? model, out string cleanPrompt)
    {
        cleanPrompt = prompt?.Trim() ?? string.Empty;

        // 1. Kiểm tra Model lựa chọn
        if (string.Equals(model?.Trim(), "pollinations", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(model?.Trim(), "image", StringComparison.OrdinalIgnoreCase))
        {
            StripSlashCommand(ref cleanPrompt);
            if (string.IsNullOrWhiteSpace(cleanPrompt))
            {
                cleanPrompt = prompt?.Trim() ?? string.Empty;
            }
            return true;
        }

        // 2. Kiểm tra Slash command
        if (StripSlashCommand(ref cleanPrompt))
        {
            if (string.IsNullOrWhiteSpace(cleanPrompt))
            {
                cleanPrompt = prompt?.Trim() ?? string.Empty;
            }
            return true;
        }

        // Nếu người dùng chủ động chọn DuckDuckGo Search, không tự động chuyển sang tạo ảnh trừ khi có slash command (/image, ...)
        if (string.Equals(model?.Trim(), "duckduckgo", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 3. Kiểm tra các tiền tố tự nhiên phổ biến
        string[] naturalPrefixes = [
            "tạo ảnh:", "tạo ảnh ", "hãy tạo ảnh ",
            "vẽ tranh:", "vẽ tranh ", "hãy vẽ tranh ",
            "vẽ hình:", "vẽ hình ", "hãy vẽ hình ",
            "vẽ cho tôi ", "vẽ một ", "hãy vẽ ", "vẽ ",
            "sinh ảnh ", "tạo hình ảnh ",
            "generate image of ", "generate image ",
            "create an image of ", "picture of ",
            "draw ", "paint ", "sketch "
        ];

        foreach (var prefix in naturalPrefixes)
        {
            if (cleanPrompt.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var stripped = cleanPrompt.Substring(prefix.Length).Trim();
                if (!string.IsNullOrEmpty(stripped))
                {
                    cleanPrompt = stripped;
                }
                return true;
            }
        }

        // 4. Kiểm tra chứa từ khóa vẽ/tạo ảnh trong tiếng Việt hoặc tiếng Anh
        string[] vnDrawingPhrases = ["tạo ảnh", "vẽ hình", "vẽ tranh", "sinh ảnh"];
        foreach (var phrase in vnDrawingPhrases)
        {
            if (cleanPrompt.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (Regex.IsMatch(cleanPrompt,
            @"\b(draw|sketch|generate image)\b",
            RegexOptions.IgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool StripSlashCommand(ref string prompt)
    {
        string[] slashCommands = ["/image", "/img", "/draw", "/ve", "/taoanh"];
        foreach (var cmd in slashCommands)
        {
            if (prompt.StartsWith(cmd + " ", StringComparison.OrdinalIgnoreCase))
            {
                prompt = prompt.Substring(cmd.Length + 1).Trim();
                return true;
            }
            if (prompt.StartsWith(cmd + ":", StringComparison.OrdinalIgnoreCase))
            {
                prompt = prompt.Substring(cmd.Length + 1).Trim();
                return true;
            }
            if (string.Equals(prompt, cmd, StringComparison.OrdinalIgnoreCase))
            {
                prompt = string.Empty;
                return true;
            }
        }
        return false;
    }
}
