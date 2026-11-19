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

    public async Task<Project> CreateProjectAsync(int? userId, string title, string? description, string? voice)
    {
        var project = new Project
        {
            UserId = userId,
            Title = string.IsNullOrWhiteSpace(title) ? "Dự án Studio mới" : title.Trim(),
            Description = description?.Trim(),
            Voice = string.IsNullOrWhiteSpace(voice) ? "Hải Đăng" : voice.Trim(),
            Status = "Empty",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Đã khởi tạo dự án Studio #{Id}: {Title}", project.Id, project.Title);
        return project;
    }

    public async Task<Project?> GetProjectAsync(int projectId)
    {
        return await _db.Projects
            .Include(p => p.Scenes.OrderBy(s => s.OrderIndex))
            .FirstOrDefaultAsync(p => p.Id == projectId);
    }

    public async Task<List<Project>> GetUserProjectsAsync(int? userId)
    {
        if (userId.HasValue)
        {
            return await _db.Projects
                .Where(p => p.UserId == userId.Value)
                .OrderByDescending(p => p.UpdatedAt)
                .ToListAsync();
        }

        // Với khách chưa đăng nhập, trả về các dự án không có userId (Demo)
        return await _db.Projects
            .Where(p => p.UserId == null)
            .OrderByDescending(p => p.UpdatedAt)
            .Take(10)
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
        var fileName = $"scene_{scene.Id}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.mp3";
        var audioDir = Path.Combine(_env.WebRootPath, "media", "studio", "audio");
        if (!Directory.Exists(audioDir)) Directory.CreateDirectory(audioDir);
        var filePath = Path.Combine(audioDir, fileName);

        try
        {
            bool isVieNeuVoice = voice.Equals("Hải Đăng", StringComparison.OrdinalIgnoreCase) ||
                                 voice.Equals("Lan Nhi", StringComparison.OrdinalIgnoreCase) ||
                                 voice.Equals("Binh", StringComparison.OrdinalIgnoreCase);

            string audioUrl = string.Empty;
            double duration = 4.0;

            if (isVieNeuVoice)
            {
                var (success, error, genUrl, dur) = await _vieNeuTts.GenerateAudioAsync(scene.Content, voice, "studio/audio");
                if (success && !string.IsNullOrWhiteSpace(genUrl))
                {
                    audioUrl = genUrl;
                    duration = dur > 0 ? dur : 4.0;
                }
                else
                {
                    _logger.LogWarning("VieNeu-TTS không phản hồi ({Error}). Chuyển sang Edge-TTS fallback...", error);
                    var fallbackVoice = (voice.Contains("Hải Đăng", StringComparison.OrdinalIgnoreCase) || voice.Contains("Binh", StringComparison.OrdinalIgnoreCase))
                        ? "vi-VN-NamMinhNeural"
                        : "vi-VN-HoaiMyNeural";
                    using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await _tts.StreamToResponseAsync(scene.Content, fileStream, fallbackVoice);
                    await fileStream.FlushAsync();
                    audioUrl = $"/media/studio/audio/{fileName}";
                    duration = await MeasureAudioDurationWithFfprobeAsync(filePath, scene.Content);
                }
            }
            else
            {
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await _tts.StreamToResponseAsync(scene.Content, fileStream, voice);
                await fileStream.FlushAsync();
                audioUrl = $"/media/studio/audio/{fileName}";
                duration = await MeasureAudioDurationWithFfprobeAsync(filePath, scene.Content);
            }

            scene.AudioUrl = audioUrl;
            scene.DurationSeconds = Math.Max(2.5, Math.Round(duration, 1));
            await _db.SaveChangesAsync();

            _logger.LogInformation("Sinh âm thanh thành công cho Scene #{Id}: {Url} ({Duration}s)", scene.Id, audioUrl, scene.DurationSeconds);
            return (true, null, audioUrl, scene.DurationSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi tạo audio cho Scene #{Id}", sceneId);
            return (false, "Lỗi sinh giọng đọc: " + ex.Message, null, 0);
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

        try
        {
            // Thử tạo bằng Local SDXL Turbo trước
            try
            {
                var (localUrl, _, _) = await _localImage.GenerateImageAsync(prompt, 768, 432);
                if (!string.IsNullOrWhiteSpace(localUrl))
                {
                    finalImageUrl = localUrl;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Local SDXL không khả dụng, chuyển sang Pollinations");
            }

            if (string.IsNullOrWhiteSpace(finalImageUrl))
            {
                // Fallback tạo ảnh tức thì qua Pollinations AI
                finalImageUrl = _pollinations.GetImageUrl(prompt, 768, 432, "flux");
            }

            scene.ImageUrl = finalImageUrl;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Sinh hình ảnh thành công cho Scene #{Id}: {Url}", scene.Id, finalImageUrl);
            return (true, null, finalImageUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi sinh ảnh cho Scene #{Id}", sceneId);
            // Fallback an toàn qua Pollinations URL
            var fallbackUrl = _pollinations.GetImageUrl(prompt, 768, 432, "flux");
            scene.ImageUrl = fallbackUrl;
            await _db.SaveChangesAsync();
            return (true, null, fallbackUrl);
        }
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

    public async Task<(bool Success, string? Error, string HtmlComposition)> GenerateCompositionAsync(int projectId)
    {
        var project = await _db.Projects.Include(p => p.Scenes.OrderBy(s => s.OrderIndex)).FirstOrDefaultAsync(p => p.Id == projectId);
        if (project == null) return (false, "Không tìm thấy dự án.", string.Empty);

        var scenesJson = JsonSerializer.Serialize(project.Scenes.Select(s => new
        {
            id = s.Id,
            orderIndex = s.OrderIndex,
            title = s.Title,
            content = s.Content,
            imageUrl = s.ImageUrl ?? "/images/cyber-cow.png",
            audioUrl = s.AudioUrl ?? "",
            duration = s.DurationSeconds,
            animation = s.AnimationEffect,
            filter = s.FilterPreset,
            subtitlePos = s.SubtitlePosition
        }));

        var totalDuration = project.Scenes.Sum(s => s.DurationSeconds);

        var compositionHtml = $@"<!DOCTYPE html>
<html lang=""vi"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{System.Net.WebUtility.HtmlEncode(project.Title)} - HyperFrames Composition</title>
    <script src=""https://cdn.jsdelivr.net/npm/gsap@3.12.5/dist/gsap.min.js""></script>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{ background: #0b0f19; color: #f1f5f9; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; display: flex; align-items: center; justify-content: center; min-height: 100vh; overflow: hidden; }}
        #hyperframes-stage {{ width: 100vw; max-width: 1280px; aspect-ratio: 16/9; position: relative; overflow: hidden; background: #000; box-shadow: 0 20px 50px rgba(0,0,0,0.8); border-radius: 8px; }}
        .hf-scene {{ position: absolute; inset: 0; opacity: 0; display: flex; flex-direction: column; align-items: center; justify-content: flex-end; padding: 40px; transition: opacity 0.5s ease; }}
        .hf-scene.active {{ opacity: 1; }}
        .hf-bg {{ position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; z-index: 1; transform-origin: center center; }}
        .hf-overlay {{ position: absolute; inset: 0; background: linear-gradient(180deg, rgba(0,0,0,0.2) 0%, rgba(0,0,0,0.8) 100%); z-index: 2; pointer-events: none; }}
        .hf-caption {{ position: relative; z-index: 3; max-width: 85%; background: rgba(15, 23, 42, 0.85); backdrop-filter: blur(8px); padding: 16px 24px; border-radius: 8px; border: 1px solid rgba(255,255,255,0.1); font-size: 20px; line-height: 1.5; text-align: center; font-weight: 500; text-shadow: 0 2px 4px rgba(0,0,0,0.8); }}
        .hf-caption.top {{ margin-bottom: auto; margin-top: 40px; }}
        .hf-caption.middle {{ margin: auto 0; }}
        .hf-caption.bottom {{ margin-top: auto; margin-bottom: 20px; }}
        /* Filter Presets */
        .filter-cinematic {{ filter: contrast(1.15) brightness(0.95) saturate(1.25); }}
        .filter-warm {{ filter: sepia(0.25) saturate(1.3) brightness(1.02); }}
        .filter-cool {{ filter: hue-rotate(185deg) saturate(1.1) brightness(0.95); }}
        .filter-noir {{ filter: grayscale(1) contrast(1.4) brightness(0.9); }}
        .filter-none {{ filter: none; }}
        /* Transport Controls */
        #hf-controls {{ position: absolute; bottom: 10px; left: 10px; right: 10px; z-index: 10; display: flex; align-items: center; gap: 12px; background: rgba(11,15,25,0.85); backdrop-filter: blur(8px); padding: 8px 16px; border-radius: 6px; border: 1px solid rgba(255,255,255,0.1); }}
        #hf-play-btn {{ background: #38bdf8; border: none; color: #0b0f19; font-weight: 700; padding: 6px 14px; border-radius: 4px; cursor: pointer; }}
        #hf-progress {{ flex: 1; height: 6px; background: rgba(255,255,255,0.2); border-radius: 3px; cursor: pointer; position: relative; }}
        #hf-bar {{ height: 100%; width: 0%; background: #38bdf8; border-radius: 3px; }}
        #hf-time {{ font-size: 12px; font-variant-numeric: tabular-nums; opacity: 0.8; }}
    </style>
</head>
<body>
    <div id=""hyperframes-stage"">
        <div id=""scenes-container""></div>
        <div id=""hf-controls"">
            <button id=""hf-play-btn"">Phát</button>
            <div id=""hf-progress""><div id=""hf-bar""></div></div>
            <div id=""hf-time"">00:00 / 00:00</div>
        </div>
    </div>
    <script>
        const scenes = {scenesJson};
        const totalDuration = {totalDuration.ToString(System.Globalization.CultureInfo.InvariantCulture)};
        const container = document.getElementById('scenes-container');
        const playBtn = document.getElementById('hf-play-btn');
        const progressBar = document.getElementById('hf-bar');
        const progressTrack = document.getElementById('hf-progress');
        const timeDisplay = document.getElementById('hf-time');

        let currentIndex = 0;
        let isPlaying = false;
        let currentAudio = null;
        let sceneTimer = null;

        scenes.forEach((s, idx) => {{
            const div = document.createElement('div');
            div.className = 'hf-scene' + (idx === 0 ? ' active' : '');
            div.id = 'hf-scene-' + idx;
            
            const img = document.createElement('img');
            img.className = 'hf-bg filter-' + (s.filter || 'none');
            img.src = s.imageUrl;
            
            const overlay = document.createElement('div');
            overlay.className = 'hf-overlay';
            
            const cap = document.createElement('div');
            cap.className = 'hf-caption ' + (s.subtitlePos || 'bottom');
            cap.textContent = s.content;
            
            div.appendChild(img);
            div.appendChild(overlay);
            div.appendChild(cap);
            container.appendChild(div);
        }});

        function updateTimeText(current, total) {{
            const fmt = (sec) => {{
                const m = Math.floor(sec / 60).toString().padStart(2, '0');
                const s = Math.floor(sec % 60).toString().padStart(2, '0');
                return `${{m}}:${{s}}`;
            }};
            timeDisplay.textContent = `${{fmt(current)}} / ${{fmt(total)}}`;
        }}

        function animateScene(idx) {{
            const el = document.getElementById('hf-scene-' + idx);
            if (!el) return;
            const img = el.querySelector('.hf-bg');
            const cap = el.querySelector('.hf-caption');
            const s = scenes[idx];

            gsap.killTweensOf([img, cap]);
            gsap.fromTo(cap, {{ opacity: 0, y: 15 }}, {{ opacity: 1, y: 0, duration: 0.6, ease: 'power2.out' }});

            const dur = s.duration || 4.0;
            switch (s.animation) {{
                case 'zoom-in':
                    gsap.fromTo(img, {{ scale: 1 }}, {{ scale: 1.15, duration: dur, ease: 'none' }});
                    break;
                case 'pan-left':
                    gsap.fromTo(img, {{ x: '5%', scale: 1.08 }}, {{ x: '-5%', duration: dur, ease: 'none' }});
                    break;
                case 'fade-scale':
                    gsap.fromTo(img, {{ scale: 1.1, opacity: 0.85 }}, {{ scale: 1, opacity: 1, duration: dur, ease: 'power1.out' }});
                    break;
                case 'ken-burns':
                default:
                    gsap.fromTo(img, {{ scale: 1, x: '-2%', y: '-2%' }}, {{ scale: 1.12, x: '2%', y: '2%', duration: dur, ease: 'sine.inOut' }});
                    break;
            }}
        }}

        function playScene(idx) {{
            if (idx >= scenes.length) {{
                isPlaying = false;
                playBtn.textContent = 'Phát lại';
                currentIndex = 0;
                return;
            }}

            currentIndex = idx;
            document.querySelectorAll('.hf-scene').forEach((el, i) => {{
                el.classList.toggle('active', i === idx);
            }});

            const s = scenes[idx];
            animateScene(idx);

            if (currentAudio) {{
                currentAudio.pause();
                currentAudio = null;
            }}

            if (s.audioUrl) {{
                currentAudio = new Audio(s.audioUrl);
                currentAudio.play().catch(() => {{}});
            }}

            const durMs = (s.duration || 4.0) * 1000;
            clearTimeout(sceneTimer);
            sceneTimer = setTimeout(() => {{
                if (isPlaying) playScene(idx + 1);
            }}, durMs);
        }}

        playBtn.addEventListener('click', () => {{
            if (isPlaying) {{
                isPlaying = false;
                playBtn.textContent = 'Phát';
                if (currentAudio) currentAudio.pause();
                clearTimeout(sceneTimer);
            }} else {{
                isPlaying = true;
                playBtn.textContent = 'Tạm dừng';
                playScene(currentIndex);
            }}
        }});
    </script>
</body>
</html>";

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
        List<string> avatarUrls)
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null) return (false, "Không tìm thấy dự án.", string.Empty);

        project.VideoType = string.IsNullOrWhiteSpace(videoType) ? "comparison" : videoType.Trim();
        if (!string.IsNullOrWhiteSpace(voice)) project.Voice = voice.Trim();
        project.AvatarsJson = JsonSerializer.Serialize(avatarUrls);
        project.SourceContent = sourceContent;

        var markdown = await _autoVideo.GenerateMarkdownPlanAsync(sourceContent, project.VideoType, project.Voice, avatarUrls);
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
