using System.Text;
using MarkItDown;

namespace COW_AI.Services;

/// <summary>
/// Bộ chuyển đổi mọi định dạng tài liệu (.pdf, .docx, .pptx, .xlsx, .csv, .html, .epub, .ipynb, .json, .xml, .txt, .md)
/// sang Markdown tối ưu cho LLM bằng thư viện chuẩn ManagedCode.MarkItDown (v10.0.8).
/// </summary>
public static class DocumentTextExtractor
{
    private static readonly MarkItDownClient _markItDown = new();

    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pptx", ".docx", ".xlsx", ".pdf", ".csv",
        ".html", ".htm", ".epub", ".ipynb", ".json", ".xml", ".txt", ".md"
    };

    public const long MaxBytes = 100 * 1024 * 1024; // 100 MB
    public const int MaxExtractedChars = 60_000;

    public static bool IsAllowed(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? string.Empty);
        return !string.IsNullOrWhiteSpace(ext) && AllowedExtensions.Contains(ext);
    }

    public static async Task<(string Content, string? Warning)> ExtractAsync(
        IFormFile file,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        try
        {
            await using var stream = file.OpenReadStream();

            // Với tệp thuần văn bản (.txt, .md), đọc trực tiếp UTF-8 nhanh hoặc qua MarkItDown
            if (ext is ".txt" or ".md")
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var raw = TrimText(await reader.ReadToEndAsync(cancellationToken));
                return (raw, string.IsNullOrWhiteSpace(raw) ? "Tệp văn bản trống." : null);
            }

            var streamInfo = new StreamInfo(
                extension: ext,
                mimeType: string.IsNullOrWhiteSpace(file.ContentType) ? null : file.ContentType);

            await using var result = await _markItDown.ConvertAsync(stream, streamInfo, cancellationToken);
            var markdown = TrimText(result?.Markdown ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(markdown))
            {
                if (!string.IsNullOrWhiteSpace(result?.Title) &&
                    !markdown.StartsWith("#", StringComparison.Ordinal))
                {
                    markdown = $"# {result.Title.Trim()}\n\n{markdown}";
                }
                return (markdown, null);
            }

            return (string.Empty, "Tài liệu không có văn bản trích xuất được sang Markdown (có thể chỉ chứa hình ảnh scan).");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "ManagedCode.MarkItDown không thể chuyển đổi tệp {FileName} sang Markdown.", file.FileName);
            return (string.Empty, "Không thể chuyển đổi cấu trúc tệp này sang Markdown (tệp có thể có mật khẩu hoặc bị hỏng).");
        }
    }

    private static string TrimText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var normalized = raw.Replace("\r\n", "\n").Trim();
        return normalized.Length > MaxExtractedChars
            ? normalized[..MaxExtractedChars] + "\n\n... [Nội dung đã được cắt ngắn ở mức 60.000 ký tự]"
            : normalized;
    }
}
