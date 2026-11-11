using System.Text.RegularExpressions;

namespace COW_AI.Services;

public class IntentDispatcherService
{
    private readonly ILogger<IntentDispatcherService>? _logger;

    /// <summary>
    /// Ngăn nhận diện nhầm các câu hỏi lập trình, vẽ sơ đồ kỹ thuật (UML/PlantUML/Mermaid/biểu đồ) hoặc câu hỏi tra cứu kiến thức thành tạo ảnh nghệ thuật.
    /// </summary>
    private static readonly Regex TechnicalOrQuestionExclusionRegex = new(
        @"(?:\b(sơ\s+đồ|biểu\s+đồ|lưu\s+đồ|đồ\s+thị|bảng\s+biểu|uml|plantuml|mermaid|flowchart|sequence\s+diagram|class\s+diagram|activity\s+diagram|use\s*case|erd|chart\.?js|matplotlib|ascii\s+art|qr\s*code|mã\s+qr|viết\s+code|đoạn\s+code|mã\s+nguồn|viết\s+hàm|viết\s+chương\s+trình|thuật\s+toán)\b|\bc#(?!\w)|\bc\+\+(?!\w)|^\s*(?:lịch\s+sử|giải\s+thích|phân\s+tích|so\s+sánh|phân\s+biệt|định\s+nghĩa|khái\s+niệm|tại\s+sao|vì\s+sao|như\s+thế\s+nào|làm\s+sao|làm\s+thế\s+nào|cách\s+nào|hướng\s+dẫn|top\s+\d+|danh\s+sách|what\s+is|what\s+are|how\s+to|how\s+does|why\s+is|why\s+do|explain\s+|history\s+of|difference\s+between)\b|\b(?:là\s+gì|như\s+thế\s+nào)\s*\??\s*$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

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

        // Không chuyển sang tạo ảnh nếu câu lệnh thực chất là vẽ sơ đồ kỹ thuật (UML/Mermaid/biểu đồ), viết code hoặc câu hỏi kiến thức
        if (TechnicalOrQuestionExclusionRegex.IsMatch(cleanPrompt))
        {
            return false;
        }

        // 3. Kiểm tra các tiền tố tự nhiên phổ biến (bảo toàn chất liệu nghệ thuật như "tranh sơn dầu", "tranh màu nước")
        var tranhPrefixMatch = Regex.Match(cleanPrompt, @"^(?:hãy\s+)?vẽ\s+(tranh\b.*)$", RegexOptions.IgnoreCase);
        if (tranhPrefixMatch.Success)
        {
            var tranhRemainder = tranhPrefixMatch.Groups[1].Value.Trim();
            if (PromptService.StartsWithPreservedArtMedium(tranhRemainder))
            {
                cleanPrompt = tranhRemainder;
                return true;
            }

            var strippedTranh = Regex.Replace(tranhRemainder, @"^tranh\s*:?\s*", "", RegexOptions.IgnoreCase).Trim();
            if (!string.IsNullOrEmpty(strippedTranh))
            {
                cleanPrompt = strippedTranh;
            }
            return true;
        }

        string[] naturalPrefixes = [
            "tạo ảnh:", "tạo ảnh ", "hãy tạo ảnh ",
            "hãy tạo một bức ảnh ", "tạo một bức ảnh ",
            "tạo cho tôi một bức ảnh ", "tạo cho tôi bức ảnh ", "tạo cho tôi ảnh ", "tạo cho tôi hình ",
            "tạo cho tôi một bức tranh ", "tạo cho tôi bức tranh ",
            "tạo giúp tôi một bức ảnh ", "tạo giúp tôi bức ảnh ", "tạo giúp tôi ảnh ", "tạo giúp tôi hình ",
            "tạo giùm tôi bức ảnh ", "tạo giùm tôi ảnh ",
            "tạo cho mình bức ảnh ", "tạo cho mình ảnh ",
            "tạo ảnh cho tôi ",
            "vẽ tranh:", "vẽ tranh ", "hãy vẽ tranh ",
            "vẽ hình:", "vẽ hình ", "hãy vẽ hình ",
            "hãy vẽ cho tôi ", "vẽ cho tôi ", "vẽ một ", "hãy vẽ ", "vẽ ",
            "vẽ giúp tôi ", "vẽ hộ tôi ", "vẽ giùm tôi ", "vẽ cho mình ",
            "sinh ảnh ", "tạo hình ảnh ", "phác họa ", "hãy phác họa ",
            "thiết kế ảnh ", "thiết kế hình ảnh ", "render ảnh ",
            "tạo hình nền ", "tạo wallpaper ", "vẽ hình nền ", "vẽ wallpaper ",
            "generate an image of ", "generate image of ", "generate image ",
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
        string[] vnDrawingPhrases = ["tạo ảnh", "vẽ hình", "vẽ tranh", "sinh ảnh", "phác họa", "thiết kế ảnh", "render ảnh"];
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

    /// <summary>
    /// Tự động trích xuất kích thước và tỷ lệ khung hình (1:1, 16:9, 9:16) từ nội dung câu lệnh người dùng.
    /// </summary>
    public static (int width, int height) ExtractDimensions(string prompt, out string cleanPrompt)
    {
        cleanPrompt = prompt?.Trim() ?? string.Empty;
        var p = cleanPrompt.ToLowerInvariant();

        // 1. Tỷ lệ dọc (9:16 - 720x1280) cho hình nền điện thoại, story, chân dung dài
        if (Regex.IsMatch(p, @"(?:\b(hình nền điện thoại|wallpaper điện thoại|ảnh dọc|khổ dọc|portrait|phone wallpaper|wallpaper phone)\b|--ar\s+9:16|9:16)"))
        {
            cleanPrompt = Regex.Replace(cleanPrompt, @"(?:\b(hình nền điện thoại|wallpaper điện thoại|ảnh dọc|khổ dọc|phone wallpaper|wallpaper phone)\b|--ar\s+9:16|9:16)", "", RegexOptions.IgnoreCase).Trim();
            return (720, 1280);
        }

        // 2. Tỷ lệ ngang (16:9 - 1280x720) cho hình nền máy tính, desktop, phong cảnh widescreen
        if (Regex.IsMatch(p, @"(?:\b(hình nền máy tính|wallpaper máy tính|wallpaper pc|desktop wallpaper|ảnh ngang|khổ ngang|widescreen)\b|--ar\s+16:9|16:9)"))
        {
            cleanPrompt = Regex.Replace(cleanPrompt, @"(?:\b(hình nền máy tính|wallpaper máy tính|wallpaper pc|desktop wallpaper|ảnh ngang|khổ ngang|widescreen)\b|--ar\s+16:9|16:9)", "", RegexOptions.IgnoreCase).Trim();
            return (1280, 720);
        }

        // 3. Tỷ lệ vuông mặc định (1:1 - 1024x1024)
        cleanPrompt = Regex.Replace(cleanPrompt, @"(?:\b(khổ vuông|square|avatar|ảnh đại diện)\b|--ar\s+1:1|1:1)", "", RegexOptions.IgnoreCase).Trim();
        return (1024, 1024);
    }

    private static bool StripSlashCommand(ref string prompt)
    {
        string[] slashCommands = ["/image", "/img", "/draw", "/ve", "/taoanh", "/anh"];
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
