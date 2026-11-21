using System.Text.Json;
using System.Text.RegularExpressions;

namespace COW_AI.Services;

public sealed record VideoDurationEstimate(
    int WordCount,
    int HeadingCount,
    int BulletCount,
    bool HasComparisonStructure,
    string RecommendedVideoType,
    string RecommendedSequentialSubMode,
    string AppliedDurationPreset,
    double TargetDurationSeconds,
    double TargetMinutes,
    int RecommendedBeatCount,
    int TargetWordsPerBeat,
    string SummaryBadgeText
);

/// <summary>
/// Phân tích cấu trúc tài liệu Markdown (lấy cảm hứng từ pipeline slide-stream)
/// để tự động căn chỉnh số phút, số nhịp (beats) và gợi ý kiểu trình bày HyperFrames 3-Zone.
/// Tốc độ đọc chuẩn tiếng Việt: ~145 từ/phút (~2.42 từ/giây).
/// </summary>
public sealed class VideoDurationEstimator
{
    public const double VietnameseWordsPerMinute = 145.0;
    public const double WordsPerSecond = VietnameseWordsPerMinute / 60.0;

    private static readonly Regex WordRegex = new(@"[\p{L}\p{N}_]+", RegexOptions.Compiled);
    private static readonly Regex HeadingRegex = new(@"^\s*#{1,4}\s+.+$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex BulletRegex = new(@"^\s*(?:[-*+]|\d+\.)\s+.+$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex TableRowRegex = new(@"^\s*\|.+\|\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex ComparisonKeywordRegex = new(
        @"\b(so\s+sánh|đối\s+chiếu|phân\s+biệt|khác\s+nhau|khác\s+biệt|vs\.?|versus|ưu\s+nhược\s+điểm)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Làm sạch và chuẩn hóa văn bản trích xuất từ MarkItDown thành Source Markdown có cấu trúc rõ ràng.
    /// </summary>
    public string NormalizeSourceMarkdown(string? rawContent, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return string.Empty;
        }

        var text = rawContent.Replace("\r\n", "\n").Replace("\r", "\n");
        // Loại bỏ ký tự điều khiển vô hình (trừ newline và tab)
        text = Regex.Replace(text, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "");
        // Gom các dòng trống liên tiếp > 2 thành 2 dòng
        text = Regex.Replace(text, @"\n{3,}", "\n\n").Trim();

        if (!HeadingRegex.IsMatch(text) && !string.IsNullOrWhiteSpace(fileName))
        {
            var cleanTitle = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Replace('-', ' ').Trim();
            if (!string.IsNullOrWhiteSpace(cleanTitle))
            {
                text = $"# {cleanTitle}\n\n{text}";
            }
        }

        return text;
    }

    public VideoDurationEstimate Estimate(
        string? markdownContent,
        string? requestedVideoType = null,
        string? requestedSequentialSubMode = null,
        string? durationPreset = "auto",
        string? aspectRatio = "16:9")
    {
        var text = (markdownContent ?? string.Empty).Trim();
        var wordCount = string.IsNullOrEmpty(text) ? 0 : WordRegex.Matches(text).Count;
        var headingCount = string.IsNullOrEmpty(text) ? 0 : HeadingRegex.Matches(text).Count;
        var bulletCount = string.IsNullOrEmpty(text) ? 0 : BulletRegex.Matches(text).Count;
        var tableRowCount = string.IsNullOrEmpty(text) ? 0 : TableRowRegex.Matches(text).Count;

        bool hasComparison = ComparisonKeywordRegex.IsMatch(text) || tableRowCount >= 3;
        var effectiveVideoType = string.IsNullOrWhiteSpace(requestedVideoType)
            ? (hasComparison ? "comparison" : "sequential")
            : requestedVideoType.Trim().ToLowerInvariant();

        var normalizedAspect = string.IsNullOrWhiteSpace(aspectRatio) ? "16:9" : aspectRatio.Trim();
        var subMode = (requestedSequentialSubMode ?? "auto").Trim().ToLowerInvariant();
        if (subMode is not ("frame" or "kinetic"))
        {
            subMode = normalizedAspect == "16:9" && wordCount > 320 ? "kinetic" : "frame";
        }

        var preset = (durationPreset ?? "auto").Trim().ToLowerInvariant();
        double targetSeconds;
        int beatCount;

        switch (preset)
        {
            case "short_35s":
                targetSeconds = 36.0;
                beatCount = effectiveVideoType == "comparison" ? 12 : 7;
                break;

            case "medium_2_3m":
                targetSeconds = Math.Clamp(wordCount > 0 ? wordCount / WordsPerSecond * 0.75 : 135.0, 110.0, 180.0);
                beatCount = effectiveVideoType == "comparison" ? 12 : Math.Clamp(headingCount > 2 ? headingCount + 2 : 10, 8, 12);
                break;

            case "deep_4_6m":
                targetSeconds = Math.Clamp(wordCount > 0 ? wordCount / WordsPerSecond * 0.85 : 300.0, 240.0, 360.0);
                beatCount = Math.Clamp(headingCount > 4 ? headingCount + 2 : 15, 12, 18);
                break;

            default:
                preset = "auto";
                if (effectiveVideoType == "comparison" && wordCount <= 450)
                {
                    // Chuẩn skill auto-compare-video: 12 dòng / 30-40s
                    targetSeconds = 36.0;
                    beatCount = 12;
                }
                else if (wordCount <= 300)
                {
                    targetSeconds = 36.0;
                    beatCount = effectiveVideoType == "comparison" ? 12 : 7;
                }
                else if (wordCount <= 1100)
                {
                    // Bài giảng vừa: 1.5 - 3 phút
                    var spokenRatio = Math.Clamp(wordCount * 0.55 / WordsPerSecond, 90.0, 180.0);
                    targetSeconds = Math.Round(spokenRatio / 5.0) * 5.0;
                    beatCount = effectiveVideoType == "comparison"
                        ? 12
                        : Math.Clamp((int)Math.Round(targetSeconds / 14.0), 8, 12);
                }
                else
                {
                    // Tài liệu học thuật / chương dài: 3.5 - 6 phút (như DaiHoi13 / CuongLinh2011)
                    var spokenRatio = Math.Clamp(wordCount * 0.42 / WordsPerSecond, 210.0, 360.0);
                    targetSeconds = Math.Round(spokenRatio / 10.0) * 10.0;
                    beatCount = Math.Clamp((int)Math.Round(targetSeconds / 18.0), 12, 16);
                }
                break;
        }

        var targetMinutes = Math.Round(targetSeconds / 60.0, 1);
        var avgClipSeconds = Math.Max(2.6, (targetSeconds - 1.5) / Math.Max(1, beatCount) - 0.38);
        var targetWordsPerBeat = Math.Clamp((int)Math.Round(avgClipSeconds * WordsPerSecond), 10, 48);

        var minsLabel = targetSeconds < 60
            ? $"~{Math.Round(targetSeconds)} giây"
            : $"~{targetMinutes:0.#} phút ({Math.Round(targetSeconds)}s)";
        var skillBadge = effectiveVideoType == "comparison"
            ? "So sánh 3-Zone (12 nhịp)"
            : (subMode == "kinetic" ? "Tuần tự 16:9 Kinetic B-Roll" : "Tuần tự 1-Khung + MC");

        var summaryBadge = $"{wordCount} từ · {Math.Max(1, headingCount)} đề mục · Thời lượng tự căn chỉnh: {minsLabel} ({beatCount} phân đoạn · {skillBadge})";

        return new VideoDurationEstimate(
            WordCount: wordCount,
            HeadingCount: headingCount,
            BulletCount: bulletCount,
            HasComparisonStructure: hasComparison,
            RecommendedVideoType: effectiveVideoType,
            RecommendedSequentialSubMode: subMode,
            AppliedDurationPreset: preset,
            TargetDurationSeconds: targetSeconds,
            TargetMinutes: targetMinutes,
            RecommendedBeatCount: beatCount,
            TargetWordsPerBeat: targetWordsPerBeat,
            SummaryBadgeText: summaryBadge
        );
    }

    public static string ApplyPhonetics(string? text, IReadOnlyDictionary<string, string>? dictionary)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var clean = Regex.Replace(text, @"\[/?kw\]", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"<[^>]+>", "");
        if (dictionary == null || dictionary.Count == 0) return clean.Trim();

        foreach (var kv in dictionary.OrderByDescending(k => k.Key.Length))
        {
            var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(kv.Key)}(?![\p{{L}}\p{{N}}])";
            clean = Regex.Replace(clean, pattern, kv.Value, RegexOptions.IgnoreCase);
        }

        return clean.Trim();
    }
}
