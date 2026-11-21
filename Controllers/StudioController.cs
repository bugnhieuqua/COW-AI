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
    private readonly VideoDurationEstimator _estimator;
    private readonly IImageCompressionService _imageCompressor;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<StudioController> _logger;

    public StudioController(
        IStudioService studioService, 
        IVieNeuTtsService vieNeuTts,
        IVideoRenderService videoRender,
        VideoDurationEstimator estimator,
        IImageCompressionService imageCompressor,
        IWebHostEnvironment env,
        ILogger<StudioController> logger)
    {
        _studioService = studioService;
        _vieNeuTts = vieNeuTts;
        _videoRender = videoRender;
        _estimator = estimator;
        _imageCompressor = imageCompressor;
        _env = env;
        _logger = logger;
    }

    private static readonly HashSet<string> AllowedAvatarExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp"
    };

    private static readonly HashSet<string> AllowedDocExtensions = DocumentTextExtractor.AllowedExtensions;

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

    private int EffectiveUserId => CurrentUserId ?? 1;

    private bool CanAccessProject(Project project)
    {
        return IsAdmin || project.UserId == null || project.UserId == EffectiveUserId || project.UserId == 1;
    }

    private async Task<(IActionResult? Error, Project? Project)> AuthorizeProjectAccessAsync(int projectId)
    {
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
        var projectId = await _studioService.GetSceneProjectIdAsync(sceneId);
        if (!projectId.HasValue)
        {
            return NotFound(new { error = "Không tìm thấy phân cảnh." });
        }

        var (err, _) = await AuthorizeProjectAccessAsync(projectId.Value);
        return err;
    }

    [HttpGet("/studio/{id:int?}")]
    [HttpGet("/studio/editor/{id:int?}")]
    public async Task<IActionResult> Index(int? id, [FromQuery] string? view)
    {
        // Cho phép khách vãng lai dùng thử Studio ở chế độ Xem/Xem trước (Preview Only)
        var isAuthenticated = User.Identity?.IsAuthenticated == true;
        List<Project> projects = new();
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
        }
        else
        {
            if (id.HasValue)
            {
                activeProject = await _studioService.GetProjectAsync(id.Value);
            }
        }

        var isEditorRoute = Request.Path.Value?.Contains("/editor") == true;
        // Nếu vào tuyến /editor mà không chỉ định id, lấy dự án gần nhất nếu có
        if (isEditorRoute && activeProject == null && projects.Count > 0)
        {
            activeProject = await _studioService.GetProjectAsync(projects[0].Id);
        }

        // Nếu cố vào editor nhưng không có bất kỳ dự án nào, chuyển hướng về Studio Hub
        if (isEditorRoute && activeProject == null)
        {
            return Redirect("/studio");
        }

        var activeView = (activeProject != null && (view == "editor" || isEditorRoute)) ? "editor" : "hub";

        ViewData["Title"] = (activeView == "editor" && activeProject != null)
            ? $"{activeProject.Title} - Trình biên tập COW.AI Studio"
            : "COW.AI Studio - Không gian Sáng tạo Video Bài Giảng AI";
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

        var project = await _studioService.CreateProjectAsync(CurrentUserId, req.Title, req.Description, req.Voice, req.AspectRatio, req.IsAspectLocked);
        return Ok(project);
    }

    [HttpPut("/api/studio/projects/{id:int}/aspect-ratio")]
    public async Task<IActionResult> UpdateAspectRatio(int id, [FromBody] UpdateAspectRatioRequest? req)
    {
        var (authErr, _) = await AuthorizeProjectAccessAsync(id);
        if (authErr != null) return authErr;

        var normalized = StudioService.NormalizeAspectRatio(req?.AspectRatio);
        var ok = await _studioService.UpdateProjectAspectRatioAsync(id, normalized);
        if (!ok) return NotFound(new { error = "Không tìm thấy dự án." });
        return Ok(new { success = true, aspectRatio = normalized });
    }

    [HttpGet("/api/studio/projects/{id:int}")]
    public async Task<IActionResult> GetProject(int id)
    {
        var (authErr, project) = await AuthorizeDemoOrOwnedProjectAsync(id);
        if (authErr != null) return authErr;
        return Ok(project);
    }

    private async Task<(IActionResult? Error, Project? Project)> AuthorizeDemoOrOwnedProjectAsync(int id)
    {
        var project = await _studioService.GetProjectAsync(id);
        if (project == null) return (NotFound(new { error = "Không tìm thấy dự án." }), null);
        if (id != 1 && project.UserId != null)
        {
            return await AuthorizeProjectAccessAsync(id);
        }
        return (null, project);
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

    [HttpPost("/api/studio/scenes/{id:int}/upload-image")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 50 * 1024 * 1024)]
    public async Task<IActionResult> UploadSceneImage(int id, IFormFile? file)
    {
        try
        {
            var authErr = await AuthorizeSceneAccessAsync(id);
            if (authErr != null) return authErr;

            if (file == null || file.Length == 0) return BadRequest(new { error = "Vui lòng chọn tệp ảnh để tải lên." });

            var (success, error, imageUrl) = await _studioService.UploadSceneImageAsync(id, file);
            if (!success) return BadRequest(new { error });

            return Ok(new { success = true, imageUrl });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi ngoại lệ khi tải ảnh cho phân cảnh {SceneId}", id);
            return StatusCode(500, new { error = "Không thể xử lý ảnh tải lên: " + ex.Message });
        }
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
        var (authErr, _) = await AuthorizeDemoOrOwnedProjectAsync(id);
        if (authErr != null) return authErr;

        var (success, error, html) = await _studioService.GenerateCompositionAsync(id);
        if (!success) return NotFound(error);
        return Content(html, "text/html", System.Text.Encoding.UTF8);
    }

    [HttpGet("/api/studio/projects/{id:int}/composition")]
    public async Task<IActionResult> GetComposition(int id)
    {
        var (authErr, _) = await AuthorizeDemoOrOwnedProjectAsync(id);
        if (authErr != null) return authErr;

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

    [HttpPost("/api/studio/scenes/{id:int}/move")]
    public async Task<IActionResult> MoveScene(int id, [FromBody] MoveSceneRequest? req)
    {
        var authErr = await AuthorizeSceneAccessAsync(id);
        if (authErr != null) return authErr;

        var dir = req?.Direction ?? "down";
        var (success, error, scenes) = await _studioService.MoveSceneAsync(id, dir);
        if (!success) return BadRequest(new { error });
        return Ok(new { success = true, scenes });
    }

    [HttpPost("/api/studio/scenes/{id:int}/duplicate")]
    public async Task<IActionResult> DuplicateScene(int id)
    {
        var authErr = await AuthorizeSceneAccessAsync(id);
        if (authErr != null) return authErr;

        var (success, error, scene, scenes) = await _studioService.DuplicateSceneAsync(id);
        if (!success) return BadRequest(new { error });
        return Ok(new { success = true, scene, scenes });
    }

    [HttpGet("/api/studio/projects/{id:int}/render-progress")]
    public async Task<IActionResult> GetRenderProgress(int id)
    {
        var (authErr, _) = await AuthorizeProjectAccessAsync(id);
        if (authErr != null) return authErr;

        return Ok(_videoRender.GetRenderProgress(id));
    }

    [HttpGet("/api/studio/voices")]
    public IActionResult GetVoices()
    {
        var voices = _vieNeuTts.GetAvailableVoices();
        return Ok(voices);
    }

    [HttpGet("/api/studio/vieneu-status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult GetVieNeuStatus([FromQuery] bool warmup = false)
    {
        if (warmup)
        {
            _vieNeuTts.TriggerWarmUp();
        }
        return Ok(_vieNeuTts.GetServerStatus());
    }

    [HttpPut("/api/studio/projects/{id:int}/avatars")]
    public async Task<IActionResult> UpdateAvatars(int id, [FromBody] UpdateAvatarsRequest? req)
    {
        var (authErr, _) = await AuthorizeProjectAccessAsync(id);
        if (authErr != null) return authErr;

        var saved = await _studioService.UpdateProjectAvatarsAsync(id, req?.AvatarUrls ?? new List<string>());
        if (saved == null) return NotFound(new { error = "Không tìm thấy dự án." });
        return Ok(new { success = true, avatarUrls = saved });
    }

    [HttpPost("/api/studio/projects/{id:int}/upload-source")]
    [RequestSizeLimit(104_857_600)]
    [RequestFormLimits(MultipartBodyLengthLimit = 104_857_600)]
    public async Task<IActionResult> UploadSource(int id, [FromForm] IFormFile? file, [FromForm] List<IFormFile>? avatars)
    {
        var (authErr, existingProject) = await AuthorizeProjectAccessAsync(id);
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
                return BadRequest(new { error = "Định dạng chưa hỗ trợ. Hãy chọn .pdf, .pptx, .docx, .xlsx, .csv, .html, .epub, .ipynb, .xml, .json, .txt hoặc .md." });
            }

            sourceFileName = Path.GetFileName(file.FileName);
            (sourceContent, warning) = await ExtractTextFromUploadedFileAsync(file);
        }

        var existingAvatars = StudioService.ParseAvatarsJson(existingProject?.AvatarsJson);
        int remainingSlots = Math.Max(0, 6 - existingAvatars.Count);

        var newlyUploadedAvatars = new List<string>();
        if (avatars != null && avatars.Count > 0)
        {
            if (remainingSlots == 0)
            {
                return BadRequest(new { error = "Dự án đã đạt tối đa 6 ảnh chân dung. Hãy xóa bớt ảnh cũ trước khi thêm ảnh mới." });
            }

            var avatarDir = Path.Combine(_env.WebRootPath, "media", "studio", "avatars", $"proj_{id}");
            Directory.CreateDirectory(avatarDir);

            int avatarIdx = existingAvatars.Count + 1;
            foreach (var av in avatars.Take(remainingSlots))
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

                // Tự động nén ảnh đại diện trước khi lưu để tiết kiệm đĩa
                await using var avStream = av.OpenReadStream();
                var (_, compBytes, compExt, _) = await _imageCompressor.CompressImageAsync(avStream, av.FileName, maxDimension: 1024, quality: 85);

                var avFileName = $"avatar_{avatarIdx++}_{Guid.NewGuid():N}{compExt}";
                var avLocalPath = Path.Combine(avatarDir, avFileName);
                await System.IO.File.WriteAllBytesAsync(avLocalPath, compBytes);

                newlyUploadedAvatars.Add($"/media/studio/avatars/proj_{id}/{avFileName}");
            }
        }

        var savedProject = await _studioService.SaveProjectSourceAsync(
            id,
            sourceFileName,
            sourceContent,
            newlyUploadedAvatars.Count > 0 ? newlyUploadedAvatars : null);

        var allAvatarUrls = StudioService.ParseAvatarsJson(savedProject?.AvatarsJson);
        var normalizedMd = savedProject?.SourceMarkdown ?? sourceContent ?? string.Empty;
        var durationEstimate = _estimator.Estimate(
            normalizedMd,
            savedProject?.VideoType,
            savedProject?.SequentialSubMode,
            savedProject?.DurationPreset,
            savedProject?.AspectRatio);

        return Ok(new
        {
            success = true,
            sourceFileName = savedProject?.SourceFileName ?? sourceFileName,
            sourceContent = normalizedMd,
            sourceMarkdown = normalizedMd,
            durationEstimate,
            warning,
            avatarUrls = allAvatarUrls,
            projectTitle = savedProject?.Title,
            projectStatus = savedProject?.Status,
            recommendedVideoType = durationEstimate.RecommendedVideoType,
            recommendedSequentialSubMode = durationEstimate.RecommendedSequentialSubMode,
            updatedAt = savedProject?.UpdatedAt
        });
    }

    // Ủy quyền trích xuất word/document.xml, .pptx, .pdf, .txt, .md cho DocumentTextExtractor dùng chung
    private Task<(string Content, string? Warning)> ExtractTextFromUploadedFileAsync(IFormFile file)
    {
        return DocumentTextExtractor.ExtractAsync(file, _logger);
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
            req?.AvatarUrls ?? new List<string>(),
            req?.SequentialSubMode ?? "auto",
            req?.DurationPreset ?? "auto");

        if (!success) return BadRequest(new { error });
        var project = await _studioService.GetProjectAsync(id);
        var durationEstimate = _estimator.Estimate(
            project?.SourceMarkdown ?? req?.SourceContent,
            project?.VideoType ?? req?.VideoType,
            project?.SequentialSubMode ?? req?.SequentialSubMode,
            project?.DurationPreset ?? req?.DurationPreset,
            project?.AspectRatio ?? "16:9");

        return Ok(new { success = true, planMarkdown, durationEstimate, project });
    }

    [HttpGet("/api/studio/projects/{id:int}/export-md")]
    public async Task<IActionResult> ExportMarkdown(int id, [FromQuery] string? kind = "script")
    {
        var (authErr, project) = await AuthorizeProjectAccessAsync(id);
        if (authErr != null) return authErr;
        if (project == null) return NotFound(new { error = "Không tìm thấy dự án." });

        bool isSource = string.Equals(kind, "source", StringComparison.OrdinalIgnoreCase);
        var mdText = isSource
            ? (project.SourceMarkdown ?? project.SourceContent ?? string.Empty)
            : (project.PlanMarkdown ?? string.Empty);

        if (string.IsNullOrWhiteSpace(mdText))
        {
            return BadRequest(new { error = isSource ? "Dự án chưa có tài liệu Markdown nguồn." : "Dự án chưa có kịch bản Markdown." });
        }

        var safeSlug = System.Text.RegularExpressions.Regex.Replace(project.Title ?? $"project_{id}", @"[^\p{L}\p{N}_\-]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(safeSlug)) safeSlug = $"project-{id}";
        var fileName = isSource ? $"{safeSlug}-source.md" : $"{safeSlug}-video-script.md";
        var bytes = System.Text.Encoding.UTF8.GetBytes(mdText);
        return File(bytes, "text/markdown; charset=utf-8", fileName);
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
    public string? SequentialSubMode { get; set; } = "auto";
    public string? DurationPreset { get; set; } = "auto";
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
    public bool IsAspectLocked { get; set; } = false;
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

public class MoveSceneRequest
{
    public string Direction { get; set; } = "down";
}

public class UpdateAspectRatioRequest
{
    public string AspectRatio { get; set; } = "16:9";
}

public class UpdateAvatarsRequest
{
    public List<string>? AvatarUrls { get; set; }
}
