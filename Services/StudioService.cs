using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using COW_AI.Data;
using COW_AI.Models;

namespace COW_AI.Services;

public class StudioService : IStudioService
{
    private readonly AppDbContext _db;
    private readonly OllamaService _ollama;
    private readonly OpenRouterService _openRouter;
    private readonly ITtsService _tts;
    private readonly LocalImageService _localImage;
    private readonly PollinationsService _pollinations;
    private readonly IVieNeuTtsService _vieNeuTts;
    private readonly IAutoVideoService _autoVideo;
    private readonly VideoDurationEstimator _estimator;
    private readonly IImageCompressionService _imageCompressor;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<StudioService> _logger;

    public StudioService(
        AppDbContext db,
        OllamaService ollama,
        OpenRouterService openRouter,
        ITtsService tts,
        LocalImageService localImage,
        PollinationsService pollinations,
        IVieNeuTtsService vieNeuTts,
        IAutoVideoService autoVideo,
        VideoDurationEstimator estimator,
        IImageCompressionService imageCompressor,
        IWebHostEnvironment env,
        ILogger<StudioService> logger)
    {
        _db = db;
        _ollama = ollama;
        _openRouter = openRouter;
        _tts = tts;
        _localImage = localImage;
        _pollinations = pollinations;
        _vieNeuTts = vieNeuTts;
        _autoVideo = autoVideo;
        _estimator = estimator;
        _imageCompressor = imageCompressor;
        _env = env;
        _logger = logger;

        EnsureStorageDirectories();
    }

    private void EnsureStorageDirectories()
    {
        try
        {
            var audioDir = Path.Combine(_env.WebRootPath, "media", "studio", "audio");
            var imageDir = Path.Combine(_env.WebRootPath, "media", "studio", "images");
            if (!Directory.Exists(audioDir)) Directory.CreateDirectory(audioDir);
            if (!Directory.Exists(imageDir)) Directory.CreateDirectory(imageDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi tạo thư mục lưu trữ media Studio");
        }
    }

    public Task<int?> GetSceneProjectIdAsync(int sceneId)
    {
        return _db.Scenes.AsNoTracking()
            .Where(s => s.Id == sceneId)
            .Select(s => (int?)s.ProjectId)
            .FirstOrDefaultAsync();
    }

    public async Task<Project?> SaveProjectSourceAsync(
        int projectId,
        string? sourceFileName,
        string? sourceContent,
        List<string>? avatarUrls)
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null) return null;

        if (!string.IsNullOrWhiteSpace(sourceFileName))
        {
            project.SourceFileName = sourceFileName;
            var normalizedMd = _estimator.NormalizeSourceMarkdown(sourceContent, sourceFileName);
            project.SourceContent = normalizedMd;
            project.SourceMarkdown = normalizedMd;

            var est = _estimator.Estimate(normalizedMd, null, project.SequentialSubMode, project.DurationPreset, project.AspectRatio);
            project.EstimatedDurationSeconds = est.TargetDurationSeconds;
            project.VideoType = est.RecommendedVideoType;
            project.SkillKey = AutoVideoService.ResolveSkillKey(project.VideoType, est.RecommendedSequentialSubMode, project.AspectRatio, est.WordCount);

            if (string.Equals(project.Status, "Empty", StringComparison.OrdinalIgnoreCase))
            {
                project.Status = "Draft";
            }

            // Tự đặt tên dự án theo tệp vừa nạp
            var baseName = Path.GetFileNameWithoutExtension(sourceFileName)?.Trim();
            if (!string.IsNullOrWhiteSpace(baseName))
            {
                project.Title = baseName;
            }

            // Xóa bỏ kịch bản và phân cảnh cũ khi nạp tài liệu mới để không bị lẫn nội dung cũ
            project.PlanMarkdown = null;
            var oldScenes = await _db.Scenes.Where(s => s.ProjectId == projectId).ToListAsync();
            if (oldScenes.Count > 0)
            {
                _db.Scenes.RemoveRange(oldScenes);
            }
        }

        if (avatarUrls != null && avatarUrls.Count > 0)
        {
            var merged = ParseAvatarsJson(project.AvatarsJson);
            foreach (var url in avatarUrls)
            {
                if (!string.IsNullOrWhiteSpace(url) && !merged.Contains(url, StringComparer.OrdinalIgnoreCase))
                {
                    merged.Add(url.Trim());
                }
            }
            if (merged.Count > 6)
            {
                merged = merged.Take(6).ToList();
            }
            project.AvatarsJson = JsonSerializer.Serialize(merged);
        }

        project.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _logger.LogInformation("Đã lưu lịch sử tài liệu nguồn '{File}' vào CSDL cho dự án #{Id}", project.SourceFileName, projectId);
        return project;
    }

    public async Task<List<string>?> UpdateProjectAvatarsAsync(int projectId, List<string> avatarUrls)
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null) return null;

        var normalized = (avatarUrls ?? new List<string>())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        project.AvatarsJson = JsonSerializer.Serialize(normalized);
        project.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return normalized;
    }

    public static List<string> ParseAvatarsJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json)
                ?.Where(u => !string.IsNullOrWhiteSpace(u))
                .ToList() ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    public static string NormalizeAspectRatio(string? aspectRatio)
    {
        return (aspectRatio ?? "16:9").Trim() switch
        {
            "9:16" => "9:16",
            "1:1" => "1:1",
            _ => "16:9"
        };
    }

    public async Task<Project> CreateProjectAsync(int? userId, string title, string? description, string? voice, string? aspectRatio = "16:9", bool isAspectLocked = false)
    {
        var normalizedAspect = NormalizeAspectRatio(aspectRatio);
        var project = new Project
        {
            UserId = userId,
            Title = string.IsNullOrWhiteSpace(title) ? "Dự án Studio mới" : title.Trim(),
            Description = description?.Trim(),
            Voice = string.IsNullOrWhiteSpace(voice) ? "Hải Đăng" : voice.Trim(),
            AspectRatio = normalizedAspect,
            IsAspectLocked = isAspectLocked,
            Status = "Empty",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Đã khởi tạo dự án Studio #{Id}: {Title} ({Aspect}, Locked={Locked})", project.Id, project.Title, project.AspectRatio, project.IsAspectLocked);
        return project;
    }

    public async Task<bool> UpdateProjectAspectRatioAsync(int projectId, string aspectRatio)
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null) return false;
        if (project.IsAspectLocked) return true;
        project.AspectRatio = NormalizeAspectRatio(aspectRatio);
        project.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<Project?> GetProjectAsync(int projectId)
    {
        return await _db.Projects
            .Include(p => p.Scenes.OrderBy(s => s.OrderIndex))
            .FirstOrDefaultAsync(p => p.Id == projectId);
    }

    public async Task<List<Project>> GetUserProjectsAsync(int? userId)
    {
        IQueryable<Project> query = _db.Projects.Include(p => p.Scenes);
        if (userId.HasValue)
        {
            query = query.Where(p => p.UserId == userId.Value);
        }
        else
        {
            // Với khách chưa đăng nhập, trả về các dự án không có userId (Demo)
            query = query.Where(p => p.UserId == null);
        }

        // Tự động loại bỏ hoàn toàn các dự án rỗng 0 cảnh và không có tệp nguồn
        return await query
            .Where(p => p.Scenes.Any() || !string.IsNullOrEmpty(p.SourceFileName) || !string.IsNullOrEmpty(p.SourceContent))
            .OrderByDescending(p => p.UpdatedAt)
            .Take(20)
            .ToListAsync();
    }

    public async Task<(bool Success, string? Error, List<Scene> Scenes)> GenerateScriptAsync(int projectId, string topic)
    {
        var project = await _db.Projects.Include(p => p.Scenes).FirstOrDefaultAsync(p => p.Id == projectId);
        if (project == null)
        {
            return (false, "Dự án không tồn tại.", new List<Scene>());
        }

        if (string.IsNullOrWhiteSpace(topic))
        {
            topic = project.Title;
        }

        var promptFile = Path.Combine(_env.ContentRootPath, "Prompts", "StudioScriptGen.txt");
        var promptTemplate = File.Exists(promptFile)
            ? await File.ReadAllTextAsync(promptFile)
            : "Tạo kịch bản video 3 cảnh cho chủ đề {{TOPIC}} dưới dạng JSON array: [{orderIndex, title, content, imagePrompt, durationSeconds}]";

        var prompt = promptTemplate.Replace("{{TOPIC}}", topic);

        string rawResponse = string.Empty;
        try
        {
            var messages = new List<Dictionary<string, string>>
            {
                new() { ["role"] = "user", ["content"] = prompt }
            };

            // Ưu tiên gọi Ollama cục bộ
            try
            {
                rawResponse = await _ollama.ChatAsync(prompt, "cow-ai:latest");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ollama local không phản hồi, thử chuyển tiếp OpenRouter");
                // Fallback nếu có key OpenRouter
                try
                {
                    rawResponse = await _openRouter.ChatAsync(prompt, "google/gemini-2.0-flash-exp:free");
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi gọi AI sinh kịch bản cho dự án #{Id}", projectId);
            return (false, "Hệ thống AI hiện đang bận. Vui lòng thử lại sau.", new List<Scene>());
        }

        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            // Dự phòng kịch bản mẫu nếu không có model LLM nào phản hồi
            rawResponse = GetFallbackScriptJson(topic);
        }

        var jsonArray = ExtractJsonArray(rawResponse);
        if (string.IsNullOrWhiteSpace(jsonArray))
        {
            jsonArray = GetFallbackScriptJson(topic);
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonArray);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return (false, "Định dạng kịch bản AI trả về không hợp lệ.", new List<Scene>());
            }

            // Xóa các cảnh cũ của dự án
            _db.Scenes.RemoveRange(project.Scenes);

            var newScenes = new List<Scene>();
            int order = 1;

            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                var title = elem.TryGetProperty("title", out var tElem) ? tElem.GetString() : $"Cảnh {order}";
                var content = elem.TryGetProperty("content", out var cElem) ? cElem.GetString() : string.Empty;
                var imgPrompt = elem.TryGetProperty("imagePrompt", out var ipElem) ? ipElem.GetString() : $"{topic}, cinematic lighting";
                var duration = elem.TryGetProperty("durationSeconds", out var dElem) && dElem.TryGetDouble(out var dur) ? dur : 5.0;

                if (string.IsNullOrWhiteSpace(content)) continue;

                var scene = new Scene
                {
                    ProjectId = projectId,
                    OrderIndex = order++,
                    Title = string.IsNullOrWhiteSpace(title) ? $"Cảnh {order}" : title.Trim(),
                    Content = content.Trim(),
                    ImagePrompt = imgPrompt?.Trim(),
                    DurationSeconds = duration > 0 ? duration : 5.0
                };

                newScenes.Add(scene);
            }

            if (newScenes.Count == 0)
            {
                return (false, "Không thể trích xuất các cảnh hợp lệ từ kịch bản.", new List<Scene>());
            }

            project.Scenes = newScenes;
            project.ScriptJson = jsonArray;
            project.Status = "Reviewing";
            project.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            _logger.LogInformation("Đã tạo {Count} cảnh cho dự án #{Id}", newScenes.Count, projectId);
            return (true, null, newScenes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi phân tích cú pháp JSON kịch bản cho dự án #{Id}", projectId);
            return (false, "Lỗi định dạng dữ liệu kịch bản: " + ex.Message, new List<Scene>());
        }
    }

    public async Task<(bool Success, string? Error, string? AudioUrl, double Duration)> GenerateSceneAudioAsync(int sceneId)
    {
        var scene = await _db.Scenes.Include(s => s.Project).FirstOrDefaultAsync(s => s.Id == sceneId);
        if (scene == null) return (false, "Cảnh không tồn tại.", null, 0);

        var voice = scene.Project?.Voice ?? "Hải Đăng";

        try
        {
            // Bắt buộc 100% sử dụng VieNeu-TTS 48kHz với câu đọc đã phiên âm (ttsText)
            var spokenText = !string.IsNullOrWhiteSpace(scene.TtsText)
                ? scene.TtsText
                : Regex.Replace(scene.Content ?? string.Empty, @"\[/?kw\]", "", RegexOptions.IgnoreCase);

            var (success, error, genUrl, dur) = await _vieNeuTts.GenerateAudioAsync(
                spokenText,
                voice,
                $"proj_{scene.ProjectId}");

            if (!success || string.IsNullOrWhiteSpace(genUrl))
            {
                _logger.LogWarning("VieNeu-TTS chưa tạo được audio cho Scene #{Id}: {Error}", sceneId, error);
                return (false, error ?? "Server âm thanh VieNeu-TTS đang load hoặc chưa sẵn sàng. Vui lòng đợi giây lát.", null, 0);
            }

            scene.AudioUrl = genUrl;
            scene.DurationSeconds = Math.Max(2.5, Math.Round(dur > 0 ? dur : 4.0, 1));
            await _db.SaveChangesAsync();

            _logger.LogInformation("Sinh âm thanh VieNeu-TTS thành công cho Scene #{Id}: {Url} ({Duration}s)", scene.Id, genUrl, scene.DurationSeconds);
            return (true, null, genUrl, scene.DurationSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi tạo audio VieNeu-TTS cho Scene #{Id}", sceneId);
            return (false, "Lỗi sinh giọng đọc VieNeu-TTS: " + ex.Message, null, 0);
        }
    }

    private async Task<double> MeasureAudioDurationWithFfprobeAsync(string localAudioPath, string text)
    {
        var ffprobePath = @"G:\Video\ffmpeg\bin\ffprobe.exe";
        if (File.Exists(ffprobePath) && File.Exists(localAudioPath))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{localAudioPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    var output = await proc.StandardOutput.ReadToEndAsync();
                    await proc.WaitForExitAsync();
                    if (double.TryParse(output.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var dur) && dur > 0)
                    {
                        return dur;
                    }
                }
            }
            catch { }
        }

        var wordCount = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(3.0, Math.Round(wordCount / 3.2, 1));
    }

    public async Task<(bool Success, string? Error, string? ImageUrl)> GenerateSceneImageAsync(int sceneId)
    {
        var scene = await _db.Scenes.FirstOrDefaultAsync(s => s.Id == sceneId);
        if (scene == null) return (false, "Cảnh không tồn tại.", null);

        var prompt = string.IsNullOrWhiteSpace(scene.ImagePrompt) ? scene.Content : scene.ImagePrompt;
        string? finalImageUrl = null;
        var imageDir = Path.Combine(_env.WebRootPath, "media", "studio", "images");
        Directory.CreateDirectory(imageDir);

        try
        {
            // 1. Thử tạo bằng Local SDXL Turbo trước rồi lưu vĩnh viễn ra đĩa (tránh mất ảnh trong RAM cache khi restart)
            try
            {
                var (localUrl, _, _) = await _localImage.GenerateImageAsync(prompt, 768, 432);
                if (!string.IsNullOrWhiteSpace(localUrl))
                {
                    var localId = localUrl.Split('/').LastOrDefault();
                    var cached = !string.IsNullOrWhiteSpace(localId) ? _localImage.GetCachedImage(localId) : null;
                    if (cached != null && cached.Bytes.Length > 0)
                    {
                        var (_, compBytes, compExt, _) = await _imageCompressor.CompressImageBytesAsync(cached.Bytes, "sdxl.png");
                        var diskName = $"scene_{scene.Id}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{compExt}";
                        var diskPath = Path.Combine(imageDir, diskName);
                        await File.WriteAllBytesAsync(diskPath, compBytes);
                        finalImageUrl = $"/media/studio/images/{diskName}";
                    }
                    else
                    {
                        finalImageUrl = localUrl;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Local SDXL không khả dụng, chuyển sang Pollinations");
            }

            // 2. Fallback qua Pollinations AI và lưu ảnh về đĩa để trình phát sân khấu & video render luôn tải được
            if (string.IsNullOrWhiteSpace(finalImageUrl))
            {
                var pollUrl = _pollinations.GetImageUrl(prompt, 768, 432, "flux");
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
                    var bytes = await http.GetByteArrayAsync(pollUrl);
                    if (bytes.Length > 512)
                    {
                        var (_, compBytes, compExt, _) = await _imageCompressor.CompressImageBytesAsync(bytes, "poll.jpg");
                        var diskName = $"scene_{scene.Id}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{compExt}";
                        var diskPath = Path.Combine(imageDir, diskName);
                        await File.WriteAllBytesAsync(diskPath, compBytes);
                        finalImageUrl = $"/media/studio/images/{diskName}";
                    }
                }
                catch
                {
                    finalImageUrl = pollUrl;
                }
            }

            scene.ImageUrl = finalImageUrl;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Sinh hình ảnh thành công cho Scene #{Id}: {Url}", scene.Id, finalImageUrl);
            return (true, null, finalImageUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi sinh ảnh cho Scene #{Id}", sceneId);
            var fallbackUrl = _pollinations.GetImageUrl(prompt, 768, 432, "flux");
            scene.ImageUrl = fallbackUrl;
            await _db.SaveChangesAsync();
            return (true, null, fallbackUrl);
        }
    }

    public async Task<(bool Success, string? Error, string? ImageUrl)> UploadSceneImageAsync(int sceneId, IFormFile file)
    {
        var scene = await _db.Scenes.FirstOrDefaultAsync(s => s.Id == sceneId);
        if (scene == null) return (false, "Phân cảnh không tồn tại.", null);

        if (file == null || file.Length == 0) return (false, "Tệp ảnh không hợp lệ.", null);
        if (file.Length > 35 * 1024 * 1024) return (false, "Tệp ảnh vượt quá giới hạn 35 MB.", null);

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" or ".jfif"))
        {
            return (false, "Định dạng ảnh không được hỗ trợ (chỉ chấp nhận PNG, JPG, JPEG, WEBP, GIF, BMP, JFIF).", null);
        }

        var webRoot = !string.IsNullOrWhiteSpace(_env.WebRootPath)
            ? _env.WebRootPath
            : Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var imageDir = Path.Combine(webRoot, "media", "studio", "images");
        Directory.CreateDirectory(imageDir);

        byte[] imageBytes;
        string finalExt = ext;

        try
        {
            await using var uploadStream = file.OpenReadStream();
            var (compSuccess, compBytes, compExt, _) = await _imageCompressor.CompressImageAsync(uploadStream, file.FileName);
            if (compSuccess && compBytes != null && compBytes.Length > 0)
            {
                imageBytes = compBytes;
                finalExt = compExt;
            }
            else
            {
                using var rawMs = new MemoryStream();
                await file.CopyToAsync(rawMs);
                imageBytes = rawMs.ToArray();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bộ nén ảnh gặp sự cố, lưu trữ trực tiếp ảnh gốc: {FileName}", file.FileName);
            using var rawMs = new MemoryStream();
            await file.CopyToAsync(rawMs);
            imageBytes = rawMs.ToArray();
        }

        var fileName = $"scene_custom_{sceneId}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{finalExt}";
        var diskPath = Path.Combine(imageDir, fileName);

        await System.IO.File.WriteAllBytesAsync(diskPath, imageBytes);

        var webUrl = $"/media/studio/images/{fileName}";
        scene.ImageUrl = webUrl;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Đã tải và lưu ảnh cho cảnh #{SceneId}: {Url} (Gốc: {OrigKB:F1} KB -> Lưu: {NewKB:F1} KB)",
            sceneId, webUrl, file.Length / 1024.0, imageBytes.Length / 1024.0);
        return (true, null, webUrl);
    }

    public async Task<(bool Success, string? Error)> AutoProduceProjectAsync(int projectId)
    {
        var project = await _db.Projects.Include(p => p.Scenes.OrderBy(s => s.OrderIndex)).FirstOrDefaultAsync(p => p.Id == projectId);
        if (project == null || project.Scenes.Count == 0)
        {
            return (false, "Dự án chưa có cảnh nào để sản xuất.");
        }

        project.Status = "Processing";
        project.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        try
        {
            // Giải phóng VRAM Ollama (qwen2.5:7b) trước khi sinh ảnh SDXL Turbo trên RTX 4060 8GB
            try { await _ollama.UnloadActiveModelsAsync(); } catch { }

            foreach (var scene in project.Scenes)
            {
                // Sinh âm thanh nếu chưa có
                if (string.IsNullOrWhiteSpace(scene.AudioUrl))
                {
                    await GenerateSceneAudioAsync(scene.Id);
                }

                // Sinh hình ảnh nếu chưa có
                if (string.IsNullOrWhiteSpace(scene.ImageUrl))
                {
                    await GenerateSceneImageAsync(scene.Id);
                }
            }

            project.Status = "Completed";
            project.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Dự án Studio #{Id} đã sản xuất hoàn tất (Completed)", projectId);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi trong quy trình AutoProduceProjectAsync #{Id}", projectId);
            project.Status = "Reviewing";
            await _db.SaveChangesAsync();
            return (false, "Lỗi sản xuất tự động: " + ex.Message);
        }
    }

    public async Task<bool> DeleteProjectAsync(int projectId)
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null) return false;

        _db.Projects.Remove(project);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Đã xóa dự án Studio #{Id}", projectId);
        return true;
    }

    public async Task<(bool Success, string? Error, Scene? Scene)> UpdateScenePropertiesAsync(
        int sceneId,
        string? title,
        string? content,
        string? imagePrompt,
        double? duration,
        string? animationEffect,
        string? filterPreset,
        string? subtitlePosition)
    {
        var scene = await _db.Scenes.FindAsync(sceneId);
        if (scene == null) return (false, "Không tìm thấy phân cảnh.", null);

        if (!string.IsNullOrWhiteSpace(title)) scene.Title = title.Trim();
        if (content != null) scene.Content = content.Trim();
        if (imagePrompt != null) scene.ImagePrompt = imagePrompt.Trim();
        if (duration.HasValue && duration.Value >= 0.5) scene.DurationSeconds = Math.Round(duration.Value, 1);
        if (!string.IsNullOrWhiteSpace(animationEffect)) scene.AnimationEffect = animationEffect.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(filterPreset)) scene.FilterPreset = filterPreset.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(subtitlePosition)) scene.SubtitlePosition = subtitlePosition.Trim().ToLowerInvariant();

        await _db.SaveChangesAsync();
        return (true, null, scene);
    }

    public async Task<Scene> AddSceneAsync(int projectId)
    {
        var scenes = await _db.Scenes.Where(s => s.ProjectId == projectId).ToListAsync();
        var nextOrder = scenes.Count == 0 ? 1 : scenes.Max(s => s.OrderIndex) + 1;

        var scene = new Scene
        {
            ProjectId = projectId,
            OrderIndex = nextOrder,
            Title = $"Cảnh {nextOrder}",
            Content = $"Nội dung phân cảnh {nextOrder}...",
            ImagePrompt = $"cinematic visual scene for scene {nextOrder}, detailed, 8k wallpaper",
            DurationSeconds = 4.0,
            AnimationEffect = "ken-burns",
            FilterPreset = "none",
            SubtitlePosition = "bottom"
        };

        _db.Scenes.Add(scene);
        await _db.SaveChangesAsync();
        return scene;
    }

    public async Task<bool> DeleteSceneAsync(int sceneId)
    {
        var scene = await _db.Scenes.FindAsync(sceneId);
        if (scene == null) return false;

        _db.Scenes.Remove(scene);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<(bool Success, string? Error, List<Scene> Scenes)> MoveSceneAsync(int sceneId, string direction)
    {
        var target = await _db.Scenes.FindAsync(sceneId);
        if (target == null) return (false, "Không tìm thấy phân cảnh.", new List<Scene>());

        var scenes = await _db.Scenes
            .Where(s => s.ProjectId == target.ProjectId)
            .OrderBy(s => s.OrderIndex)
            .ThenBy(s => s.Id)
            .ToListAsync();

        int idx = scenes.FindIndex(s => s.Id == sceneId);
        int swapIdx = string.Equals(direction, "up", StringComparison.OrdinalIgnoreCase) ? idx - 1 : idx + 1;
        if (idx < 0 || swapIdx < 0 || swapIdx >= scenes.Count)
        {
            return (true, null, scenes);
        }

        (scenes[idx], scenes[swapIdx]) = (scenes[swapIdx], scenes[idx]);
        for (int i = 0; i < scenes.Count; i++) scenes[i].OrderIndex = i + 1;
        await _db.SaveChangesAsync();
        return (true, null, scenes);
    }

    public async Task<(bool Success, string? Error, Scene? Scene, List<Scene> Scenes)> DuplicateSceneAsync(int sceneId)
    {
        var source = await _db.Scenes.FindAsync(sceneId);
        if (source == null) return (false, "Không tìm thấy phân cảnh để nhân bản.", null, new List<Scene>());

        var scenes = await _db.Scenes
            .Where(s => s.ProjectId == source.ProjectId)
            .OrderBy(s => s.OrderIndex)
            .ThenBy(s => s.Id)
            .ToListAsync();

        var clone = new Scene
        {
            ProjectId = source.ProjectId,
            OrderIndex = source.OrderIndex + 1,
            Title = $"{source.Title} (Bản sao)",
            Content = source.Content,
            ImagePrompt = source.ImagePrompt,
            ImageUrl = source.ImageUrl,
            AudioUrl = source.AudioUrl,
            DurationSeconds = source.DurationSeconds,
            AnimationEffect = source.AnimationEffect,
            FilterPreset = source.FilterPreset,
            SubtitlePosition = source.SubtitlePosition,
            SceneType = source.SceneType,
            AvatarPosition = source.AvatarPosition,
            Speaker = source.Speaker
        };

        int insertAt = Math.Min(scenes.Count, scenes.FindIndex(s => s.Id == sceneId) + 1);
        scenes.Insert(insertAt, clone);
        _db.Scenes.Add(clone);
        for (int i = 0; i < scenes.Count; i++) scenes[i].OrderIndex = i + 1;

        await _db.SaveChangesAsync();
        return (true, null, clone, scenes);
    }

    public async Task<(bool Success, string? Error, string HtmlComposition)> GenerateCompositionAsync(int projectId)
    {
        var project = await _db.Projects.Include(p => p.Scenes.OrderBy(s => s.OrderIndex)).FirstOrDefaultAsync(p => p.Id == projectId);
        if (project == null) return (false, "Không tìm thấy dự án.", string.Empty);

        var compositionHtml = await _autoVideo.BuildHyperFramesCompositionAsync(project, project.Scenes);
        return (true, null, compositionHtml);
    }

    private static string ExtractJsonArray(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // Tìm mảng JSON bắt đầu bằng [ và kết thúc bằng ]
        var match = Regex.Match(text, @"\[\s*\{[\s\S]*\}\s*\]");
        if (match.Success)
        {
            return match.Value;
        }

        // Tìm trong khối markdown ```json ... ```
        var codeBlockMatch = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)\s*```");
        if (codeBlockMatch.Success)
        {
            var content = codeBlockMatch.Groups[1].Value.Trim();
            if (content.StartsWith('[') && content.EndsWith(']'))
            {
                return content;
            }
        }

        return string.Empty;
    }

    private static string GetFallbackScriptJson(string topic)
    {
        return $@"
[
  {{
    ""orderIndex"": 1,
    ""title"": ""Mở đầu: {topic}"",
    ""content"": ""Chào mừng các bạn đến với bản tin chuyên sâu về {topic}. Hôm nay chúng ta sẽ cùng khám phá những điểm cốt lõi nhất."",
    ""imagePrompt"": ""cinematic presentation cover, {topic}, futuristic technological glow, 8k render, professional visual design"",
    ""durationSeconds"": 5.0
  }},
  {{
    ""orderIndex"": 2,
    ""title"": ""Phân tích chi tiết"",
    ""content"": ""Với kiến trúc tối ưu và công nghệ hiện đại, hệ thống mang lại hiệu năng vượt trội và bảo mật client-side tối đa."",
    ""imagePrompt"": ""modern dashboard visualization, high tech glowing nodes, detailed digital art, cybernetic dark theme"",
    ""durationSeconds"": 5.5
  }},
  {{
    ""orderIndex"": 3,
    ""title"": ""Tổng kết và Định hướng"",
    ""content"": ""Đó là toàn bộ những điểm nổi bật. Hãy cùng bắt tay trải nghiệm ngay hôm nay trên COW.AI Studio."",
    ""imagePrompt"": ""inspiring futuristic workspace with multiple screens, elegant neon teal accents, clean aesthetic"",
    ""durationSeconds"": 4.5
  }}
]";
    }

    public async Task<(bool Success, string? Error, string PlanMarkdown)> GeneratePlanMarkdownAsync(
        int projectId, 
        string sourceContent, 
        string videoType, 
        string voice, 
        List<string> avatarUrls,
        string? sequentialSubMode = "auto",
        string? durationPreset = "auto")
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null) return (false, "Không tìm thấy dự án.", string.Empty);

        var effectiveSource = string.IsNullOrWhiteSpace(sourceContent)
            ? (project.SourceMarkdown ?? project.SourceContent ?? string.Empty)
            : sourceContent;

        var normalizedMd = _estimator.NormalizeSourceMarkdown(effectiveSource, project.SourceFileName ?? project.Title);
        project.VideoType = string.IsNullOrWhiteSpace(videoType) ? "comparison" : videoType.Trim();
        project.SequentialSubMode = string.IsNullOrWhiteSpace(sequentialSubMode) ? "auto" : sequentialSubMode.Trim();
        project.DurationPreset = string.IsNullOrWhiteSpace(durationPreset) ? "auto" : durationPreset.Trim();
        if (!string.IsNullOrWhiteSpace(voice)) project.Voice = voice.Trim();
        project.AvatarsJson = JsonSerializer.Serialize(avatarUrls);
        project.SourceContent = normalizedMd;
        project.SourceMarkdown = normalizedMd;

        var est = _estimator.Estimate(normalizedMd, project.VideoType, project.SequentialSubMode, project.DurationPreset, project.AspectRatio);
        project.EstimatedDurationSeconds = est.TargetDurationSeconds;
        project.SkillKey = AutoVideoService.ResolveSkillKey(project.VideoType, est.RecommendedSequentialSubMode, project.AspectRatio, est.WordCount);

        var markdown = await _autoVideo.GenerateMarkdownPlanAsync(
            normalizedMd,
            project.VideoType,
            project.Voice,
            avatarUrls,
            default,
            project.SequentialSubMode,
            project.DurationPreset,
            project.AspectRatio,
            project.Title);

        project.PlanMarkdown = markdown;
        project.Status = "Reviewing";
        project.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return (true, null, markdown);
    }

    public async Task<(bool Success, string? Error, List<Scene> Scenes)> ApplyPlanAndProduceAsync(
        int projectId, 
        string planMarkdown, 
        bool autoProduce = false)
    {
        var project = await _db.Projects.Include(p => p.Scenes).FirstOrDefaultAsync(p => p.Id == projectId);
        if (project == null) return (false, "Không tìm thấy dự án.", new List<Scene>());

        project.PlanMarkdown = planMarkdown;
        var parsedScenes = await _autoVideo.ParsePlanToScenesAsync(projectId, planMarkdown);
        if (parsedScenes.Count == 0)
        {
            return (false, "Không thể phân tích kịch bản thành các phân cảnh hợp lệ.", new List<Scene>());
        }

        _db.Scenes.RemoveRange(project.Scenes);
        project.Scenes = parsedScenes;
        project.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        if (autoProduce)
        {
            await AutoProduceProjectAsync(projectId);
        }

        return (true, null, parsedScenes);
    }

    public async Task<(bool Success, string Message, Project? Project)> ProcessChatEditAsync(
        int projectId, 
        string instruction)
    {
        var result = await _autoVideo.ProcessChatEditAsync(projectId, instruction);
        return (result.Success, result.Message, result.UpdatedProject);
    }
}
