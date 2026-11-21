using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using COW_AI.Data;
using COW_AI.Models;

namespace COW_AI.Services;

public class AutoVideoService : IAutoVideoService
{
    private readonly AppDbContext _db;
    private readonly OllamaService _ollama;
    private readonly OpenRouterService _openRouter;
    private readonly IVieNeuTtsService _vieNeuTts;
    private readonly PollinationsService _pollinations;
    private readonly VideoDurationEstimator _estimator;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<AutoVideoService> _logger;
    private readonly IReadOnlyDictionary<string, string> _phonetics;

    public AutoVideoService(
        AppDbContext db,
        OllamaService ollama,
        OpenRouterService openRouter,
        IVieNeuTtsService vieNeuTts,
        PollinationsService pollinations,
        VideoDurationEstimator estimator,
        IWebHostEnvironment env,
        IConfiguration config,
        ILogger<AutoVideoService> logger)
    {
        _db = db;
        _ollama = ollama;
        _openRouter = openRouter;
        _vieNeuTts = vieNeuTts;
        _pollinations = pollinations;
        _estimator = estimator;
        _env = env;
        _config = config;
        _logger = logger;
        _phonetics = LoadPhoneticsDictionary();
    }

    private IReadOnlyDictionary<string, string> LoadPhoneticsDictionary()
    {
        try
        {
            var path = Path.Combine(_env.ContentRootPath, "Prompts", "VideoSkills", "tts-phonetics.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null)
                {
                    return new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể nạp từ điển phiên âm tts-phonetics.json");
        }
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private string LoadSkillTemplate(string skillFileName)
    {
        try
        {
            var path = Path.Combine(_env.ContentRootPath, "Prompts", "VideoSkills", skillFileName);
            if (File.Exists(path))
            {
                return File.ReadAllText(path, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể nạp skill template {SkillFile}", skillFileName);
        }
        return string.Empty;
    }

    private string LoadPromptTemplate(string promptFileName)
    {
        try
        {
            var path = Path.Combine(_env.ContentRootPath, "Prompts", "VideoSkills", promptFileName);
            if (!File.Exists(path))
            {
                path = Path.Combine(_env.ContentRootPath, "Prompts", promptFileName);
            }
            if (File.Exists(path))
            {
                return File.ReadAllText(path, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể nạp prompt template {PromptFile}", promptFileName);
        }
        return string.Empty;
    }

    public static string ResolveSkillKey(string videoType, string sequentialSubMode, string aspectRatio, int wordCount)
    {
        if (string.Equals(videoType, "comparison", StringComparison.OrdinalIgnoreCase))
        {
            return "compare-3zone";
        }

        var sub = (sequentialSubMode ?? "auto").Trim().ToLowerInvariant();
        if (sub == "kinetic") return "sequential-kinetic-16x9";
        if (sub == "frame") return "sequential-singleframe";

        return string.Equals(aspectRatio?.Trim(), "16:9", StringComparison.OrdinalIgnoreCase) && wordCount > 320
            ? "sequential-kinetic-16x9"
            : "sequential-singleframe";
    }

    public async Task<string> GenerateMarkdownPlanAsync(
        string sourceContent,
        string videoType,
        string voice,
        List<string> avatarUrls,
        CancellationToken ct = default,
        string? sequentialSubMode = "auto",
        string? durationPreset = "auto",
        string? aspectRatio = "16:9",
        string? projectTitle = null)
    {
        var rawInput = string.IsNullOrWhiteSpace(sourceContent)
            ? "So sánh Trí tuệ Nhân tạo tổng quát (AGI) và Trí tuệ Nhân tạo hẹp (Narrow AI)"
            : sourceContent.Trim();

        var normalizedSourceMd = _estimator.NormalizeSourceMarkdown(rawInput, projectTitle);
        var estimate = _estimator.Estimate(normalizedSourceMd, videoType, sequentialSubMode, durationPreset, aspectRatio);
        var isComparison = string.Equals(videoType, "comparison", StringComparison.OrdinalIgnoreCase);
        var skillKey = ResolveSkillKey(videoType, estimate.RecommendedSequentialSubMode, aspectRatio ?? "16:9", estimate.WordCount);
        var skillTemplate = LoadSkillTemplate($"{skillKey}.md");

        var avatarCount = avatarUrls?.Count ?? 0;
        var avatarMode = avatarCount > 0
            ? $"Có {avatarCount} ảnh giảng viên / MC AI"
            : (skillKey == "sequential-kinetic-16x9"
                ? "Chế độ Widescreen Kinetic B-Roll (Không hiển thị avatar)"
                : "2D Host Avatar mặc định");

        var titleText = !string.IsNullOrWhiteSpace(projectTitle)
            ? projectTitle.Replace('_', ' ').Trim()
            : ExtractTopicHeadline(normalizedSourceMd);

        // Chắt lọc nội dung nguồn (~4.000 ký tự) để vừa vặn hoàn hảo trong cửa sổ ngữ cảnh LLM
        // tránh làm mô hình 7B bị quá tải bộ nhớ và tràn context
        var distilledSourceMd = DistillSourceForScript(normalizedSourceMd, titleText, 5000);

        // Nạp Prompt Template tách riêng từ Prompts/VideoSkills/AutoVideoPlanPrompt.txt
        // tuân thủ triệt để nguyên tắc Zero Hardcoded Prompts của COW.AI
        var promptTemplate = LoadPromptTemplate("AutoVideoPlanPrompt.txt");
        var prompt = promptTemplate
            .Replace("{{SKILL_KEY}}", skillKey)
            .Replace("{{SKILL_TEMPLATE}}", skillTemplate)
            .Replace("{{PROJECT_TITLE}}", titleText)
            .Replace("{{WORD_COUNT}}", estimate.WordCount.ToString())
            .Replace("{{HEADING_COUNT}}", estimate.HeadingCount.ToString())
            .Replace("{{TARGET_DURATION}}", estimate.TargetDurationSeconds.ToString("0"))
            .Replace("{{TARGET_MINUTES}}", estimate.TargetMinutes.ToString("0.#"))
            .Replace("{{WORDS_PER_MINUTE}}", "145")
            .Replace("{{RECOMMENDED_BEATS}}", estimate.RecommendedBeatCount.ToString())
            .Replace("{{WORDS_PER_BEAT}}", estimate.TargetWordsPerBeat.ToString())
            .Replace("{{ASPECT_RATIO}}", aspectRatio ?? "16:9")
            .Replace("{{VOICE}}", voice)
            .Replace("{{AVATAR_MODE}}", avatarMode)
            .Replace("{{SOURCE_CONTENT}}", distilledSourceMd)
            .Replace("{{DEFAULT_BEAT_LABEL}}", isComparison ? "Hook A" : "Mở đầu")
            .Replace("{{BEAT_ROLES}}", isComparison ? "hook | question | reveal-a | reveal-b | compare | payoff" : "hook | concept | step | stat | payoff")
            .Replace("{{AVAILABLE_EFFECTS}}", skillKey == "sequential-kinetic-16x9" ? "slide-in | typewriter | stat-counter | ken-burns" : "spring-pop | typewriter | ken-burns | zoom-in")
            .Replace("{{AVAILABLE_POSES}}", isComparison ? "point-left | point-right | shrug | center" : "right | center | none");

        string llmBody = string.Empty;
        var studioModel = _config["Ollama:StudioModel"] ?? "qwen2.5:7b";
        foreach (var candidateModel in new[] { studioModel, "cow-ai:latest" }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                llmBody = await _ollama.ChatAsync(prompt, candidateModel, ct);
                if (!string.IsNullOrWhiteSpace(llmBody) && llmBody.Length >= 80) break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ollama model {Model} không phản hồi khi tạo kịch bản video, thử model tiếp theo...", candidateModel);
            }
        }

        if (string.IsNullOrWhiteSpace(llmBody) || llmBody.Length < 80)
        {
            try
            {
                llmBody = await _openRouter.ChatAsync(prompt, "google/gemini-2.0-flash-exp:free", ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenRouter không phản hồi. Chuyển sang bộ sinh kịch bản cấu trúc nội bộ.");
            }
        }

        var normalizedBody = NormalizeGeneratedScriptBody(llmBody, normalizedSourceMd, titleText, skillKey, isComparison, estimate, avatarUrls ?? new List<string>());
        return PrependYamlFrontmatterAndSummary(normalizedBody, titleText, skillKey, videoType, voice, aspectRatio ?? "16:9", estimate, avatarMode);
    }

    private string NormalizeGeneratedScriptBody(
        string? rawBody,
        string sourceMd,
        string titleText,
        string skillKey,
        bool isComparison,
        VideoDurationEstimate estimate,
        List<string> avatarUrls)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return BuildStructuredFallbackBody(sourceMd, titleText, skillKey, isComparison, estimate, avatarUrls);
        }

        var body = rawBody.Trim();
        // Bỏ YAML frontmatter thừa nếu LLM tự sinh
        body = Regex.Replace(body, @"^\s*---[\s\S]*?---\s*", "");
        // Loại bỏ hoàn toàn chữ "Cảnh X" nếu LLM vô tình viết -> đổi thành "[Phần X]"
        body = Regex.Replace(
            body,
            @"^###\s+(?:Cảnh|Scene)\s+(\d+)\s*[:\-–]?\s*(.+)$",
            "### [Phần $1]: $2",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        if (!Regex.IsMatch(body, @"^###\s+", RegexOptions.Multiline))
        {
            return BuildStructuredFallbackBody(sourceMd, titleText, skillKey, isComparison, estimate, avatarUrls);
        }

        // Đảm bảo mỗi khối ### có trường Lời đọc TTS (ttsText) đã qua từ điển phiên âm
        var blocks = Regex.Split(body, @"(?=^###\s+)", RegexOptions.Multiline)
                          .Where(b => b.TrimStart().StartsWith("###", StringComparison.Ordinal))
                          .ToList();

        if (blocks.Count == 0)
        {
            return BuildStructuredFallbackBody(sourceMd, titleText, skillKey, isComparison, estimate, avatarUrls);
        }

        var sb = new StringBuilder();
        int idx = 1;
        foreach (var rawBlock in blocks)
        {
            var block = rawBlock.Trim();
            // Đảm bảo không còn chữ Cảnh trên dòng tiêu đề
            var lines = block.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            lines[0] = Regex.Replace(lines[0], @"\bCảnh\s*(\d+)\b", "Phần $1", RegexOptions.IgnoreCase);
            if (!lines[0].Contains("[Phần", StringComparison.OrdinalIgnoreCase) && !lines[0].Contains("[Beat", StringComparison.OrdinalIgnoreCase))
            {
                var cleanHead = lines[0].Replace("###", "").Trim().TrimStart(':', '-', ' ');
                lines[0] = $"### [Phần {idx}]: {cleanHead}";
            }

            var joined = string.Join("\n", lines);
            var captionMatch = Regex.Match(joined, @"-\s*\*\*(?:Lời hiển thị \(Caption\)|Lời hiển thị|Lời thoại)\*\*[:\s]*(.+)", RegexOptions.IgnoreCase);
            var rawCaption = captionMatch.Success ? captionMatch.Groups[1].Value.Trim() : titleText;
            var cleanCaption = CleanMarkdownArtifacts(rawCaption, preserveKw: true);

            var ttsMatch = Regex.Match(joined, @"-\s*\*\*Lời đọc TTS(?: \(ttsText\))?\*\*[:\s]*(.+)", RegexOptions.IgnoreCase);
            var rawTts = ttsMatch.Success ? ttsMatch.Groups[1].Value.Trim() : CleanMarkdownArtifacts(rawCaption, preserveKw: false);
            var cleanTtsRaw = CleanMarkdownArtifacts(rawTts, preserveKw: false);
            var phoneticTts = CleanMarkdownArtifacts(VideoDurationEstimator.ApplyPhonetics(cleanTtsRaw, _phonetics), preserveKw: false);

            if (captionMatch.Success)
            {
                joined = Regex.Replace(
                    joined,
                    @"-\s*\*\*(?:Lời hiển thị \(Caption\)|Lời hiển thị|Lời thoại)\*\*[:\s]*(.+)",
                    $"- **Lời hiển thị (Caption)**: {cleanCaption}",
                    RegexOptions.IgnoreCase);
            }

            if (ttsMatch.Success)
            {
                joined = Regex.Replace(
                    joined,
                    @"-\s*\*\*Lời đọc TTS(?: \(ttsText\))?\*\*[:\s]*(.+)",
                    $"- **Lời đọc TTS (ttsText)**: {phoneticTts}",
                    RegexOptions.IgnoreCase);
            }
            else
            {
                joined += $"\n- **Lời đọc TTS (ttsText)**: {phoneticTts}";
            }

            sb.AppendLine(joined);
            sb.AppendLine();
            idx++;
        }

        return sb.ToString().Trim();
    }

    private string PrependYamlFrontmatterAndSummary(
        string scriptBody,
        string titleText,
        string skillKey,
        string videoType,
        string voice,
        string aspectRatio,
        VideoDurationEstimate estimate,
        string avatarMode)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"title: \"{titleText.Replace("\"", "'")}\"");
        sb.AppendLine($"skill: \"{skillKey}\"");
        sb.AppendLine($"video_type: \"{videoType}\"");
        sb.AppendLine($"sequential_sub_mode: \"{estimate.RecommendedSequentialSubMode}\"");
        sb.AppendLine($"aspect_ratio: \"{aspectRatio}\"");
        sb.AppendLine($"voice: \"{voice}\"");
        sb.AppendLine($"word_count_source: {estimate.WordCount}");
        sb.AppendLine($"target_duration_seconds: {estimate.TargetDurationSeconds.ToString("0.0", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"target_minutes: {estimate.TargetMinutes.ToString("0.1", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"recommended_beats: {estimate.RecommendedBeatCount}");
        sb.AppendLine("words_per_minute: 145");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# KỊCH BẢN VIDEO CHI TIẾT: {titleText}");
        sb.AppendLine();
        sb.AppendLine($"- **Kiểu trình bày (3-Zone Skill)**: `{skillKey}` ({(videoType == "comparison" ? "So sánh / Đối chiếu 2 cột" : (skillKey == "sequential-kinetic-16x9" ? "Tuần tự 16:9 Kinetic Typography + B-Roll" : "Tuần tự 3-Zone 1-Khung + MC Avatar"))})");
        sb.AppendLine($"- **Thời lượng tự căn chỉnh**: ~{estimate.TargetMinutes:0.#} phút ({estimate.TargetDurationSeconds:0} giây · {estimate.RecommendedBeatCount} phân đoạn · từ {estimate.WordCount} từ tài liệu gốc)");
        sb.AppendLine($"- **Giọng đọc VieNeu 48kHz**: {voice}");
        sb.AppendLine($"- **Chế độ Nhân vật**: {avatarMode}");
        sb.AppendLine();
        sb.AppendLine(scriptBody);
        return sb.ToString();
    }

    private string BuildStructuredFallbackBody(
        string sourceMd,
        string topic,
        string skillKey,
        bool isComparison,
        VideoDurationEstimate estimate,
        List<string> avatarUrls)
    {
        var sb = new StringBuilder();
        var segments = ExtractSourceSegments(sourceMd, topic, isComparison ? Math.Max(6, Math.Min(12, estimate.RecommendedBeatCount)) : estimate.RecommendedBeatCount);
        var perBeatDuration = Math.Round(Math.Clamp(estimate.TargetDurationSeconds / Math.Max(1, segments.Count), 3.0, 22.0), 1);

        if (isComparison)
        {
            var (conceptA, conceptB) = ExtractComparisonPairNames(topic, sourceMd);
            string[] roles = ["hook", "hook", "question", "reveal-a", "reveal-a", "reveal-b", "reveal-b", "compare", "payoff"];
            string[] badges = ["HOOK A", "HOOK B", "NÚT THẮT", $"GIẢI MÃ {conceptA}", $"ỨNG DỤNG {conceptA}", $"GIẢI MÃ {conceptB}", $"ỨNG DỤNG {conceptB}", "ĐỐI CHIẾU TRỰC TIẾP", "ĐÚC KẾT"];
            string[] poses = ["point-left", "point-right", "shrug", "point-left", "point-left", "point-right", "point-right", "center", "center"];

            for (int i = 0; i < segments.Count; i++)
            {
                var role = roles[Math.Min(i, roles.Length - 1)];
                var badge = badges[Math.Min(i, badges.Length - 1)];
                var pose = poses[Math.Min(i, poses.Length - 1)];
                var seg = segments[i];
                var cleanSegText = CleanMarkdownArtifacts(seg.Text, preserveKw: false);
                var caption = HighlightPrimaryKeyword(cleanSegText, i < 2 ? (i == 0 ? conceptA : conceptB) : null);
                var tts = CleanMarkdownArtifacts(VideoDurationEstimator.ApplyPhonetics(cleanSegText, _phonetics), preserveKw: false);

                sb.AppendLine($"### [Phần {i + 1} - {badge}]: {CleanMarkdownArtifacts(seg.Heading, false)}");
                sb.AppendLine($"- **Loại bố cục**: {skillKey}");
                sb.AppendLine($"- **Vai trò nhịp**: {role}");
                sb.AppendLine($"- **Huy hiệu chuyên mục**: {CleanMarkdownArtifacts(badge, false)}");
                sb.AppendLine($"- **Lời hiển thị (Caption)**: {caption}");
                sb.AppendLine($"- **Lời đọc TTS (ttsText)**: {tts}");
                sb.AppendLine($"- **Prompt hình ảnh**: Dual comparison infographic card for {conceptA} versus {conceptB}, clean dark indigo cybernetic studio UI, 8k, cinematic");
                sb.AppendLine($"- **Hiệu ứng**: {(i % 2 == 0 ? "spring-pop" : "zoom-in")}");
                sb.AppendLine($"- **Thời lượng**: {perBeatDuration.ToString("0.0", CultureInfo.InvariantCulture)}s");
                sb.AppendLine($"- **Vị trí nhân vật**: {pose}");
                sb.AppendLine($"- **Âm thanh SFX**: {(i == 0 || role == "compare" ? "whoosh" : (role == "payoff" ? "hit" : "click"))}");
                sb.AppendLine();
            }
        }
        else
        {
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                var cleanSegText = CleanMarkdownArtifacts(seg.Text, preserveKw: false);
                var role = i == 0 ? "hook" : (i == segments.Count - 1 ? "payoff" : (Regex.IsMatch(cleanSegText, @"\d+") ? "stat" : "step"));
                var badge = i == 0 ? "MỞ ĐẦU" : (i == segments.Count - 1 ? "TỔNG KẾT" : $"TRỌNG TÂM {i}");
                var effect = skillKey == "sequential-kinetic-16x9"
                    ? (role == "stat" ? "stat-counter" : (i % 2 == 0 ? "slide-in" : "typewriter"))
                    : (i % 2 == 0 ? "spring-pop" : "typewriter");
                var caption = HighlightPrimaryKeyword(cleanSegText, null);
                var tts = CleanMarkdownArtifacts(VideoDurationEstimator.ApplyPhonetics(cleanSegText, _phonetics), preserveKw: false);

                sb.AppendLine($"### [Phần {i + 1} - {badge}]: {CleanMarkdownArtifacts(seg.Heading, false)}");
                sb.AppendLine($"- **Loại bố cục**: {skillKey}");
                sb.AppendLine($"- **Vai trò nhịp**: {role}");
                sb.AppendLine($"- **Huy hiệu chuyên mục**: {CleanMarkdownArtifacts(badge, false)}");
                sb.AppendLine($"- **Lời hiển thị (Caption)**: {caption}");
                sb.AppendLine($"- **Lời đọc TTS (ttsText)**: {tts}");
                sb.AppendLine($"- **Prompt hình ảnh**: Modern educational keynote visualization of {seg.Heading}, dark high-contrast technical lighting, 8k, cinematic");
                sb.AppendLine($"- **Hiệu ứng**: {effect}");
                sb.AppendLine($"- **Thời lượng**: {perBeatDuration.ToString("0.0", CultureInfo.InvariantCulture)}s");
                sb.AppendLine($"- **Vị trí nhân vật**: {(skillKey == "sequential-kinetic-16x9" && avatarUrls.Count == 0 ? "none" : "right")}");
                sb.AppendLine($"- **Âm thanh SFX**: {(i == 0 ? "whoosh" : (role == "payoff" ? "hit" : "click"))}");
                sb.AppendLine();
            }
        }

        return sb.ToString().Trim();
    }

    private static string ExtractTopicHeadline(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "Bài giảng AI Studio";

        // 1. Tìm tên đề tài được đặt trong dấu ngoặc kép hoặc sau từ khóa chủ đạo
        var quotedMatch = Regex.Match(markdown, @"(?:trò chơi|game|đề tài|dự án|hệ thống|báo cáo)\s+[“""']([^”""'\n]{3,60})[”""']", RegexOptions.IgnoreCase);
        if (quotedMatch.Success)
        {
            var found = quotedMatch.Groups[1].Value.Trim();
            if (found.Length >= 4 && !found.Contains("Slide", StringComparison.OrdinalIgnoreCase))
                return $"Đề tài {found}";
        }

        // 2. Tìm dòng in đậm lớn loại trừ mục lục và danh mục
        var boldMatches = Regex.Matches(markdown, @"\*\*(?:BÁO CÁO|ĐỒ ÁN|TỔNG QUAN|CHỦ ĐỀ|ĐỀ TÀI)?\s*([^\*\n]{4,80})\*\*", RegexOptions.IgnoreCase);
        foreach (Match bm in boldMatches)
        {
            var b = bm.Groups[1].Value.Trim();
            if (IsIgnoredHeadline(b)) continue;
            return b.Length > 80 ? b[..80] : b;
        }

        // 3. Tìm Heading 1 hoặc Heading 2
        var hMatches = Regex.Matches(markdown, @"^\s*#+\s+(.+)$", RegexOptions.Multiline);
        foreach (Match hm in hMatches)
        {
            var h = hm.Groups[1].Value.Trim();
            if (IsIgnoredHeadline(h)) continue;
            return h.Length > 80 ? h[..80] : h;
        }

        var lines = markdown.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().Trim('#', '*', ' ', '-'))
            .Where(l => l.Length > 5 && !IsIgnoredHeadline(l))
            .ToList();

        var firstValid = lines.FirstOrDefault() ?? "Bài giảng AI Studio";
        return firstValid.Length > 80 ? firstValid[..80] : firstValid;
    }

    private static bool IsIgnoredHeadline(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var lower = text.ToLowerInvariant();
        return lower.Contains("mục lục") ||
               lower.Contains("danh mục") ||
               lower.Contains("lời nói đầu") ||
               lower.Contains("lời cảm ơn") ||
               lower.Contains("lời cam đoan") ||
               lower.Contains("slide") ||
               lower.Contains("pageref") ||
               lower.Contains("_toc") ||
               lower.Contains("toc \\o") ||
               lower.StartsWith("---");
    }

    /// <summary>
    /// Loại bỏ triệt để mọi ký tự định dạng Markdown (*, **, #, |, ~, `, -, >, links, TOC refs)
    /// để đảm bảo phụ đề hiển thị sạch sẽ và âm thanh VieNeu-TTS / Edge-TTS đọc tự nhiên không bị lẫn ký tự cú pháp.
    /// </summary>
    public static string CleanMarkdownArtifacts(string? text, bool preserveKw = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var s = text;

        if (!preserveKw)
        {
            s = Regex.Replace(s, @"\[/?kw\]", "", RegexOptions.IgnoreCase);
        }

        // Xóa markdown links [text](url) -> text
        s = Regex.Replace(s, @"\[([^\]]+)\]\([^\)]+\)", "$1");
        // Xóa image links ![alt](url) -> rỗng
        s = Regex.Replace(s, @"!\[[^\]]*\]\([^\)]+\)", "");
        // Xóa headers: #, ##, ### ở đầu dòng hoặc sau dấu xuống dòng
        s = Regex.Replace(s, @"(^|\n)\s*#{1,6}\s*", "$1");
        // Xóa table pipes | và thay bằng khoảng trắng
        s = s.Replace('|', ' ');
        // Xóa bold/italic ***, **, *, ___, __, _
        s = Regex.Replace(s, @"\*{1,3}(.*?)\*{1,3}", "$1");
        s = Regex.Replace(s, @"_{1,3}(.*?)_{1,3}", "$1");
        // Xóa strikethrough ~~
        s = Regex.Replace(s, @"~~(.*?)~~", "$1");
        // Xóa inline code `code`
        s = Regex.Replace(s, @"`([^`]+)`", "$1");
        // Xóa bullet markers -, *, + ở đầu dòng
        s = Regex.Replace(s, @"(^|\n)\s*[\-\*\+]\s+", "$1");
        // Xóa số thứ tự 1., 2. ở đầu dòng
        s = Regex.Replace(s, @"(^|\n)\s*\d+[\.\)]\s+", "$1");
        // Xóa blockquote >
        s = Regex.Replace(s, @"(^|\n)\s*>\s*", "$1");
        // Xóa horizontal rule --- hoặc ***
        s = Regex.Replace(s, @"(^|\n)\s*[-*_]{3,}\s*($|\n)", "$1$2");
        // Xóa các mã TOC của Word: PAGEREF, _Toc..., \h \z \u
        s = Regex.Replace(s, @"PAGEREF\s+[^\s]+\s*(?:\\h|\\z|\\u)?(?:\s*\d+)?", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"TOC\s+\\[a-z0-9\s""]+", "", RegexOptions.IgnoreCase);
        // Chuẩn hóa khoảng trắng
        s = Regex.Replace(s, @"[ \t]+", " ");
        return s.Trim();
    }

    /// <summary>
    /// Chắt lọc thông minh các nội dung trọng tâm của tài liệu lớn (DOCX báo cáo khoa học, đồ án, đề tài):
    /// - Loại bỏ hoàn toàn các trang bìa, mục lục, danh mục hình ảnh/bảng biểu/viết tắt/lời cảm ơn
    /// - Phân rã theo từng Chương / Mục lớn (Chương 1..Chương 4)
    /// - Trích xuất tiêu đề chương, mục tiêu, kiến trúc, tính năng, số liệu kết quả và kết luận
    /// - Phân bổ dung lượng cân đối qua tất cả các chương (~5000-6000 ký tự) cho LLM.
    /// </summary>
    private static string DistillSourceForScript(string sourceMd, string topicTitle, int maxChars = 5500)
    {
        if (string.IsNullOrWhiteSpace(sourceMd)) return $"# {topicTitle}";

        // Tiền xử lý: loại bỏ các dòng mã trường Word và mục lục
        var cleaned = Regex.Replace(sourceMd, @"PAGEREF\s+[^\s]+\s*(?:\\h|\\z|\\u)?(?:\s*\d+)?", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"TOC\s+\\[a-z0-9\s""]+", "", RegexOptions.IgnoreCase);

        var rawParagraphs = cleaned.Replace("\r\n", "\n")
            .Split(new[] { "\n\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p) && p.Length >= 15 && !IsIgnoredHeadline(p))
            .ToList();

        // Tách các đoạn theo Chương nếu có
        var chapters = new List<List<string>>();
        List<string>? currentChapter = null;

        foreach (var p in rawParagraphs)
        {
            var isNewChapter = Regex.IsMatch(p, @"^(?:#+\s*)?(?:CHƯƠNG|PHẦN)\s+\d+", RegexOptions.IgnoreCase);
            if (isNewChapter)
            {
                currentChapter = new List<string> { p };
                chapters.Add(currentChapter);
            }
            else if (currentChapter != null)
            {
                currentChapter.Add(p);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# {topicTitle}\n");

        if (chapters.Count >= 2)
        {
            // Phân bổ ngân sách ký tự đều qua các chương
            int perChapterBudget = Math.Max(700, (maxChars - 500) / chapters.Count);
            foreach (var ch in chapters)
            {
                var chHeading = ch[0];
                sb.AppendLine($"\n## {chHeading}\n");
                int used = chHeading.Length;

                foreach (var line in ch.Skip(1))
                {
                    bool isKey = line.StartsWith('#') ||
                                 line.StartsWith('-') || line.StartsWith('*') || line.StartsWith('•') ||
                                 Regex.IsMatch(line, @"^\d+\.\d+") ||
                                 Regex.IsMatch(line, @"\d+\s*(?:%|giây|ms|màn|Scene|FPS|HP|cấp|bước|lớp)", RegexOptions.IgnoreCase) ||
                                 Regex.IsMatch(line, @"(?:mục tiêu|kiến trúc|tính năng|module|kết quả|đánh giá|kết luận|nổi bật|cốt lõi)", RegexOptions.IgnoreCase);

                    if (isKey || line.Length < 160)
                    {
                        if (used + line.Length + 2 > perChapterBudget) break;
                        sb.AppendLine($"- {line}");
                        used += line.Length + 3;
                    }
                }
            }
        }
        else
        {
            // Tài liệu không chia chương rõ ràng -> Lọc theo đoạn có giá trị thông tin cao
            int currentLength = sb.Length;
            foreach (var line in rawParagraphs)
            {
                bool isKey = line.StartsWith('#') ||
                             line.StartsWith('-') || line.StartsWith('*') || line.StartsWith('•') ||
                             Regex.IsMatch(line, @"\d+\s*(?:%|giây|ms|FPS|HP)", RegexOptions.IgnoreCase) ||
                             Regex.IsMatch(line, @"(?:mục tiêu|kiến trúc|tính năng|kết quả|đánh giá|kết luận)", RegexOptions.IgnoreCase);

                if (isKey)
                {
                    if (currentLength + line.Length + 2 > maxChars) break;
                    sb.AppendLine(line.StartsWith('#') ? line : $"- {line}");
                    currentLength += line.Length + 3;
                }
            }
        }

        if (sb.Length < 400)
        {
            // Fallback lấy trực tiếp các dòng sạch đầu tiên
            foreach (var line in rawParagraphs.Take(15))
            {
                sb.AppendLine($"- {line}");
                if (sb.Length > maxChars) break;
            }
        }

        return sb.ToString().Trim();
    }

    private static (string A, string B) ExtractComparisonPairNames(string topic, string sourceMd)
    {
        var vsMatch = Regex.Match(topic + " " + sourceMd, @"([\p{L}\p{N}.+#\- ]{2,25})\s+(?:vs\.?|và|với|hay)\s+([\p{L}\p{N}.+#\- ]{2,25})", RegexOptions.IgnoreCase);
        if (vsMatch.Success)
        {
            return (vsMatch.Groups[1].Value.Trim().ToUpperInvariant(), vsMatch.Groups[2].Value.Trim().ToUpperInvariant());
        }
        return ("KHÁI NIỆM A", "KHÁI NIỆM B");
    }

    private static List<(string Heading, string Text)> ExtractSourceSegments(string sourceMd, string topic, int targetCount)
    {
        var result = new List<(string Heading, string Text)>();
        var rawParagraphs = Regex.Split(sourceMd, @"\n\s*\n")
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p) && !IsIgnoredHeadline(p))
            .ToList();

        string currentHeading = topic;
        foreach (var p in rawParagraphs)
        {
            if (p.StartsWith('#'))
            {
                var h = p.TrimStart('#', ' ').Trim();
                if (!IsIgnoredHeadline(h)) currentHeading = h;
                continue;
            }
            var cleanLine = Regex.Replace(p, @"^\s*(?:[-*+]|\d+\.)\s*", "").Replace("\n", " ").Trim();
            if (cleanLine.Length < 12 || IsIgnoredHeadline(cleanLine)) continue;
            if (cleanLine.Length > 180) cleanLine = cleanLine[..177] + "...";
            result.Add((currentHeading.Length > 48 ? currentHeading[..48] : currentHeading, cleanLine));
        }

        while (result.Count < Math.Max(3, Math.Min(targetCount, 6)))
        {
            int n = result.Count + 1;
            result.Add((
                n == 1 ? $"Tổng quan {topic}" : (n == 2 ? "Nguyên lý & Đặc trưng cốt lõi" : "Ứng dụng & Đúc kết thực tiễn"),
                n == 1
                    ? $"Khám phá bức tranh tổng thể và mục tiêu trọng tâm của {topic}."
                    : (n == 2
                        ? $"Phân tích nguyên lý hoạt động, kiến trúc kỹ thuật và các điểm nhấn quan trọng nhất."
                        : $"Đúc kết bài học thực tiễn giúp áp dụng {topic} hiệu quả và chính xác.")
            ));
        }

        return result.Take(Math.Max(3, targetCount)).ToList();
    }

    private static string HighlightPrimaryKeyword(string text, string? preferredKeyword)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains("[kw]", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }
        if (!string.IsNullOrWhiteSpace(preferredKeyword) && text.Contains(preferredKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return Regex.Replace(text, Regex.Escape(preferredKeyword), m => $"[kw]{m.Value}[/kw]", RegexOptions.IgnoreCase);
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 4)
        {
            int kwIdx = Math.Min(words.Length - 1, 2);
            words[kwIdx] = $"[kw]{words[kwIdx]}[/kw]";
            return string.Join(" ", words);
        }
        return text;
    }

    public Task<List<Scene>> ParsePlanToScenesAsync(
        int projectId,
        string markdownPlan,
        CancellationToken ct = default)
    {
        var scenes = new List<Scene>();
        if (string.IsNullOrWhiteSpace(markdownPlan)) return Task.FromResult(scenes);

        // Bỏ YAML frontmatter trước khi tách phân đoạn
        var bodyOnly = Regex.Replace(markdownPlan, @"^\s*---[\s\S]*?---\s*", "");

        var sceneBlocks = Regex.Split(bodyOnly, @"(?=^###\s+)", RegexOptions.Multiline)
                               .Where(b => b.TrimStart().StartsWith("###", StringComparison.OrdinalIgnoreCase))
                               .ToList();

        int order = 1;
        foreach (var block in sceneBlocks)
        {
            var headerLine = block.Split('\n').FirstOrDefault()?.Trim() ?? "";
            // Hỗ trợ cả "### [Phần 1 - HOOK]: Tiêu đề" và định dạng cũ "### Cảnh 1: Tiêu đề"
            var rawHeader = Regex.Replace(headerLine, @"^###\s+", "").Trim();
            rawHeader = Regex.Replace(rawHeader, @"^(?:Cảnh|Scene)\s*\d+\s*[:\-–]?\s*", "", RegexOptions.IgnoreCase);

            string badgeFromHeader = $"PHẦN {order}";
            var bracketMatch = Regex.Match(rawHeader, @"^\[(?:Phần|Beat)?\s*\d*\s*[\-–:]?\s*([^\]]+)\]\s*[:\-–]?\s*(.*)$", RegexOptions.IgnoreCase);
            string title;
            if (bracketMatch.Success)
            {
                badgeFromHeader = CleanMarkdownArtifacts(bracketMatch.Groups[1].Value.Trim(), false);
                title = string.IsNullOrWhiteSpace(bracketMatch.Groups[2].Value)
                    ? badgeFromHeader
                    : CleanMarkdownArtifacts(bracketMatch.Groups[2].Value.Trim(), false);
            }
            else
            {
                title = string.IsNullOrWhiteSpace(rawHeader) ? $"Phần {order}" : CleanMarkdownArtifacts(rawHeader, false);
            }
            // Tuyệt đối không để sót chữ "Cảnh X"
            title = Regex.Replace(title, @"^(?:Cảnh|Scene)\s*\d+\s*[:\-–]?\s*", "", RegexOptions.IgnoreCase).Trim();
            if (string.IsNullOrWhiteSpace(title)) title = $"Phần {order}";

            var contentMatch = Regex.Match(block, @"-\s*\*\*(?:Lời hiển thị \(Caption\)|Lời hiển thị|Lời thoại)\*\*[:\s]*(.+)", RegexOptions.IgnoreCase);
            var rawCaption = contentMatch.Success ? contentMatch.Groups[1].Value.Trim() : title;
            var cleanCaption = CleanMarkdownArtifacts(rawCaption, preserveKw: false);

            var kwMatch = Regex.Match(rawCaption, @"\[kw\](.+?)\[/kw\]", RegexOptions.IgnoreCase);
            var visualKw = kwMatch.Success ? CleanMarkdownArtifacts(kwMatch.Groups[1].Value, preserveKw: false) : null;

            var ttsMatch = Regex.Match(block, @"-\s*\*\*Lời đọc TTS(?: \(ttsText\))?\*\*[:\s]*(.+)", RegexOptions.IgnoreCase);
            var rawTts = ttsMatch.Success ? ttsMatch.Groups[1].Value.Trim() : cleanCaption;
            var cleanTtsRaw = CleanMarkdownArtifacts(rawTts, preserveKw: false);
            var ttsText = CleanMarkdownArtifacts(
                VideoDurationEstimator.ApplyPhonetics(cleanTtsRaw, _phonetics),
                preserveKw: false);

            var badgeMatch = Regex.Match(block, @"-\s*\*\*Huy hiệu chuyên mục\*\*[:\s]*(.+)", RegexOptions.IgnoreCase);
            var rawBadge = badgeMatch.Success ? badgeMatch.Groups[1].Value.Trim() : badgeFromHeader;
            var sectionBadge = CleanMarkdownArtifacts(Regex.Replace(rawBadge, @"\bCảnh\s*\d+\b", $"Phần {order}", RegexOptions.IgnoreCase), false);

            var roleMatch = Regex.Match(block, @"-\s*\*\*Vai trò nhịp\*\*[:\s]*(.+)", RegexOptions.IgnoreCase);
            var beatRole = roleMatch.Success ? roleMatch.Groups[1].Value.Trim().ToLowerInvariant() : "step";

            var promptMatch = Regex.Match(block, @"-\s*\*\*Prompt(?:\s+hình)?\s+ảnh\*\*[:\s]*(.+)", RegexOptions.IgnoreCase);
            var prompt = promptMatch.Success ? promptMatch.Groups[1].Value.Trim() : "cinematic educational presentation visual, 8k";

            var effectMatch = Regex.Match(block, @"-\s*\*\*Hiệu ứng\*\*[:\s]*(.+)", RegexOptions.IgnoreCase);
            var effect = effectMatch.Success ? effectMatch.Groups[1].Value.Trim().ToLowerInvariant() : "ken-burns";

            var durationMatch = Regex.Match(block, @"-\s*\*\*Thời lượng\*\*[:\s]*([0-9.]+)", RegexOptions.IgnoreCase);
            double duration = 4.5;
            if (durationMatch.Success && double.TryParse(durationMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedDuration))
            {
                duration = Math.Clamp(parsedDuration, 2.0, 45.0);
            }

            var posMatch = Regex.Match(block, @"-\s*\*\*Vị trí nhân vật\*\*[:\s]*(.+)", RegexOptions.IgnoreCase);
            var pos = posMatch.Success ? posMatch.Groups[1].Value.Trim().ToLowerInvariant() : "center";

            var sceneTypeMatch = Regex.Match(block, @"-\s*\*\*(?:Loại bố cục|Loại cảnh)\*\*[:\s]*(.+)", RegexOptions.IgnoreCase);
            var sceneType = sceneTypeMatch.Success ? sceneTypeMatch.Groups[1].Value.Trim().ToLowerInvariant() : "compare-3zone";

            var sfxMatch = Regex.Match(block, @"-\s*\*\*Âm thanh SFX\*\*[:\s]*(.+)", RegexOptions.IgnoreCase);
            var sfxCue = sfxMatch.Success ? sfxMatch.Groups[1].Value.Trim().ToLowerInvariant() : "whoosh";

            scenes.Add(new Scene
            {
                ProjectId = projectId,
                OrderIndex = order++,
                Title = title.Length > 100 ? title[..100] : title,
                Content = cleanCaption,
                TtsText = ttsText,
                VisualKeyword = visualKw,
                BeatRole = beatRole.Length > 40 ? beatRole[..40] : beatRole,
                SectionBadge = sectionBadge.Length > 80 ? sectionBadge[..80] : sectionBadge,
                SfxCue = sfxCue.Length > 40 ? sfxCue[..40] : sfxCue,
                ImagePrompt = prompt,
                AnimationEffect = effect.Length > 50 ? effect[..50] : effect,
                DurationSeconds = duration,
                AvatarPosition = pos.Length > 50 ? pos[..50] : pos,
                SceneType = sceneType.Length > 50 ? sceneType[..50] : sceneType,
                FilterPreset = "none",
                SubtitlePosition = "bottom"
            });
        }

        return Task.FromResult(scenes);
    }

    public Task<string> BuildHyperFramesCompositionAsync(
        Project project,
        List<Scene> scenes,
        CancellationToken ct = default)
    {
        var isComparison = string.Equals(project.VideoType, "comparison", StringComparison.OrdinalIgnoreCase);
        var skillKey = !string.IsNullOrWhiteSpace(project.SkillKey)
            ? project.SkillKey
            : ResolveSkillKey(project.VideoType, project.SequentialSubMode, project.AspectRatio, (project.SourceMarkdown ?? project.SourceContent ?? "").Length / 5);

        var avatars = string.IsNullOrWhiteSpace(project.AvatarsJson)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(project.AvatarsJson) ?? new List<string>();
        var defaultAvatar = SanitizeMediaUrl(avatars.Count > 0 ? avatars[0] : "/images/logo.png", "/images/logo.png");
        var showAvatar = avatars.Count > 0 || skillKey != "sequential-kinetic-16x9";

        // Tính timeline tất định (start[1] = 0.55s, gap cùng beat 0.32s, chuyển beat 0.42s, outro hold 1.3s)
        double cursor = 0.55;
        var timedBeats = new List<(Scene Scene, double Start, double Dur, double End, string HtmlCaption)>();
        string? prevRole = null;

        for (int i = 0; i < scenes.Count; i++)
        {
            var sc = scenes[i];
            var dur = Math.Max(2.2, sc.DurationSeconds);
            if (i > 0)
            {
                bool sameBeat = !string.IsNullOrWhiteSpace(prevRole) && string.Equals(prevRole, sc.BeatRole, StringComparison.OrdinalIgnoreCase);
                cursor += sameBeat ? 0.32 : 0.42;
            }
            var start = Math.Round(cursor, 3);
            var end = Math.Round(start + dur + 0.25, 3);
            cursor = start + dur;
            prevRole = sc.BeatRole;

            var htmlCap = FormatCaptionWithKeywordSpan(sc.Content, sc.VisualKeyword, isComparison && i < 2);
            timedBeats.Add((sc, start, Math.Round(dur, 3), end, htmlCap));
        }

        var rootDuration = Math.Round(Math.Max(6.0, cursor + 1.3), 2);
        var rootDurStr = rootDuration.ToString("0.##", CultureInfo.InvariantCulture);

        var (conceptA, conceptB) = ExtractComparisonPairNames(project.Title, project.SourceMarkdown ?? project.SourceContent ?? "");
        var firstScene = scenes.FirstOrDefault();
        var secondScene = scenes.Skip(1).FirstOrDefault() ?? firstScene;
        var firstImg = System.Net.WebUtility.HtmlEncode(SanitizeMediaUrl(firstScene?.ImageUrl, "/images/cyber-cow.png"));
        var secondImg = System.Net.WebUtility.HtmlEncode(SanitizeMediaUrl(secondScene?.ImageUrl, "/images/cyber-cow.png"));
        var encodedAvatar = System.Net.WebUtility.HtmlEncode(defaultAvatar);
        var channelEyebrow = System.Net.WebUtility.HtmlEncode(_config["AutoVideo:ChannelName"] ?? "COW.AI STUDIO");

        var captionsDom = new StringBuilder();
        var audioDom = new StringBuilder();
        var beatsJsonList = new List<object>();

        for (int i = 0; i < timedBeats.Count; i++)
        {
            var b = timedBeats[i];
            var lineId = $"line-{i + 1}";
            var stStr = b.Start.ToString("0.###", CultureInfo.InvariantCulture);
            var durStr = b.Dur.ToString("0.###", CultureInfo.InvariantCulture);
            var badge = string.IsNullOrWhiteSpace(b.Scene.SectionBadge) ? $"PHẦN {i + 1}" : b.Scene.SectionBadge!;

            captionsDom.AppendLine($@"                <div class=""caption-line clip"" id=""{lineId}"" data-start=""{stStr}"" data-duration=""{durStr}"" data-track-index=""10"">
                    <span class=""caption-badge"">{System.Net.WebUtility.HtmlEncode(badge)}</span>
                    <span class=""caption-line-text"">{b.HtmlCaption}</span>
                </div>");

            if (!string.IsNullOrWhiteSpace(b.Scene.AudioUrl))
            {
                var safeAudio = System.Net.WebUtility.HtmlEncode(SanitizeMediaUrl(b.Scene.AudioUrl, ""));
                audioDom.AppendLine($@"        <audio id=""vo-{i + 1}"" class=""clip"" src=""{safeAudio}"" data-start=""{stStr}"" data-duration=""{durStr}"" data-track-index=""20"" preload=""auto""></audio>");
            }

            beatsJsonList.Add(new
            {
                index = i + 1,
                lineId = $"#{lineId}",
                title = b.Scene.Title,
                badge,
                role = b.Scene.BeatRole ?? "step",
                imageUrl = SanitizeMediaUrl(b.Scene.ImageUrl, "/images/cyber-cow.png"),
                audioUrl = SanitizeMediaUrl(b.Scene.AudioUrl, ""),
                start = b.Start,
                dur = b.Dur,
                capOut = i == timedBeats.Count - 1 ? (double?)null : b.End,
                pose = b.Scene.AvatarPosition ?? "center",
                effect = b.Scene.AnimationEffect ?? "spring-pop"
            });
        }

        var beatsJson = JsonSerializer.Serialize(beatsJsonList).Replace("<", "\\u003c").Replace(">", "\\u003e");
        var isWidescreen = string.Equals(project.AspectRatio, "16:9", StringComparison.OrdinalIgnoreCase) || skillKey == "sequential-kinetic-16x9";

        var html = $@"<!DOCTYPE html>
<html lang=""vi"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{System.Net.WebUtility.HtmlEncode(project.Title)} — HyperFrames ({skillKey})</title>
    <link rel=""stylesheet"" href=""/assets/studio/fonts/local_fonts.css"" />
    <script src=""/lib/gsap/gsap.min.js""></script>
    <script>window.gsap || document.write('<script src=""https://cdn.jsdelivr.net/npm/gsap@3.14.2/dist/gsap.min.js""><\/script>')</script>
    <style>
        /* Nhúng trực tiếp @font-face Be Vietnam Pro chống rụng dấu tiếng Việt */
        @font-face {{
            font-family: 'Be Vietnam Pro';
            font-style: normal;
            font-weight: 700;
            src: url('/assets/studio/fonts/be-vietnam-pro-normal-700-vietnamese.woff2') format('woff2');
            unicode-range: U+0102-0103, U+0110-0111, U+0128-0129, U+0168-0169, U+01A0-01A1, U+01AF-01B0, U+0300-0301, U+0303-0304, U+0308-0309, U+0323, U+0329, U+1EA0-1EF9, U+20AB;
        }}
        @font-face {{
            font-family: 'Be Vietnam Pro';
            font-style: normal;
            font-weight: 700;
            src: url('/assets/studio/fonts/be-vietnam-pro-normal-700-latin.woff2') format('woff2');
            unicode-range: U+0000-00FF, U+0131, U+0152-0153, U+02BB-02BC, U+02C6, U+02DA, U+02DC, U+2000-206F, U+20AC, U+2122, U+2191, U+2193, U+2212, U+2215, U+FEFF, U+FFFD;
        }}
        :root {{
            --bg: #0D0A1A;
            --panel: #17122C;
            --panel-edge: #2B2350;
            --fg: #F3F1FF;
            --fg-dim: #9089B0;
            --accent-pink: #FF4FA3;
            --accent-cyan: #37E6C4;
            --accent-gold: #FFD700;
        }}
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        html, body {{
            background: var(--bg);
            color: var(--fg);
            font-family: 'Be Vietnam Pro', -apple-system, BlinkMacSystemFont, sans-serif;
            display: flex;
            align-items: center;
            justify-content: center;
            min-height: 100vh;
            overflow: hidden;
        }}
        #root {{
            width: 100vw;
            max-width: 1080px;
            aspect-ratio: {(isWidescreen ? "16/9" : "9/16")};
            position: relative;
            overflow: hidden;
            background: radial-gradient(circle at 50% 25%, rgba(55, 230, 196, 0.14), rgba(13, 10, 26, 0.98) 72%), var(--bg);
            border-radius: 12px;
            border: 1px solid var(--panel-edge);
        }}
        .clip {{ position: relative; }}
        #header-bar {{
            position: absolute;
            top: 16px;
            left: 24px;
            right: 24px;
            display: flex;
            align-items: center;
            justify-content: space-between;
            z-index: 15;
        }}
        #eyebrow {{
            font-family: 'JetBrains Mono', monospace;
            font-size: 0.82rem;
            font-weight: 700;
            letter-spacing: 0.12em;
            text-transform: uppercase;
            color: var(--accent-cyan);
            background: rgba(23, 18, 44, 0.9);
            border: 1px solid var(--panel-edge);
            padding: 6px 14px;
            border-radius: 8px;
        }}
        #section-pill {{
            font-size: 0.82rem;
            font-weight: 700;
            color: var(--accent-gold);
            background: rgba(255, 215, 0, 0.12);
            border: 1px solid rgba(255, 215, 0, 0.4);
            padding: 6px 14px;
            border-radius: 8px;
            line-height: 1.4;
        }}
        #progress-track {{
            position: absolute;
            top: 0;
            left: 0;
            right: 0;
            height: 4px;
            background: rgba(255, 255, 255, 0.08);
            z-index: 20;
        }}
        #progress-fill {{
            width: 0%;
            height: 100%;
            background: var(--accent-cyan);
        }}
        .stage-layer {{
            position: absolute;
            inset: 56px 0 44px 0;
            display: flex;
            flex-direction: column;
            padding: 12px 24px;
            gap: 12px;
        }}
        /* Zone 1: Cards / B-Roll */
        .zone-cards {{
            flex: 5;
            position: relative;
            display: grid;
            grid-template-columns: {(isComparison ? "1fr 1fr" : "1fr")};
            gap: 18px;
            align-items: stretch;
            min-height: 0;
        }}
        .compare-card {{
            background: var(--panel);
            border: 1px solid var(--panel-edge);
            border-radius: 12px;
            overflow: hidden;
            display: flex;
            flex-direction: column;
            position: relative;
        }}
        .compare-card img {{
            width: 100%;
            height: 74%;
            object-fit: cover;
        }}
        /* Quy tắc Anti-Clipping dấu tiếng Việt: line-height >= 1.35, padding-top >= 16px, màu trực tiếp + text-shadow */
        .compare-card__label {{
            padding: 16px 12px 10px;
            margin-top: -6px;
            font-size: 1.15rem;
            font-weight: 700;
            line-height: 1.4;
            text-align: center;
            color: var(--accent-cyan);
            text-shadow: 0 2px 10px rgba(0, 0, 0, 0.8);
        }}
        #vs-badge {{
            position: absolute;
            left: 50%;
            top: 50%;
            transform: translate(-50%, -50%);
            width: 54px;
            height: 54px;
            border-radius: 50%;
            background: var(--bg);
            border: 2px solid var(--accent-pink);
            color: var(--fg);
            font-weight: 700;
            display: flex;
            align-items: center;
            justify-content: center;
            z-index: 6;
            box-shadow: 0 6px 20px rgba(0, 0, 0, 0.75);
        }}
        /* Zone 2: Kinetic Captions */
        .zone-captions {{
            flex: 2.3;
            position: relative;
            display: flex;
            align-items: center;
            justify-content: center;
        }}
        .caption-line {{
            position: absolute;
            inset: 0;
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            text-align: center;
            opacity: 0;
            padding: 8px 16px;
        }}
        .caption-badge {{
            font-family: 'JetBrains Mono', monospace;
            font-size: 0.72rem;
            font-weight: 700;
            letter-spacing: 0.08em;
            color: var(--accent-cyan);
            margin-bottom: 6px;
            text-transform: uppercase;
        }}
        .caption-line-text {{
            font-size: 1.25rem;
            font-weight: 700;
            line-height: 1.42;
            padding-top: 16px;
            margin-top: -10px;
            color: var(--fg);
            text-shadow: 0 2px 12px rgba(0, 0, 0, 0.88);
            background: rgba(23, 18, 44, 0.86);
            padding: 14px 20px 12px;
            border-radius: 10px;
            border: 1px solid var(--panel-edge);
            max-width: 92%;
        }}
        .kw {{
            color: var(--accent-pink);
            display: inline-block;
            margin: 0 4px;
            text-shadow: 0 0 18px rgba(255, 79, 163, 0.55);
        }}
        .kw--cyan {{
            color: var(--accent-cyan);
            text-shadow: 0 0 18px rgba(55, 230, 196, 0.55);
        }}
        /* Zone 3: Avatar / Presenter (hoàn toàn tách ngoài khung chữ) */
        .zone-avatar {{
            flex: 1.8;
            display: flex;
            align-items: center;
            justify-content: center;
        }}
        .avatar-frame {{
            width: 88px;
            height: 88px;
            border-radius: 50%;
            border: 2px solid var(--accent-cyan);
            overflow: hidden;
            background: var(--panel);
            box-shadow: 0 8px 24px rgba(0, 0, 0, 0.65);
        }}
        .avatar-frame img {{ width: 100%; height: 100%; object-fit: cover; }}
        .stage-controls {{
            position: absolute;
            bottom: 8px;
            left: 24px;
            right: 24px;
            display: flex;
            align-items: center;
            justify-content: space-between;
            font-size: 0.78rem;
            color: var(--fg-dim);
            z-index: 15;
        }}
    </style>
</head>
<body>
    <div id=""root"" class=""clip"" data-start=""0"" data-duration=""{rootDurStr}"" data-track-index=""0"">
        <div id=""progress-track""><div id=""progress-fill""></div></div>
        <div id=""header-bar"">
            <div id=""eyebrow"">{channelEyebrow}</div>
            <div id=""section-pill"">{System.Net.WebUtility.HtmlEncode(firstScene?.SectionBadge ?? project.Title)}</div>
        </div>

        <div id=""scene"" class=""stage-layer clip"" data-start=""0"" data-duration=""{rootDurStr}"" data-track-index=""1"">
            <div class=""zone-cards"">
                <div class=""compare-card"" id=""card-left"">
                    <img id=""stageImageA"" src=""{firstImg}"" alt=""Visual A"" />
                    <div class=""compare-card__label"" id=""stageTitleA"">{System.Net.WebUtility.HtmlEncode(isComparison ? conceptA : (firstScene?.Title ?? project.Title))}</div>
                </div>
                {(isComparison ? $@"
                <div id=""vs-badge"">VS</div>
                <div class=""compare-card"" id=""card-right"">
                    <img id=""stageImageB"" src=""{secondImg}"" alt=""Visual B"" />
                    <div class=""compare-card__label"" id=""stageTitleB"">{System.Net.WebUtility.HtmlEncode(conceptB)}</div>
                </div>" : "")}
            </div>

            <div class=""zone-captions"">
{captionsDom}
            </div>

            {(showAvatar ? $@"
            <div class=""zone-avatar"">
                <div class=""avatar-frame"" id=""avatar-host"">
                    <img id=""stageAvatar"" src=""{encodedAvatar}"" alt=""Host Avatar"" />
                </div>
            </div>" : "")}
        </div>

        <div class=""stage-controls"">
            <span id=""timeIndicator"">00:00 / {rootDurStr}s</span>
            <span id=""sceneIndicator"">Phần 1/{Math.Max(1, scenes.Count)}</span>
        </div>

{audioDom}
    </div>

    <script>
        window.__timelines = window.__timelines || {{}};
        const ROOT_DURATION = {rootDurStr};
        const BEATS = {beatsJson};
        const isComparison = {(isComparison ? "true" : "false")};

        const tl = gsap.timeline({{ paused: true }});
        tl.fromTo('#progress-fill', {{ width: '0%' }}, {{ width: '100%', duration: ROOT_DURATION, ease: 'none' }}, 0);

        function showLine(lineSelector, start, capOut) {{
            tl.fromTo(lineSelector,
                {{ opacity: 0, y: 20 }},
                {{ opacity: 1, y: 0, duration: 0.32, ease: 'power3.out' }},
                start);
            const kws = document.querySelectorAll(lineSelector + ' .kw');
            if (kws.length > 0) {{
                tl.fromTo(kws,
                    {{ scale: 1 }},
                    {{ scale: 1.14, duration: 0.14, yoyo: true, repeat: 1, ease: 'power2.out' }},
                    start + 0.12);
            }}
            if (capOut !== null && capOut !== undefined) {{
                tl.to(lineSelector, {{ opacity: 0, y: -12, duration: 0.2, ease: 'power2.in' }}, capOut);
            }}
        }}

        BEATS.forEach((b, idx) => {{
            showLine(b.lineId, b.start, b.capOut);
            tl.call(() => {{
                const pill = document.getElementById('section-pill');
                if (pill) pill.textContent = b.badge || b.title;
                const ind = document.getElementById('sceneIndicator');
                if (ind) ind.textContent = `Phần ${{idx + 1}}/${{BEATS.length}}`;
                const imgA = document.getElementById('stageImageA');
                const titleA = document.getElementById('stageTitleA');
                if (!isComparison) {{
                    if (imgA && b.imageUrl) imgA.src = b.imageUrl;
                    if (titleA && b.title) titleA.textContent = b.title;
                }}
            }}, null, b.start);

            if (isComparison) {{
                if (b.role === 'reveal-a') {{
                    tl.to('#card-left',  {{ scale: 1.03, opacity: 1,    duration: 0.35, ease: 'power2.out' }}, b.start);
                    tl.to('#card-right', {{ scale: 0.96, opacity: 0.55, duration: 0.35, ease: 'power2.out' }}, b.start);
                }} else if (b.role === 'reveal-b') {{
                    tl.to('#card-left',  {{ scale: 0.96, opacity: 0.55, duration: 0.35, ease: 'power2.out' }}, b.start);
                    tl.to('#card-right', {{ scale: 1.03, opacity: 1,    duration: 0.35, ease: 'power2.out' }}, b.start);
                }} else {{
                    tl.to('#card-left, #card-right', {{ scale: 1, opacity: 1, duration: 0.35, ease: 'power2.out' }}, b.start);
                }}
            }}
        }});

        window.__timelines['main'] = tl;

        // Cho phép bấm trực tiếp trong iframe preview để chạy thử timeline
        document.getElementById('root').addEventListener('click', () => {{
            if (tl.isActive()) {{
                tl.pause();
            }} else {{
                if (tl.progress() >= 0.99) tl.restart();
                else tl.play();
            }}
        }});
    </script>
</body>
</html>";

        return Task.FromResult(html);
    }

    private static string FormatCaptionWithKeywordSpan(string content, string? visualKeyword, bool isHookConcept)
    {
        var encoded = System.Net.WebUtility.HtmlEncode(content ?? "");
        var kwClass = isHookConcept ? "kw kw--cyan" : "kw";

        if (!string.IsNullOrWhiteSpace(visualKeyword))
        {
            var encodedKw = System.Net.WebUtility.HtmlEncode(visualKeyword.Trim());
            if (!string.IsNullOrEmpty(encodedKw) && encoded.Contains(encodedKw, StringComparison.OrdinalIgnoreCase))
            {
                return Regex.Replace(
                    encoded,
                    Regex.Escape(encodedKw),
                    m => $@"<span class=""{kwClass}"">{m.Value}</span>",
                    RegexOptions.IgnoreCase);
            }
        }

        var parts = encoded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3)
        {
            int idx = Math.Min(parts.Length - 1, 2);
            parts[idx] = $@"<span class=""{kwClass}"">{parts[idx]}</span>";
            return string.Join(" ", parts);
        }
        return encoded;
    }

    private static string SanitizeMediaUrl(string? url, string fallback)
    {
        if (string.IsNullOrWhiteSpace(url)) return fallback;
        var trimmed = url.Trim();
        if (trimmed.StartsWith("/", StringComparison.Ordinal) && !trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return trimmed;
        }
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            return trimmed;
        }
        return fallback;
    }

    public async Task<(bool Success, string Message, Project? UpdatedProject)> ProcessChatEditAsync(
        int projectId,
        string userInstruction,
        CancellationToken ct = default)
    {
        var project = await _db.Projects.Include(p => p.Scenes.OrderBy(s => s.OrderIndex)).FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null)
        {
            return (false, "Không tìm thấy dự án tương ứng.", null);
        }

        if (string.IsNullOrWhiteSpace(userInstruction))
        {
            return (false, "Vui lòng nhập lệnh chỉnh sửa.", project);
        }

        var text = userInstruction.Trim();
        bool modified = false;

        if (text.Contains("đen trắng", StringComparison.OrdinalIgnoreCase) || text.Contains("noir", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var sc in project.Scenes) sc.FilterPreset = "noir";
            modified = true;
        }
        else if (text.Contains("điện ảnh", StringComparison.OrdinalIgnoreCase) || text.Contains("cinematic", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var sc in project.Scenes) sc.FilterPreset = "cinematic";
            modified = true;
        }

        var durationMatch = Regex.Match(text, @"(?:phần|cảnh)\s*(\d+).*?(\d+(\.\d+)?)\s*(giây|s)", RegexOptions.IgnoreCase);
        if (durationMatch.Success)
        {
            var sceneNum = int.Parse(durationMatch.Groups[1].Value);
            var dur = double.Parse(durationMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            var targetScene = project.Scenes.FirstOrDefault(s => s.OrderIndex == sceneNum);
            if (targetScene != null)
            {
                targetScene.DurationSeconds = Math.Clamp(dur, 1.0, 45.0);
                modified = true;
            }
        }

        var contentMatch = Regex.Match(text, @"sửa lời thoại (?:phần|cảnh)\s*(\d+)[:\s]+(.+)", RegexOptions.IgnoreCase);
        if (contentMatch.Success)
        {
            var sceneNum = int.Parse(contentMatch.Groups[1].Value);
            var newContent = contentMatch.Groups[2].Value.Trim();
            var targetScene = project.Scenes.FirstOrDefault(s => s.OrderIndex == sceneNum);
            if (targetScene != null)
            {
                targetScene.Content = newContent;
                targetScene.TtsText = VideoDurationEstimator.ApplyPhonetics(newContent, _phonetics);
                targetScene.AudioUrl = null;
                modified = true;
            }
        }

        if (text.Contains("Hải Đăng", StringComparison.OrdinalIgnoreCase))
        {
            project.Voice = "Hải Đăng";
            modified = true;
        }
        else if (text.Contains("Lan Nhi", StringComparison.OrdinalIgnoreCase))
        {
            project.Voice = "Lan Nhi";
            modified = true;
        }

        if (modified)
        {
            project.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return (true, $"Đã cập nhật dự án #{projectId} theo lệnh: \"{userInstruction}\"", project);
        }

        project.Description = (project.Description ?? "") + $"\n[Ghi chú biên tập]: {userInstruction}";
        project.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return (true, $"Đã ghi nhận yêu cầu biên tập: \"{userInstruction}\"", project);
    }
}
