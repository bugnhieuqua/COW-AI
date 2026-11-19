using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using COW_AI.Models;
using COW_AI.Services;

namespace COW_AI.Controllers;

public class StudioController : Controller
{
    private readonly IStudioService _studioService;
    private readonly IVieNeuTtsService _vieNeuTts;
    private readonly IVideoRenderService _videoRender;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<StudioController> _logger;

    public StudioController(
        IStudioService studioService, 
        IVieNeuTtsService vieNeuTts,
        IVideoRenderService videoRender,
        IWebHostEnvironment env,
        ILogger<StudioController> logger)
    {
        _studioService = studioService;
        _vieNeuTts = vieNeuTts;
        _videoRender = videoRender;
        _env = env;
        _logger = logger;
    }

    private static readonly HashSet<string> AllowedAvatarExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp"
    };

    private static readonly HashSet<string> AllowedDocExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pptx", ".docx", ".pdf", ".txt", ".md"
    };

    private const long MaxAvatarBytes = 5 * 1024 * 1024;      // 5 MB
    private const long MaxDocumentBytes = 100 * 1024 * 1024;  // 100 MB
    private const int MaxExtractedChars = 60_000;

    private int? CurrentUserId
    {
        get
        {
            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(idClaim, out var id) ? id : null;
        }
    }

    private bool IsAdmin => User.IsInRole("Admin");

    private bool CanAccessProject(Project project)
    {
        return IsAdmin || (CurrentUserId.HasValue && project.UserId == CurrentUserId.Value);
    }

    private async Task<(IActionResult? Error, Project? Project)> AuthorizeProjectAccessAsync(int projectId)
    {
        if (User.Identity?.IsAuthenticated != true || !CurrentUserId.HasValue)
        {
            return (Unauthorized(new { error = "Vui lòng đăng nhập tài khoản để thực hiện thao tác này." }), null);
        }

        var project = await _studioService.GetProjectAsync(projectId);
        if (project == null)
        {
            return (NotFound(new { error = "Không tìm thấy dự án." }), null);
        }

        if (!CanAccessProject(project))
        {
            return (StatusCode(StatusCodes.Status403Forbidden, new { error = "Bạn không có quyền truy cập dự án này." }), null);
        }

        return (null, project);
    }

    private async Task<IActionResult?> AuthorizeSceneAccessAsync(int sceneId)
    {
        if (User.Identity?.IsAuthenticated != true || !CurrentUserId.HasValue)
        {
            return Unauthorized(new { error = "Vui lòng đăng nhập tài khoản để chỉnh sửa phân cảnh." });
        }

        var projectId = await _studioService.GetSceneProjectIdAsync(sceneId);
        if (!projectId.HasValue)
        {
            return NotFound(new { error = "Không tìm thấy phân cảnh." });
        }

        var (err, _) = await AuthorizeProjectAccessAsync(projectId.Value);
        return err;
    }

    [HttpGet("/studio/{id:int?}")]
    [HttpGet("/studio/editor/{id:int}")]
    public async Task<IActionResult> Index(int? id, [FromQuery] string? view)
    {
        // Cho phép khách vãng lai dùng thử Studio ở chế độ Xem/Xem trước (Preview Only)
        var isAuthenticated = User.Identity?.IsAuthenticated == true;
        List<Project> projects;
        Project? activeProject = null;

        if (isAuthenticated && CurrentUserId.HasValue)
        {
            var userId = CurrentUserId.Value;
            projects = await _studioService.GetUserProjectsAsync(userId);

            if (id.HasValue)
            {
                var requested = await _studioService.GetProjectAsync(id.Value);
                if (requested != null && CanAccessProject(requested))
                {
                    activeProject = requested;
                }
            }

            if (activeProject == null && projects.Count > 0)
            {
                activeProject = await _studioService.GetProjectAsync(projects[0].Id);
            }

            // Nếu user đăng nhập chưa có dự án nào, tạo dự án khởi tạo
            if (activeProject == null)
            {
                activeProject = await _studioService.CreateProjectAsync(
                    userId,
                    "Bài giảng: Kiến trúc An toàn Phần mềm OWASP 2026",
                    "Bài giảng số hóa đa cảnh tự động từ tài liệu với VieNeu 48kHz",
                    "Hải Đăng");

                projects = await _studioService.GetUserProjectsAsync(userId);
            }
        }
        else
        {
            // Khách vãng lai: Danh sách dự án rỗng, chỉ xem trước dự án demo #1
            projects = new List<Project>();
            activeProject = await _studioService.GetProjectAsync(1);

            if (activeProject == null)
            {
                activeProject = await _studioService.CreateProjectAsync(
                    1,
                    "Bài giảng: Kiến trúc An toàn Phần mềm OWASP 2026",
                    "Bài giảng số hóa đa cảnh tự động từ tài liệu với VieNeu 48kHz",
                    "Hải Đăng");
            }
        }

        var isEditorRoute = Request.Path.Value?.Contains("/editor") == true || (id.HasValue && view != "hub");
        var activeView = (view == "editor" || isEditorRoute) ? "editor" : "hub";

        ViewData["Title"] = activeView == "editor" ? $"{activeProject.Title} - Trình biên tập COW.AI Studio" : "COW.AI Studio - Không gian Sáng tạo Video Bài Giảng AI";
        ViewData["Projects"] = projects;
        ViewData["IsAuthenticated"] = isAuthenticated;
        ViewData["ActiveView"] = activeView;

        return View(activeProject);
    }

    [HttpGet("/studio/about")]
    public IActionResult About()
    {
        ViewData["Title"] = "COW.AI Studio - Nền tảng Biên tập & Sản xuất Video AI Đa Cảnh Tự Động";
        return View();
    }

    [HttpPost("/api/studio/projects")]
    public async Task<IActionResult> CreateProject([FromBody] CreateProjectRequest req)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized(new { error = "Vui lòng đăng nhập để tạo dự án video bài giảng." });
        }

        if (req == null || string.IsNullOrWhiteSpace(req.Title))
        {
            return BadRequest(new { error = "Tiêu đề dự án không được để trống." });
        }

        var project = await _studioService.CreateProjectAsync(CurrentUserId, req.Title, req.Description, req.Voice);
        return Ok(project);
    }

    [HttpGet("/api/studio/projects/{id:int}")]
    public async Task<IActionResult> GetProject(int id)
    {
        if (id != 1)
        {
            var (authErr, owned) = await AuthorizeProjectAccessAsync(id);
            if (authErr != null) return authErr;
            return Ok(owned);
        }
        var project = await _studioService.GetProjectAsync(id);
        if (project == null) return NotFound(new { error = "Không tìm thấy dự án." });
        return Ok(project);
    }

    [HttpPost("/api/studio/projects/{id:int}/script")]
    public async Task<IActionResult> GenerateScript(int id, [FromBody] GenerateScriptRequest req)
    {
        var (authErr, _) = await AuthorizeProjectAccessAsync(id);
        if (authErr != null) return authErr;

        var topic = req?.Topic ?? string.Empty;
        var (success, error, scenes) = await _studioService.GenerateScriptAsync(id, topic);
        if (!success)
        {
            return BadRequest(new { error });
        }

        var updatedProject = await _studioService.GetProjectAsync(id);
        return Ok(new { success = true, scenes, project = updatedProject });
    }

    [HttpPost("/api/studio/scenes/{id:int}/audio")]
    public async Task<IActionResult> GenerateSceneAudio(int id)
    {
        var authErr = await AuthorizeSceneAccessAsync(id);
        if (authErr != null) return authErr;

        var (success, error, audioUrl, duration) = await _studioService.GenerateSceneAudioAsync(id);
        if (!success) return BadRequest(new { error });
        return Ok(new { success = true, audioUrl, duration });
    }

    [HttpPost("/api/studio/scenes/{id:int}/image")]
    public async Task<IActionResult> GenerateSceneImage(int id)
    {
        var authErr = await AuthorizeSceneAccessAsync(id);
        if (authErr != null) return authErr;

        var (success, error, imageUrl) = await _studioService.GenerateSceneImageAsync(id);
        if (!success) return BadRequest(new { error });
        return Ok(new { success = true, imageUrl });
    }

    [HttpPost("/api/studio/projects/{id:int}/auto-produce")]
    public async Task<IActionResult> AutoProduce(int id)
    {
        var (authErr, _) = await AuthorizeProjectAccessAsync(id);
        if (authErr != null) return authErr;

        var (success, error) = await _studioService.AutoProduceProjectAsync(id);
        if (!success) return BadRequest(new { error });
        var updatedProject = await _studioService.GetProjectAsync(id);
        return Ok(new { success = true, project = updatedProject });
    }

    [HttpDelete("/api/studio/projects/{id:int}")]
    public async Task<IActionResult> DeleteProject(int id)
    {
        var (authErr, _) = await AuthorizeProjectAccessAsync(id);
        if (authErr != null) return authErr;

        var success = await _studioService.DeleteProjectAsync(id);
        if (!success) return NotFound(new { error = "Không tìm thấy dự án để xóa." });
        return Ok(new { success = true });
    }

    [HttpPost("/api/studio/projects/bulk-delete")]
    public async Task<IActionResult> BulkDeleteProjects([FromBody] BulkDeleteProjectsRequest? req)
    {
        if (User.Identity?.IsAuthenticated != true || !CurrentUserId.HasValue)
        {
            return Unauthorized(new { error = "Vui lòng đăng nhập để xóa dự án." });
        }

        if (req == null || req.ProjectIds == null || req.ProjectIds.Count == 0)
        {
            return BadRequest(new { error = "Danh sách dự án cần xóa không được để trống." });
        }

        int deletedCount = 0;
        foreach (var id in req.ProjectIds)
        {
            var (authErr, _) = await AuthorizeProjectAccessAsync(id);
            if (authErr != null) continue;
            if (await _studioService.DeleteProjectAsync(id))
            {
                deletedCount++;
            }
        }

        return Ok(new { success = true, deletedCount });
    }

    [HttpGet("/studio/{id:int}/preview")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PreviewComposition(int id)
    {
        if (id != 1)
        {
            var (authErr, _) = await AuthorizeProjectAccessAsync(id);
            if (authErr != null) return authErr;
        }
        var (success, error, html) = await _studioService.GenerateCompositionAsync(id);
        if (!success) return NotFound(error);
        return Content(html, "text/html", System.Text.Encoding.UTF8);
    }

    [HttpGet("/api/studio/projects/{id:int}/composition")]
    public async Task<IActionResult> GetComposition(int id)
    {
        if (id != 1)
        {
            var (authErr, _) = await AuthorizeProjectAccessAsync(id);
            if (authErr != null) return authErr;
        }
        var (success, error, html) = await _studioService.GenerateCompositionAsync(id);
        if (!success) return BadRequest(new { error });
        return Ok(new { success = true, html });
    }

    [HttpPut("/api/studio/scenes/{id:int}")]
    public async Task<IActionResult> UpdateScene(int id, [FromBody] UpdateSceneRequest req)
    {
        var authErr = await AuthorizeSceneAccessAsync(id);
        if (authErr != null) return authErr;

        var (success, error, scene) = await _studioService.UpdateScenePropertiesAsync(
            id,
            req?.Title,
            req?.Content,
            req?.ImagePrompt,
            req?.DurationSeconds,
            req?.AnimationEffect,
            req?.FilterPreset,
            req?.SubtitlePosition);

        if (!success) return BadRequest(new { error });
        return Ok(new { success = true, scene });
    }

    [HttpPost("/api/studio/projects/{id:int}/scenes")]
    public async Task<IActionResult> AddScene(int id)
    {
        var (authErr, _) = await AuthorizeProjectAccessAsync(id);
        if (authErr != null) return authErr;

        var scene = await _studioService.AddSceneAsync(id);
        return Ok(new { success = true, scene });
    }

    [HttpDelete("/api/studio/scenes/{id:int}")]
    public async Task<IActionResult> DeleteScene(int id)
    {
        var authErr = await AuthorizeSceneAccessAsync(id);
        if (authErr != null) return authErr;

        var success = await _studioService.DeleteSceneAsync(id);
        if (!success) return NotFound(new { error = "Không tìm thấy phân cảnh để xóa." });
        return Ok(new { success = true });
    }

    [HttpGet("/api/studio/voices")]
    public IActionResult GetVoices()
    {
        var voices = _vieNeuTts.GetAvailableVoices();
        return Ok(voices);
    }

    [HttpPost("/api/studio/projects/{id:int}/upload-source")]
    [RequestSizeLimit(104_857_600)]
    [RequestFormLimits(MultipartBodyLengthLimit = 104_857_600)]
    public async Task<IActionResult> UploadSource(int id, [FromForm] IFormFile? file, [FromForm] List<IFormFile>? avatars)
    {
        var (authErr, _) = await AuthorizeProjectAccessAsync(id);
        if (authErr != null) return authErr;

        string sourceContent = string.Empty;
        string? sourceFileName = null;
        string? warning = null;

        if (file != null && file.Length > 0)
        {
            if (file.Length > MaxDocumentBytes)
            {
                return BadRequest(new { error = "Tệp tài liệu vượt quá giới hạn 100 MB." });
            }

            var docExt = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(docExt) || !AllowedDocExtensions.Contains(docExt))
            {
                return BadRequest(new { error = "Chỉ hỗ trợ tài liệu định dạng .pptx, .docx, .pdf, .txt hoặc .md." });
            }

            sourceFileName = Path.GetFileName(file.FileName);
            (sourceContent, warning) = await ExtractTextFromUploadedFileAsync(file);
        }

        var avatarUrls = new List<string>();
        if (avatars != null && avatars.Count > 0)
        {
            var avatarDir = Path.Combine(_env.WebRootPath, "media", "studio", "avatars", $"proj_{id}");
            Directory.CreateDirectory(avatarDir);

            int avatarIdx = 1;
            foreach (var av in avatars.Take(3))
            {
                if (av.Length == 0) continue;
                if (av.Length > MaxAvatarBytes)
                {
                    return BadRequest(new { error = "Ảnh giảng viên vượt quá giới hạn 5 MB mỗi tệp." });
                }

                var ext = Path.GetExtension(av.FileName);
                if (string.IsNullOrWhiteSpace(ext) || !AllowedAvatarExtensions.Contains(ext))
                {
                    return BadRequest(new { error = "Ảnh giảng viên chỉ chấp nhận định dạng .png, .jpg, .jpeg hoặc .webp." });
                }

                var avFileName = $"avatar_{avatarIdx++}_{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
                var avLocalPath = Path.Combine(avatarDir, avFileName);
                await using (var stream = new FileStream(avLocalPath, FileMode.Create))
                {
                    await av.CopyToAsync(stream);
                }
                avatarUrls.Add($"/media/studio/avatars/proj_{id}/{avFileName}");
            }
        }

        return Ok(new
        {
            success = true,
            sourceFileName,
            sourceContent,
            warning,
            avatarUrls
        });
    }

    private async Task<(string Content, string? Warning)> ExtractTextFromUploadedFileAsync(IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        try
        {
            if (ext == ".pptx")
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                ms.Position = 0;

                using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: false);
                var slideEntries = zip.Entries
                    .Where(e => System.Text.RegularExpressions.Regex.IsMatch(
                        e.FullName, @"^ppt/slides/slide(\d+)\.xml$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    .OrderBy(e =>
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(e.FullName, @"slide(\d+)\.xml$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        return m.Success ? int.Parse(m.Groups[1].Value) : 999;
                    })
                    .ToList();

                var sb = new System.Text.StringBuilder();
                int slideNum = 1;
                foreach (var entry in slideEntries)
                {
                    using var entryStream = entry.Open();
                    using var reader = new StreamReader(entryStream, System.Text.Encoding.UTF8);
                    var xml = await reader.ReadToEndAsync();
                    var textMatches = System.Text.RegularExpressions.Regex.Matches(xml, @"<a:t(?:\s+[^>]*)?>([\s\S]*?)<\/a:t>");
                    if (textMatches.Count > 0)
                    {
                        sb.AppendLine($"### Slide {slideNum}:");
                        foreach (System.Text.RegularExpressions.Match tm in textMatches)
                        {
                            var txt = System.Net.WebUtility.HtmlDecode(tm.Groups[1].Value).Trim();
                            if (!string.IsNullOrEmpty(txt))
                            {
                                sb.Append(txt).Append(' ');
                            }
                        }
                        sb.AppendLine().AppendLine();
                    }
                    slideNum++;
                }

                // Trích thêm Ghi chú diễn giả (Speaker Notes) hoặc SmartArt nếu có
                var extraEntries = zip.Entries
                    .Where(e => System.Text.RegularExpressions.Regex.IsMatch(
                        e.FullName, @"^ppt/(notesSlides/notesSlide|diagrams/data)\d+\.xml$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    .ToList();
                foreach (var extra in extraEntries)
                {
                    using var es = extra.Open();
                    using var er = new StreamReader(es, System.Text.Encoding.UTF8);
                    var xml = await er.ReadToEndAsync();
                    var matches = System.Text.RegularExpressions.Regex.Matches(xml, @"<a:t(?:\s+[^>]*)?>([\s\S]*?)<\/a:t>");
                    if (matches.Count == 0) continue;
                    var noteLine = string.Join(" ", matches.Cast<System.Text.RegularExpressions.Match>()
                        .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim())
                        .Where(t => !string.IsNullOrEmpty(t) && !int.TryParse(t, out _)));
                    if (noteLine.Length > 8) sb.AppendLine(noteLine);
                }

                var extracted = TrimExtractedText(sb.ToString());
                if (extracted.Length > 0) return (extracted, null);
                return (string.Empty, "Tệp PPTX không có văn bản trích xuất được (có thể các slide chỉ chứa hình ảnh). Hãy dán dàn ý ngắn vào ô bên phải.");
            }

            if (ext == ".docx")
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                ms.Position = 0;

                using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: false);
                var docEntry = zip.GetEntry("word/document.xml");
                if (docEntry != null)
                {
                    using var entryStream = docEntry.Open();
                    using var reader = new StreamReader(entryStream, System.Text.Encoding.UTF8);
                    var xml = await reader.ReadToEndAsync();
                    var paraMatches = System.Text.RegularExpressions.Regex.Matches(xml, @"<w:p\b[\s\S]*?<\/w:p>");
                    var sb = new System.Text.StringBuilder();
                    foreach (System.Text.RegularExpressions.Match pm in paraMatches)
                    {
                        var textMatches = System.Text.RegularExpressions.Regex.Matches(pm.Value, @"<w:t(?:\s+[^>]*)?>([\s\S]*?)<\/w:t>");
                        if (textMatches.Count == 0) continue;
                        var line = string.Concat(textMatches.Cast<System.Text.RegularExpressions.Match>()
                            .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value))).Trim();
                        if (!string.IsNullOrEmpty(line)) sb.AppendLine(line);
                    }
                    var extracted = TrimExtractedText(sb.ToString());
                    if (extracted.Length > 0) return (extracted, null);
                }
                return (string.Empty, "Tệp DOCX không có nội dung văn bản đọc được. Hãy nhập trực tiếp dàn ý vào ô bên phải.");
            }

            if (ext == ".pdf")
            {
                using var stream = file.OpenReadStream();
                using var reader = new StreamReader(stream, System.Text.Encoding.Latin1);
                var raw = await reader.ReadToEndAsync();
                var matches = System.Text.RegularExpressions.Regex.Matches(raw, @"\(([^()\\]{2,})\)");
                var sb = new System.Text.StringBuilder();
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    var piece = m.Groups[1].Value.Trim();
                    if (piece.Length >= 2 && piece.Any(char.IsLetter))
                    {
                        sb.AppendLine(piece);
                    }
                }
                var extracted = TrimExtractedText(sb.ToString());
                if (extracted.Length > 20) return (extracted, null);
                return (string.Empty, "Tệp PDF này dùng font nén/ảnh scan nên chưa tách chữ trực tiếp được. Hãy copy-paste nội dung hoặc xuất sang DOCX/PPTX/TXT.");
            }

            // Văn bản thuần (.txt, .md)
            using var textStream = file.OpenReadStream();
            using var textReader = new StreamReader(textStream, System.Text.Encoding.UTF8);
            return (TrimExtractedText(await textReader.ReadToEndAsync()), null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể trích xuất văn bản từ tệp {FileName}", file.FileName);
            return (string.Empty, "Không thể đọc cấu trúc tệp này (tệp có thể bị mật khẩu hoặc hỏng). Hãy thử lưu lại hoặc dán nội dung trực tiếp.");
        }
    }

    private static string TrimExtractedText(string text)
    {
        var clean = (text ?? string.Empty).Trim();
        return clean.Length > MaxExtractedChars ? clean[..MaxExtractedChars] : clean;
    }

    [HttpPost("/api/studio/projects/{id:int}/render-backend-video")]
    public async Task<IActionResult> RenderBackendVideo(int id, [FromBody] RenderVideoRequest? req)
    {
        var (authErr, _) = await AuthorizeProjectAccessAsync(id);
        if (authErr != null) return authErr;

        var result = await _videoRender.RenderProjectVideoAsync(
            id,
            req?.AspectRatio ?? "16:9",
            req?.IncludeBgm ?? true,
            req?.RenderMode ?? "fast");

        if (!result.Success)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(new
        {
            success = true,
            videoUrl = result.VideoUrl,
            duration = result.TotalDurationSeconds
        });
    }

    [HttpPost("/api/studio/projects/{id:int}/generate-plan")]
    public async Task<IActionResult> GeneratePlan(int id, [FromBody] GeneratePlanRequest req)
    {
        var (authErr, _) = await AuthorizeProjectAccessAsync(id);
        if (authErr != null) return authErr;

        var (success, error, planMarkdown) = await _studioService.GeneratePlanMarkdownAsync(
            id,
            req?.SourceContent ?? string.Empty,
            req?.VideoType ?? "comparison",
            req?.Voice ?? "Hải Đăng",
            req?.AvatarUrls ?? new List<string>());

        if (!success) return BadRequest(new { error });
        var project = await _studioService.GetProjectAsync(id);
        return Ok(new { success = true, planMarkdown, project });
    }

    [HttpPost("/api/studio/projects/{id:int}/apply-plan")]
    public async Task<IActionResult> ApplyPlan(int id, [FromBody] ApplyPlanRequest req)
    {
        var (authErr, _) = await AuthorizeProjectAccessAsync(id);
        if (authErr != null) return authErr;

        var (success, error, scenes) = await _studioService.ApplyPlanAndProduceAsync(
            id,
            req?.PlanMarkdown ?? string.Empty,
            req?.AutoProduce ?? false);

        if (!success) return BadRequest(new { error });
        var project = await _studioService.GetProjectAsync(id);
        return Ok(new { success = true, scenes, project });
    }

    [HttpPost("/api/studio/projects/{id:int}/chat-edit")]
    public async Task<IActionResult> ChatEdit(int id, [FromBody] ChatEditRequest req)
    {
        var (authErr, _) = await AuthorizeProjectAccessAsync(id);
        if (authErr != null) return authErr;

        var (success, message, project) = await _studioService.ProcessChatEditAsync(id, req?.Instruction ?? string.Empty);
        if (!success) return BadRequest(new { error = message });
        return Ok(new { success = true, message, project });
    }
}

public class GeneratePlanRequest
{
    public string? SourceContent { get; set; }
    public string? VideoType { get; set; } = "comparison";
    public string? Voice { get; set; }
    public List<string>? AvatarUrls { get; set; }
}

public class ApplyPlanRequest
{
    public string? PlanMarkdown { get; set; }
    public bool AutoProduce { get; set; }
}

public class ChatEditRequest
{
    public string? Instruction { get; set; }
}

public class UpdateSceneRequest
{
    public string? Title { get; set; }
    public string? Content { get; set; }
    public string? ImagePrompt { get; set; }
    public double? DurationSeconds { get; set; }
    public string? AnimationEffect { get; set; }
    public string? FilterPreset { get; set; }
    public string? SubtitlePosition { get; set; }
}

public class CreateProjectRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Voice { get; set; }
    public string? AspectRatio { get; set; } = "16:9";
}

public class GenerateScriptRequest
{
    public string? Topic { get; set; }
}

public class RenderVideoRequest
{
    public string? AspectRatio { get; set; } = "16:9";
    public bool IncludeBgm { get; set; } = true;
    public string? RenderMode { get; set; } = "fast";
}

public class BulkDeleteProjectsRequest
{
    public List<int>? ProjectIds { get; set; }
}
