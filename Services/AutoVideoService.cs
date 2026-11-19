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
    private readonly IConfiguration _config;
    private readonly ILogger<AutoVideoService> _logger;

    public AutoVideoService(
        AppDbContext db,
        OllamaService ollama,
        OpenRouterService openRouter,
        IVieNeuTtsService vieNeuTts,
        PollinationsService pollinations,
        IConfiguration config,
        ILogger<AutoVideoService> logger)
    {
        _db = db;
        _ollama = ollama;
        _openRouter = openRouter;
        _vieNeuTts = vieNeuTts;
        _pollinations = pollinations;
        _config = config;
        _logger = logger;
    }

    public async Task<string> GenerateMarkdownPlanAsync(
        string sourceContent, 
        string videoType, 
        string voice, 
        List<string> avatarUrls, 
        CancellationToken ct = default)
    {
        var cleanContent = string.IsNullOrWhiteSpace(sourceContent)
            ? "So sánh Trí tuệ Nhân tạo tổng quát (AGI) và Trí tuệ Nhân tạo hẹp (Narrow AI)"
            : sourceContent.Trim();

        var isComparison = videoType.Equals("comparison", StringComparison.OrdinalIgnoreCase);
        var avatarCount = avatarUrls.Count;
        var avatarMode = avatarCount > 0 
            ? $"Có {avatarCount} ảnh avatar nhân vật đại diện" 
            : "Chế độ Cinematic B-Roll (Không có ảnh đại diện, tự động sinh cảnh phong cảnh minh họa)";

        var prompt = $@"Bạn là Tổng biên kịch và Đạo diễn video tự động của COW.AI Studio kết hợp HyperFrames.
Hãy chuyển nội dung sau thành BẢN KẾ HOẠCH PHÂN CẢNH VIDEO chuẩn Markdown (.md):

NỘI DUNG NGUỒN:
{cleanContent}

CẤU HÌNH SẢN XUẤT:
- Thể loại video: {(isComparison ? "SO SÁNH ĐỐI CHIẾU 2 CỘT (Template HyperFrames auto-compare-video)" : "THUYẾT MINH TUẦN TỰ (Cinematic Narrative)")}
- Giọng đọc: {voice}
- Chế độ Avatar: {avatarMode}

YÊU CẦU ĐỊNH DẠNG BẢN KẾ HOẠCH MARKDOWN (.MD):
1. Tiêu đề chính: # KẾ HOẠCH PHÂN CẢNH: [Tên dự án]
2. Khối tóm tắt:
   - **Thể loại**: {videoType}
   - **Giọng đọc**: {voice}
   - **Avatar**: {avatarMode}
3. Chia thành từ 3 đến 6 phân cảnh (Scene). Với mỗi phân cảnh, viết theo cấu trúc sau:

### Cảnh [X]: [Tên phân cảnh]
- **Loại cảnh**: {(isComparison ? "comparison" : "sequential")}
- **Lời thoại**: [Câu thoại thuyết minh súc tích, tự nhiên bằng tiếng Việt]
- **Prompt hình ảnh**: [Mô tả chi tiết bằng tiếng Anh để sinh ảnh AI, ví dụ: 3D render of cyber robot on futuristic background, 8k]
- **Hiệu ứng**: [ken-burns hoặc zoom-in hoặc pan-left]
- **Thời lượng**: [4.0]s
- **Vị trí nhân vật**: [left, right, center hoặc none]

Chỉ xuất định dạng Markdown hợp lệ, không chào hỏi thừa.";

        string markdownResult = string.Empty;
        var studioModel = _config["Ollama:StudioModel"] ?? "qwen2.5:7b";
        foreach (var candidateModel in new[] { studioModel, "cow-ai:latest" }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                markdownResult = await _ollama.ChatAsync(prompt, candidateModel, ct);
                if (!string.IsNullOrWhiteSpace(markdownResult) && markdownResult.Length >= 50) break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ollama model {Model} không phản hồi, thử model dự phòng...", candidateModel);
            }
        }

        if (string.IsNullOrWhiteSpace(markdownResult) || markdownResult.Length < 50)
        {
            try
            {
                markdownResult = await _openRouter.ChatAsync(prompt, "google/gemini-2.0-flash-exp:free", ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenRouter không phản hồi. Dùng template mẫu mặc định.");
            }
        }

        if (string.IsNullOrWhiteSpace(markdownResult) || !markdownResult.Contains("### Cảnh"))
        {
            markdownResult = BuildFallbackMarkdownPlan(cleanContent, videoType, voice, avatarUrls);
        }

        return markdownResult;
    }

    private string BuildFallbackMarkdownPlan(string topic, string videoType, string voice, List<string> avatarUrls)
    {
        var isComparison = videoType.Equals("comparison", StringComparison.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.AppendLine($"# KẾ HOẠCH PHÂN CẢNH: {topic}");
        sb.AppendLine();
        sb.AppendLine($"- **Thể loại**: {(isComparison ? "comparison (So sánh đối chiếu 2 cột)" : "sequential (Thuyết minh tuần tự)")}");
        sb.AppendLine($"- **Giọng đọc**: {voice}");
        sb.AppendLine($"- **Avatar**: {(avatarUrls.Count > 0 ? $"{avatarUrls.Count} ảnh nhân vật" : "Cinematic B-Roll không ảnh")}");
        sb.AppendLine();

        sb.AppendLine("### Cảnh 1: Mở đầu chủ đề");
        sb.AppendLine($"- **Loại cảnh**: {(isComparison ? "comparison" : "sequential")}");
        sb.AppendLine($"- **Lời thoại**: Chào bạn! Hôm nay chúng ta cùng khám phá và phân tích sâu về {topic}.");
        sb.AppendLine("- **Prompt hình ảnh**: futuristic digital technology studio presentation, highly detailed, neon lights, 8k");
        sb.AppendLine("- **Hiệu ứng**: ken-burns");
        sb.AppendLine("- **Thời lượng**: 4.0s");
        sb.AppendLine("- **Vị trí nhân vật**: center");
        sb.AppendLine();

        sb.AppendLine("### Cảnh 2: Khía cạnh cốt lõi");
        sb.AppendLine($"- **Loại cảnh**: {(isComparison ? "comparison" : "sequential")}");
        sb.AppendLine("- **Lời thoại**: Hãy quan sát điểm khác biệt cơ bản về nguyên lý hoạt động và ứng dụng thực tiễn của từng thành phần.");
        sb.AppendLine("- **Prompt hình ảnh**: comparison diagram infographics, cybernetic futuristic interface, clean contrast, 8k");
        sb.AppendLine("- **Hiệu ứng**: zoom-in");
        sb.AppendLine("- **Thời lượng**: 4.5s");
        sb.AppendLine("- **Vị trí nhân vật**: left");
        sb.AppendLine();

        sb.AppendLine("### Cảnh 3: Kết luận & Đúc kết");
        sb.AppendLine($"- **Loại cảnh**: {(isComparison ? "comparison" : "sequential")}");
        sb.AppendLine("- **Lời thoại**: Nắm vững sự khác biệt này sẽ giúp bạn tối ưu hóa hiệu quả công việc và đưa ra quyết định chuẩn xác.");
        sb.AppendLine("- **Prompt hình ảnh**: glowing AI core chip breakthrough triumph success, cinematic depth, 8k");
        sb.AppendLine("- **Hiệu ứng**: pan-left");
        sb.AppendLine("- **Thời lượng**: 4.0s");
        sb.AppendLine("- **Vị trí nhân vật**: right");

        return sb.ToString();
    }

    public Task<List<Scene>> ParsePlanToScenesAsync(
        int projectId, 
        string markdownPlan, 
        CancellationToken ct = default)
    {
        var scenes = new List<Scene>();
        if (string.IsNullOrWhiteSpace(markdownPlan)) return Task.FromResult(scenes);

        var sceneBlocks = Regex.Split(markdownPlan, @"(?=###\s+Cảnh\s+\d+)", RegexOptions.IgnoreCase)
                               .Where(b => b.Trim().StartsWith("### Cảnh", StringComparison.OrdinalIgnoreCase))
                               .ToList();

        int order = 1;
        foreach (var block in sceneBlocks)
        {
            var titleMatch = Regex.Match(block, @"###\s+Cảnh\s+\d+[:\s\-]*(.+)", RegexOptions.IgnoreCase);
            var title = titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : $"Cảnh {order}";

            var contentMatch = Regex.Match(block, @"-\s*\*\*Lời thoại\*\*[:\s]*(.+)", RegexOptions.IgnoreCase);
            var content = contentMatch.Success ? contentMatch.Groups[1].Value.Trim() : "Nội dung thuyết minh phân cảnh.";

            var promptMatch = Regex.Match(block, @"-\s*\*\*Prompt hình ảnh\*\*[:\s]*(.+)", RegexOptions.IgnoreCase);
            var prompt = promptMatch.Success ? promptMatch.Groups[1].Value.Trim() : "cinematic visual wallpaper, 8k";

            var effectMatch = Regex.Match(block, @"-\s*\*\*Hiệu ứng\*\*[:\s]*(.+)", RegexOptions.IgnoreCase);
            var effect = effectMatch.Success ? effectMatch.Groups[1].Value.Trim().ToLowerInvariant() : "ken-burns";

            var durationMatch = Regex.Match(block, @"-\s*\*\*Thời lượng\*\*[:\s]*([0-9.]+)", RegexOptions.IgnoreCase);
            double duration = 4.0;
            if (durationMatch.Success && double.TryParse(durationMatch.Groups[1].Value, out var parsedDuration))
            {
                duration = Math.Clamp(parsedDuration, 2.0, 30.0);
            }

            var posMatch = Regex.Match(block, @"-\s*\*\*Vị trí nhân vật\*\*[:\s]*(.+)", RegexOptions.IgnoreCase);
            var pos = posMatch.Success ? posMatch.Groups[1].Value.Trim().ToLowerInvariant() : "center";

            var sceneTypeMatch = Regex.Match(block, @"-\s*\*\*Loại cảnh\*\*[:\s]*(.+)", RegexOptions.IgnoreCase);
            var sceneType = sceneTypeMatch.Success ? sceneTypeMatch.Groups[1].Value.Trim().ToLowerInvariant() : "standard";

            scenes.Add(new Scene
            {
                ProjectId = projectId,
                OrderIndex = order++,
                Title = title,
                Content = content,
                ImagePrompt = prompt,
                AnimationEffect = effect,
                DurationSeconds = duration,
                AvatarPosition = pos,
                SceneType = sceneType,
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
        var isComparison = project.VideoType.Equals("comparison", StringComparison.OrdinalIgnoreCase);
        var totalDuration = scenes.Sum(s => s.DurationSeconds);
        var avatars = string.IsNullOrWhiteSpace(project.AvatarsJson)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(project.AvatarsJson) ?? new List<string>();

        var defaultAvatar = SanitizeMediaUrl(avatars.Count > 0 ? avatars[0] : "/images/logo.png", "/images/logo.png");
        var hasAvatar = avatars.Count > 0;

        var scenesJson = JsonSerializer.Serialize(scenes.Select(s => new
        {
            id = s.Id,
            orderIndex = s.OrderIndex,
            title = s.Title,
            content = s.Content,
            imageUrl = SanitizeMediaUrl(s.ImageUrl, "/images/cyber-cow.png"),
            audioUrl = SanitizeMediaUrl(s.AudioUrl, ""),
            duration = s.DurationSeconds,
            animation = s.AnimationEffect,
            filter = s.FilterPreset,
            subtitlePos = s.SubtitlePosition,
            avatarPos = s.AvatarPosition ?? "center",
            sceneType = s.SceneType ?? (isComparison ? "comparison" : "sequential")
        })).Replace("<", "\\u003c").Replace(">", "\\u003e");

        var firstScene = scenes.FirstOrDefault();
        var secondScene = scenes.Skip(1).FirstOrDefault();
        var firstImg = System.Net.WebUtility.HtmlEncode(SanitizeMediaUrl(firstScene?.ImageUrl, "/images/cyber-cow.png"));
        var firstTitle = System.Net.WebUtility.HtmlEncode(firstScene?.Title ?? project.Title);
        var secondImg = System.Net.WebUtility.HtmlEncode(SanitizeMediaUrl(secondScene?.ImageUrl, "/images/cyber-cow.png"));
        var secondTitle = System.Net.WebUtility.HtmlEncode(secondScene?.Title ?? "So sánh");
        var firstCaption = System.Net.WebUtility.HtmlEncode(firstScene?.Content ?? project.Title);
        var encodedAvatar = System.Net.WebUtility.HtmlEncode(defaultAvatar);

        var html = $@"<!DOCTYPE html>
<html lang=""vi"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{System.Net.WebUtility.HtmlEncode(project.Title)} - HyperFrames Auto-Video</title>
    <link rel=""stylesheet"" href=""/assets/studio/fonts/local_fonts.css"" />
    <script src=""https://cdn.jsdelivr.net/npm/gsap@3.12.5/dist/gsap.min.js""></script>
    <style>
        :root {{
            --hf-bg: #0D0A1A;
            --hf-panel: #17122C;
            --hf-panel-edge: #2B2350;
            --hf-fg: #F3F1FF;
            --hf-fg-dim: #9089B0;
            --hf-accent-pink: #FF4FA3;
            --hf-accent-cyan: #37E6C4;
        }}
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{
            background: var(--hf-bg);
            color: var(--hf-fg);
            font-family: 'Be Vietnam Pro', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            display: flex;
            align-items: center;
            justify-content: center;
            min-height: 100vh;
            overflow: hidden;
        }}
        #hyperframes-stage {{
            width: 100vw;
            max-width: 1080px;
            aspect-ratio: {(isComparison ? "9/16" : "16/9")};
            height: auto;
            max-height: 95vh;
            position: relative;
            overflow: hidden;
            background: #000;
            border-radius: 12px;
            box-shadow: 0 24px 60px rgba(0,0,0,0.85);
            border: 1px solid var(--hf-panel-edge);
        }}
        .stage-layer {{
            position: absolute;
            inset: 0;
            display: flex;
            flex-direction: column;
        }}
        /* 3-Zone Layout (DESIGN.md của auto-compare-video) */
        .zone-cards {{
            flex: 5;
            display: grid;
            grid-template-columns: {(isComparison ? "1fr 1fr" : "1fr")};
            gap: 16px;
            padding: 24px;
            align-items: center;
            justify-content: center;
            z-index: 2;
        }}
        .compare-card {{
            background: rgba(23, 18, 44, 0.85);
            border: 1px solid var(--hf-panel-edge);
            border-radius: 12px;
            height: 100%;
            display: flex;
            flex-direction: column;
            overflow: hidden;
            box-shadow: 0 10px 25px rgba(0,0,0,0.4);
            transition: transform 0.3s ease;
        }}
        .compare-card img {{
            width: 100%;
            height: 70%;
            object-fit: cover;
        }}
        .compare-card__label {{
            padding: 12px;
            font-size: 1.1rem;
            font-weight: 700;
            text-align: center;
            color: var(--hf-accent-cyan);
        }}
        .zone-captions {{
            flex: 2;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 16px 24px;
            text-align: center;
            z-index: 4;
        }}
        .caption-text {{
            font-size: 1.35rem;
            font-weight: 700;
            line-height: 1.4;
            color: #ffffff;
            text-shadow: 0 2px 10px rgba(0,0,0,0.8);
            background: rgba(13, 10, 26, 0.75);
            padding: 10px 20px;
            border-radius: 8px;
            border: 1px solid rgba(255, 79, 163, 0.4);
        }}
        .zone-avatar {{
            flex: 3;
            display: flex;
            align-items: center;
            justify-content: center;
            z-index: 3;
            position: relative;
        }}
        .avatar-frame {{
            width: 120px;
            height: 120px;
            border-radius: 50%;
            border: 3px solid var(--hf-accent-pink);
            overflow: hidden;
            box-shadow: 0 0 24px rgba(255, 79, 163, 0.4);
            animation: pulseAvatar 2s infinite ease-in-out;
        }}
        .avatar-frame img {{
            width: 100%;
            height: 100%;
            object-fit: cover;
        }}
        @keyframes pulseAvatar {{
            0%, 100% {{ transform: scale(1); }}
            50% {{ transform: scale(1.05); }}
        }}
        .ken-burns {{ animation: kenBurns 12s infinite alternate ease-in-out; }}
        @keyframes kenBurns {{ 0% {{ transform: scale(1) translate(0, 0); }} 100% {{ transform: scale(1.1) translate(-2%, -2%); }} }}
        /* Audio Player Bar */
        .stage-controls {{
            position: absolute;
            bottom: 12px;
            left: 20px;
            right: 20px;
            display: flex;
            align-items: center;
            justify-content: space-between;
            z-index: 10;
            background: rgba(13, 10, 26, 0.85);
            padding: 8px 16px;
            border-radius: 8px;
            border: 1px solid var(--hf-panel-edge);
        }}
    </style>
</head>
<body>
    <div id=""hyperframes-stage"">
        <div class=""stage-layer"">
            <!-- Zone 1: Cards Visuals -->
            <div class=""zone-cards"">
                <div class=""compare-card"" id=""cardA"">
                    <img id=""stageImageA"" src=""{firstImg}"" class=""ken-burns"" alt=""Visual A"" />
                    <div class=""compare-card__label"" id=""stageTitleA"">{firstTitle}</div>
                </div>
                {(isComparison ? $@"
                <div class=""compare-card"" id=""cardB"">
                    <img id=""stageImageB"" src=""{secondImg}"" class=""ken-burns"" alt=""Visual B"" />
                    <div class=""compare-card__label"" id=""stageTitleB"">{secondTitle}</div>
                </div>" : "")}
            </div>

            <!-- Zone 2: Kinetic Captions -->
            <div class=""zone-captions"">
                <div class=""caption-text"" id=""stageCaption"">{firstCaption}</div>
            </div>

            <!-- Zone 3: Avatar / Host -->
            {(hasAvatar ? $@"
            <div class=""zone-avatar"">
                <div class=""avatar-frame"">
                    <img id=""stageAvatar"" src=""{encodedAvatar}"" alt=""MC Avatar"" />
                </div>
            </div>" : "")}
        </div>

        <!-- Controls -->
        <div class=""stage-controls"">
            <span id=""timeIndicator"">00:00 / {Math.Round(totalDuration, 1)}s</span>
            <span id=""sceneIndicator"">Cảnh 1/{scenes.Count}</span>
        </div>
    </div>

    <audio id=""stageAudio"" preload=""auto""></audio>

    <script>
        const scenes = {scenesJson};
        let currentIdx = 0;
        const audio = document.getElementById('stageAudio');
        const caption = document.getElementById('stageCaption');
        const imgA = document.getElementById('stageImageA');
        const titleA = document.getElementById('stageTitleA');
        const sceneInd = document.getElementById('sceneIndicator');

        function playScene(idx) {{
            if (idx >= scenes.length) idx = 0;
            currentIdx = idx;
            const sc = scenes[idx];
            caption.innerText = sc.content;
            if (titleA) titleA.innerText = sc.title;
            if (imgA && sc.imageUrl) imgA.src = sc.imageUrl;
            if (sceneInd) sceneInd.innerText = `Cảnh ${{idx + 1}}/${{scenes.length}}`;

            if (sc.audioUrl) {{
                audio.src = sc.audioUrl;
                audio.play().catch(() => {{}});
                audio.onended = () => playScene(idx + 1);
            }} else {{
                setTimeout(() => playScene(idx + 1), (sc.duration || 4) * 1000);
            }}
        }}

        // Bắt đầu phát khi click vào stage
        document.getElementById('hyperframes-stage').addEventListener('click', () => {{
            playScene(0);
        }});
    </script>
</body>
</html>";

        return Task.FromResult(html);
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

        // Phân tích lệnh chỉnh sửa
        // 1. Chỉnh sửa hiệu ứng màu (filter)
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

        // 2. Chỉnh sửa thời lượng (Ví dụ: "rút ngắn cảnh 1 xuống 3 giây", "tăng thời lượng...")
        var durationMatch = Regex.Match(text, @"cảnh\s*(\d+).*?(\d+(\.\d+)?)\s*(giây|s)", RegexOptions.IgnoreCase);
        if (durationMatch.Success)
        {
            var sceneNum = int.Parse(durationMatch.Groups[1].Value);
            var dur = double.Parse(durationMatch.Groups[2].Value);
            var targetScene = project.Scenes.FirstOrDefault(s => s.OrderIndex == sceneNum);
            if (targetScene != null)
            {
                targetScene.DurationSeconds = Math.Clamp(dur, 1.0, 30.0);
                modified = true;
            }
        }

        // 3. Chỉnh sửa lời thoại cảnh
        var contentMatch = Regex.Match(text, @"sửa lời thoại cảnh\s*(\d+)[:\s]+(.+)", RegexOptions.IgnoreCase);
        if (contentMatch.Success)
        {
            var sceneNum = int.Parse(contentMatch.Groups[1].Value);
            var newContent = contentMatch.Groups[2].Value.Trim();
            var targetScene = project.Scenes.FirstOrDefault(s => s.OrderIndex == sceneNum);
            if (targetScene != null)
            {
                targetScene.Content = newContent;
                targetScene.AudioUrl = null; // Cần sinh lại audio
                modified = true;
            }
        }

        // 4. Đổi giọng đọc (Ví dụ: "đổi giọng sang Hải Đăng", "đổi giọng Lan Nhi")
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

        // Nếu lệnh chung chung, bổ sung kịch bản hoặc cập nhật ghi chú
        project.Description = (project.Description ?? "") + $"\n[Ghi chú biên tập]: {userInstruction}";
        project.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return (true, $"Đã ghi nhận yêu cầu biên tập: \"{userInstruction}\"", project);
    }
}
