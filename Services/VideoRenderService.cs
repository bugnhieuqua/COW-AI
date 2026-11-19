using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using COW_AI.Data;
using COW_AI.Models;
using Microsoft.EntityFrameworkCore;

namespace COW_AI.Services;

public class VideoRenderService : IVideoRenderService
{
    private static readonly SemaphoreSlim _renderLock = new(1, 1);
    private static bool? _nvencSupported;

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly IAutoVideoService _autoVideo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<VideoRenderService> _logger;
    private readonly string _ffmpegPath;
    private readonly string _bgmPath;
    private readonly string _whooshPath;
    private readonly string _hitPath;

    public VideoRenderService(
        AppDbContext db,
        IWebHostEnvironment env,
        IConfiguration config,
        IAutoVideoService autoVideo,
        IHttpClientFactory httpClientFactory,
        ILogger<VideoRenderService> logger)
    {
        _db = db;
        _env = env;
        _config = config;
        _autoVideo = autoVideo;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _ffmpegPath = ResolveBinary(_config["AutoVideo:FfmpegPath"], "ffmpeg.exe", "ffmpeg");
        _bgmPath = ResolveStudioAsset("audio", "bgm_48k.wav", _config["AutoVideo:BgmPath"], @"G:\Video\auto-compare-video\audio\bgm_48k.wav");
        _whooshPath = ResolveStudioAsset("audio", "whoosh_48k.wav", null, @"G:\Video\auto-compare-video\audio\whoosh_48k.wav");
        _hitPath = ResolveStudioAsset("audio", "hit_48k.wav", null, @"G:\Video\auto-compare-video\audio\hit_48k.wav");
    }

    private string ResolveBinary(string? configured, string exeName, string fallbackCommand)
    {
        var candidates = new[]
        {
            configured,
            Path.Combine(_env.ContentRootPath, "Tools", "ffmpeg", exeName),
            Path.Combine(@"G:\Video\ffmpeg\bin", exeName)
        };
        foreach (var path in candidates)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
        }
        return fallbackCommand;
    }

    private string ResolveStudioAsset(string subDir, string fileName, string? configuredPath, string legacyPath)
    {
        var candidates = new[]
        {
            Path.Combine(_env.WebRootPath, "assets", "studio", subDir, fileName),
            configuredPath,
            legacyPath
        };
        foreach (var path in candidates)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
        }
        return string.Empty;
    }

    public bool IsFfmpegAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            proc.WaitForExit(3000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<VideoRenderResult> RenderProjectVideoAsync(
        int projectId,
        string aspectRatio = "16:9",
        bool includeBgm = true,
        string renderMode = "fast",
        CancellationToken ct = default)
    {
        var project = await _db.Projects.Include(p => p.Scenes.OrderBy(s => s.OrderIndex))
                                        .FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null)
        {
            return new VideoRenderResult(false, "Không tìm thấy dự án.", null, null, 0);
        }

        if (project.Scenes.Count == 0)
        {
            return new VideoRenderResult(false, "Dự án chưa có phân cảnh nào để ghép video.", null, null, 0);
        }

        var rendersDir = Path.Combine(_env.WebRootPath, "media", "studio", "renders");
        Directory.CreateDirectory(rendersDir);
        PruneOldRenders(rendersDir, TimeSpan.FromDays(7));

        var tempDir = Path.Combine(rendersDir, $"temp_{projectId}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        await _renderLock.WaitAsync(ct);
        try
        {
            bool isPortrait = string.Equals(aspectRatio, "9:16", StringComparison.OrdinalIgnoreCase);
            int width = isPortrait ? 1080 : 1920;
            int height = isPortrait ? 1920 : 1080;
            const int fps = 30;
            const double fadeDur = 0.35;

            var scenes = project.Scenes.OrderBy(s => s.OrderIndex).ToList();
            var prepared = new List<PreparedScene>();
            double cursor = 0;

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(15);

            for (int i = 0; i < scenes.Count; i++)
            {
                var scene = scenes[i];
                double sceneDur = Math.Clamp(scene.DurationSeconds > 0 ? scene.DurationSeconds : 4.0, 1.5, 60.0);

                // 1. Resolve Image
                var localImagePath = Path.Combine(tempDir, $"img_{i:D3}.png");
                bool imageAcquired = await AcquireSceneImageAsync(scene, localImagePath, httpClient, ct);
                if (!imageAcquired)
                {
                    var defaultLogo = Path.Combine(_env.WebRootPath, "images", "logo.png");
                    if (File.Exists(defaultLogo)) File.Copy(defaultLogo, localImagePath, true);
                    else await GeneratePlaceholderImageAsync(localImagePath, width, height, scene.Title);
                }

                // 2. Resolve Audio (chuẩn hóa sang WAV 48kHz stereo đúng độ dài cảnh)
                var normalizedAudio = Path.Combine(tempDir, $"vo_{i:D3}.wav");
                string? sourceAudio = ResolveLocalWebPath(scene.AudioUrl);
                if (!string.IsNullOrWhiteSpace(sourceAudio) && File.Exists(sourceAudio))
                {
                    var padArgs = $"-y -i \"{sourceAudio}\" -af \"aresample=48000,apad=whole_dur={F(sceneDur)}\" -t {F(sceneDur)} -ac 2 -c:a pcm_s16le \"{normalizedAudio}\"";
                    var (aCode, _) = await RunProcessAsync(_ffmpegPath, padArgs, ct: ct);
                    if (aCode != 0 || !File.Exists(normalizedAudio))
                    {
                        await GenerateSilenceWavAsync(normalizedAudio, sceneDur);
                    }
                }
                else
                {
                    await GenerateSilenceWavAsync(normalizedAudio, sceneDur);
                }

                prepared.Add(new PreparedScene(scene, localImagePath, normalizedAudio, cursor, sceneDur));
                cursor += sceneDur;
            }

            double totalDuration = cursor;
            double videoOverlap = prepared.Count > 1 ? fadeDur * (prepared.Count - 1) : 0;
            double finalVideoDuration = Math.Max(1.0, totalDuration - videoOverlap);

            // 3. Master Audio Mixing 48kHz (Voice + SFX whoosh/hit + BGM Dynamic Ducking)
            var masterAudioWav = Path.Combine(tempDir, "master_audio.wav");
            await BuildMasterAudioAsync(prepared, masterAudioWav, includeBgm, totalDuration, ct);
            if (!File.Exists(masterAudioWav))
            {
                await GenerateSilenceWavAsync(masterAudioWav, totalDuration);
            }

            var outFileName = $"video_proj_{projectId}_{(isPortrait ? "9x16" : "16x9")}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.mp4";
            var outFilePath = Path.Combine(rendersDir, outFileName);

            // 4. Optional HyperFrames High-End Render Mode (fallback tự động về FFmpeg Nâng cấp)
            bool renderedByHyperFrames = false;
            if (string.Equals(renderMode, "hyperframes", StringComparison.OrdinalIgnoreCase))
            {
                renderedByHyperFrames = await TryRenderHyperFramesAsync(project, scenes, prepared, masterAudioWav, outFilePath, width, height, tempDir, ct);
            }

            // 5. FFmpeg Enhanced Render Mode (Ken Burns + Filter Preset + xfade + ASS Subtitle + NVENC/libx264)
            if (!renderedByHyperFrames)
            {
                var (ok, err) = await RenderEnhancedFfmpegAsync(prepared, masterAudioWav, outFilePath, width, height, fps, fadeDur, tempDir, ct);
                if (!ok || !File.Exists(outFilePath))
                {
                    return new VideoRenderResult(false, $"FFmpeg render lỗi: {err}", null, null, 0);
                }
            }

            var videoUrl = $"/media/studio/renders/{outFileName}";
            _logger.LogInformation(
                "Xuất video thành công cho dự án #{Id} ({Mode}, {Aspect}): {Url} ({Dur:0.0}s)",
                projectId, renderedByHyperFrames ? "hyperframes" : "ffmpeg-enhanced", aspectRatio, videoUrl, finalVideoDuration);

            return new VideoRenderResult(true, null, videoUrl, outFilePath, finalVideoDuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi trong quy trình ghép video cho dự án #{Id}", projectId);
            return new VideoRenderResult(false, "Lỗi ghép video: " + ex.Message, null, null, 0);
        }
        finally
        {
            _renderLock.Release();
            try
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
            catch { }
        }
    }

    private async Task<bool> AcquireSceneImageAsync(Scene scene, string localImagePath, HttpClient httpClient, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(scene.ImageUrl)) return false;

        var localDisk = ResolveLocalWebPath(scene.ImageUrl);
        if (!string.IsNullOrWhiteSpace(localDisk) && File.Exists(localDisk))
        {
            File.Copy(localDisk, localImagePath, true);
            return true;
        }

        if (Uri.TryCreate(scene.ImageUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            await IsPublicHostAsync(uri.Host))
        {
            try
            {
                var bytes = await httpClient.GetByteArrayAsync(uri, ct);
                if (bytes.Length > 1000 && bytes.Length <= 25 * 1024 * 1024)
                {
                    await File.WriteAllBytesAsync(localImagePath, bytes, ct);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không tải được ảnh cảnh {Order}: {Url}", scene.OrderIndex, scene.ImageUrl);
            }
        }

        return false;
    }

    private string? ResolveLocalWebPath(string? webUrl)
    {
        if (string.IsNullOrWhiteSpace(webUrl)) return null;
        if (!webUrl.StartsWith('/') && !webUrl.StartsWith('~')) return null;
        var rel = webUrl.TrimStart('~', '/').Replace('/', Path.DirectorySeparatorChar);
        var rootFull = Path.GetFullPath(_env.WebRootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(_env.WebRootPath, rel));
        return fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
    }

    private static async Task<bool> IsPublicHostAsync(string host)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            if (addresses.Length == 0) return false;
            foreach (var ip in addresses)
            {
                if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return false;
                var b = ip.GetAddressBytes();
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    (b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
                     (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254)))
                {
                    return false;
                }
            }
            return true;
        }
        catch { return false; }
    }

    private static void PruneOldRenders(string rendersDir, TimeSpan maxAge)
    {
        try
        {
            var cutoff = DateTime.UtcNow - maxAge;
            foreach (var file in Directory.EnumerateFiles(rendersDir, "*.mp4"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Trộn Master Audio 48kHz theo công thức auto-compare-video:
    /// Voiceover (1.05x) + SFX whoosh (0.22) + SFX hit (0.18) + BGM Dynamic Sidechain Ducking + Fade In/Out.
    /// </summary>
    private async Task BuildMasterAudioAsync(
        List<PreparedScene> scenes,
        string outputWav,
        bool includeBgm,
        double totalDuration,
        CancellationToken ct)
    {
        var tempDir = Path.GetDirectoryName(outputWav)!;
        var voiceConcatWav = Path.Combine(tempDir, "master_voiceover.wav");
        await ConcatAudioFilesAsync(scenes.Select(s => s.AudioPath).ToList(), voiceConcatWav, ct);
        if (!File.Exists(voiceConcatWav)) return;

        bool hasBgm = includeBgm && !string.IsNullOrWhiteSpace(_bgmPath) && File.Exists(_bgmPath);
        bool hasWhoosh = !string.IsNullOrWhiteSpace(_whooshPath) && File.Exists(_whooshPath);
        bool hasHit = !string.IsNullOrWhiteSpace(_hitPath) && File.Exists(_hitPath);

        if (!hasBgm && !hasWhoosh && !hasHit)
        {
            File.Copy(voiceConcatWav, outputWav, true);
            return;
        }

        var inputs = new StringBuilder($"-y -i \"{voiceConcatWav}\" ");
        var filter = new StringBuilder();
        var mixLabels = new List<string>();
        int nextInput = 1;

        int bgmIdx = -1;
        if (hasBgm)
        {
            bgmIdx = nextInput++;
            inputs.Append($"-stream_loop -1 -i \"{_bgmPath}\" ");
        }

        int whooshIdx = -1;
        if (hasWhoosh)
        {
            whooshIdx = nextInput++;
            inputs.Append($"-i \"{_whooshPath}\" ");
        }

        int hitIdx = -1;
        if (hasHit)
        {
            hitIdx = nextInput++;
            inputs.Append($"-i \"{_hitPath}\" ");
        }

        // Tách luồng giọng nói: 1 luồng mix chính, 1 luồng điều khiển sidechain ducking cho BGM
        if (hasBgm)
        {
            filter.Append("[0:a]volume=1.08,asplit=2[vo_main][vo_sc];");
            var fadeOutStart = Math.Max(0, totalDuration - 2.5);
            filter.Append($"[{bgmIdx}:a]atrim=0:{F(totalDuration)},asetpts=PTS-STARTPTS,volume=0.22,afade=t=in:st=0:d=0.5,afade=t=out:st={F(fadeOutStart)}:d=2.5[bgm_raw];");
            filter.Append("[bgm_raw][vo_sc]sidechaincompress=threshold=0.03:ratio=4:attack=120:release=350[bgm_ducked];");
            mixLabels.Add("[vo_main]");
            mixLabels.Add("[bgm_ducked]");
        }
        else
        {
            filter.Append("[0:a]volume=1.08[vo_main];");
            mixLabels.Add("[vo_main]");
        }

        // Chèn SFX chuyển cảnh (tối đa 12 cảnh để filter graph nhẹ và nhanh)
        var sfxScenes = scenes.Take(12).ToList();
        if (hasWhoosh && sfxScenes.Count > 0)
        {
            filter.Append($"[{whooshIdx}:a]asplit={sfxScenes.Count}");
            for (int i = 0; i < sfxScenes.Count; i++) filter.Append($"[w_src_{i}]");
            filter.Append(';');

            for (int i = 0; i < sfxScenes.Count; i++)
            {
                int delayMs = Math.Max(0, (int)Math.Round(sfxScenes[i].StartSeconds * 1000));
                filter.Append($"[w_src_{i}]volume=0.20,adelay={delayMs}|{delayMs}[w_{i}];");
                mixLabels.Add($"[w_{i}]");
            }
        }

        if (hasHit && sfxScenes.Count > 0)
        {
            filter.Append($"[{hitIdx}:a]asplit={sfxScenes.Count}");
            for (int i = 0; i < sfxScenes.Count; i++) filter.Append($"[h_src_{i}]");
            filter.Append(';');

            for (int i = 0; i < sfxScenes.Count; i++)
            {
                int delayMs = Math.Max(0, (int)Math.Round((sfxScenes[i].StartSeconds + 0.15) * 1000));
                filter.Append($"[h_src_{i}]volume=0.16,adelay={delayMs}|{delayMs}[h_{i}];");
                mixLabels.Add($"[h_{i}]");
            }
        }

        filter.Append(string.Concat(mixLabels));
        filter.Append($"amix=inputs={mixLabels.Count}:duration=first:normalize=0,alimiter=limit=0.95[aout]");

        var args = $"{inputs}-filter_complex \"{filter}\" -map \"[aout]\" -t {F(totalDuration)} -ar 48000 -ac 2 -c:a pcm_s16le \"{outputWav}\"";
        var (code, err) = await RunProcessAsync(_ffmpegPath, args, ct: ct);
        if (code != 0 || !File.Exists(outputWav))
        {
            _logger.LogWarning("Sidechain/SFX master mix fallback sang trộn cơ bản: {Err}", err);
            if (hasBgm)
            {
                var simpleArgs = $"-y -i \"{voiceConcatWav}\" -stream_loop -1 -i \"{_bgmPath}\" " +
                                 $"-filter_complex \"[0:a]volume=1.05[a0];[1:a]volume=0.14[a1];[a0][a1]amix=inputs=2:duration=first:normalize=0[aout]\" " +
                                 $"-map \"[aout]\" -t {F(totalDuration)} -ar 48000 -ac 2 -c:a pcm_s16le \"{outputWav}\"";
                await RunProcessAsync(_ffmpegPath, simpleArgs, ct: ct);
            }
            if (!File.Exists(outputWav)) File.Copy(voiceConcatWav, outputWav, true);
        }
    }

    private async Task<(bool Ok, string? Error)> RenderEnhancedFfmpegAsync(
        List<PreparedScene> scenes,
        string masterAudioWav,
        string outFilePath,
        int width,
        int height,
        int fps,
        double fadeDur,
        string tempDir,
        CancellationToken ct)
    {
        var clipPaths = new List<string>();
        for (int i = 0; i < scenes.Count; i++)
        {
            var ps = scenes[i];
            var clipPath = Path.Combine(tempDir, $"clip_{i:D3}.mp4");
            int frames = Math.Max(fps, (int)Math.Ceiling(ps.DurationSeconds * fps));

            string zoomExpr = BuildKenBurnsFilter(ps.Scene.AnimationEffect, width, height, frames, fps);
            string colorFilter = BuildColorFilter(ps.Scene.FilterPreset);
            string vf = $"scale={width * 2}:{height * 2}:force_original_aspect_ratio=increase,crop={width * 2}:{height * 2},{zoomExpr}{colorFilter},format=yuv420p";

            var clipArgs = $"-y -loop 1 -i \"{ps.ImagePath}\" -vf \"{vf}\" -t {F(ps.DurationSeconds)} -r {fps} -c:v libx264 -preset ultrafast -crf 18 -an \"{clipPath}\"";
            var (cCode, cErr) = await RunProcessAsync(_ffmpegPath, clipArgs, ct: ct);
            if (cCode != 0 || !File.Exists(clipPath))
            {
                // Fallback tĩnh nếu zoompan lỗi trên ảnh lạ
                var fallbackArgs = $"-y -loop 1 -i \"{ps.ImagePath}\" -vf \"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,format=yuv420p\" -t {F(ps.DurationSeconds)} -r {fps} -c:v libx264 -preset ultrafast -crf 20 -an \"{clipPath}\"";
                var (fCode, fErr) = await RunProcessAsync(_ffmpegPath, fallbackArgs, ct: ct);
                if (fCode != 0 || !File.Exists(clipPath)) return (false, cErr + "\n" + fErr);
            }
            clipPaths.Add(clipPath);
        }

        // Tạo file phụ đề ASS tiếng Việt (Be Vietnam Pro)
        var assPath = Path.Combine(tempDir, "subtitles.ass");
        await WriteAssSubtitlesAsync(scenes, assPath, width, height, fadeDur, ct);
        var assEscaped = EscapeFfmpegFilterPath(assPath);

        // Dò NVENC (RTX 4060) để tăng tốc encode cuối
        string encoderArgs = await SupportsNvencAsync(ct)
            ? "-c:v h264_nvenc -preset p4 -cq 22"
            : "-c:v libx264 -preset fast -crf 20";

        var inputs = new StringBuilder("-y ");
        foreach (var cp in clipPaths) inputs.Append($"-i \"{cp}\" ");
        int audioInputIdx = clipPaths.Count;
        inputs.Append($"-i \"{masterAudioWav}\" ");

        var vfChain = new StringBuilder();
        string videoOutLabel;
        if (clipPaths.Count == 1)
        {
            vfChain.Append($"[0:v]ass='{assEscaped}',format=yuv420p[vout]");
            videoOutLabel = "[vout]";
        }
        else
        {
            double offset = scenes[0].DurationSeconds - fadeDur;
            vfChain.Append($"[0:v][1:v]xfade=transition=fade:duration={F(fadeDur)}:offset={F(Math.Max(0.1, offset))}[vx1];");
            for (int i = 2; i < clipPaths.Count; i++)
            {
                offset += scenes[i - 1].DurationSeconds - fadeDur;
                vfChain.Append($"[vx{i - 1}][{i}:v]xfade=transition=fade:duration={F(fadeDur)}:offset={F(Math.Max(0.1, offset))}[vx{i}];");
            }
            vfChain.Append($"[vx{clipPaths.Count - 1}]ass='{assEscaped}',format=yuv420p[vout]");
            videoOutLabel = "[vout]";
        }

        var finalArgs = $"{inputs}-filter_complex \"{vfChain}\" -map \"{videoOutLabel}\" -map {audioInputIdx}:a {encoderArgs} -c:a aac -b:a 192k -movflags +faststart -shortest \"{outFilePath}\"";
        var (exitCode, stdErr) = await RunProcessAsync(_ffmpegPath, finalArgs, ct: ct);

        // Nếu NVENC thất bại ở runtime (driver mismatch), tự động fallback sang libx264
        if (exitCode != 0 && encoderArgs.Contains("h264_nvenc"))
        {
            _nvencSupported = false;
            var cpuArgs = finalArgs.Replace(encoderArgs, "-c:v libx264 -preset fast -crf 20");
            (exitCode, stdErr) = await RunProcessAsync(_ffmpegPath, cpuArgs, ct: ct);
        }

        return (exitCode == 0 && File.Exists(outFilePath), stdErr);
    }

    private async Task<bool> TryRenderHyperFramesAsync(
        Project project,
        List<Scene> scenes,
        List<PreparedScene> prepared,
        string masterAudioWav,
        string outFilePath,
        int width,
        int height,
        string tempDir,
        CancellationToken ct)
    {
        try
        {
            var hfDir = Path.Combine(tempDir, "hf_project");
            Directory.CreateDirectory(hfDir);

            // Ghi composition HTML từ AutoVideoService + chèn audio master
            var html = await _autoVideo.BuildHyperFramesCompositionAsync(project, scenes, ct);
            html = html.Replace("max-width: 1080px;", $"width: {width}px; height: {height}px; max-width: none; max-height: none;");
            await File.WriteAllTextAsync(Path.Combine(hfDir, "index.html"), html, Encoding.UTF8, ct);
            File.Copy(masterAudioWav, Path.Combine(hfDir, "master_audio.wav"), true);

            var hfOut = Path.Combine(tempDir, "hf_video.mp4");
            var ffmpegDir = Path.GetDirectoryName(_ffmpegPath);
            var envVars = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(ffmpegDir) && Directory.Exists(ffmpegDir))
            {
                envVars["PATH"] = ffmpegDir + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? "");
            }

            var (code, err) = await RunProcessAsync(
                "cmd.exe",
                $"/c npx --yes hyperframes@0.9.2 render -o \"{hfOut}\"",
                workingDir: hfDir,
                extraEnv: envVars,
                timeoutMs: 120_000,
                ct: ct);

            if (code == 0 && File.Exists(hfOut))
            {
                // Mux video từ HyperFrames với master_audio.wav 48kHz thống nhất
                var muxArgs = $"-y -i \"{hfOut}\" -i \"{masterAudioWav}\" -map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -b:a 192k -movflags +faststart -shortest \"{outFilePath}\"";
                var (mCode, _) = await RunProcessAsync(_ffmpegPath, muxArgs, ct: ct);
                if (mCode == 0 && File.Exists(outFilePath)) return true;
            }
            _logger.LogWarning("HyperFrames render không hoàn tất (code {Code}), tự động fallback sang FFmpeg Nâng cấp: {Err}", code, err);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HyperFrames render gặp ngoại lệ, tự động fallback sang FFmpeg Nâng cấp.");
            return false;
        }
    }

    private static string BuildKenBurnsFilter(string? effect, int width, int height, int frames, int fps)
    {
        return (effect ?? "zoom-in").ToLowerInvariant() switch
        {
            "zoom-out" => $"zoompan=z='max(1.0,1.16-0.16*on/{frames})':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d={frames}:s={width}x{height}:fps={fps}",
            "pan-left" => $"zoompan=z='1.12':x='(iw-iw/zoom)*(1-on/{frames})':y='ih/2-(ih/zoom/2)':d={frames}:s={width}x{height}:fps={fps}",
            "pan-right" => $"zoompan=z='1.12':x='(iw-iw/zoom)*(on/{frames})':y='ih/2-(ih/zoom/2)':d={frames}:s={width}x{height}:fps={fps}",
            _ => $"zoompan=z='min(1.16,1.0+0.16*on/{frames})':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d={frames}:s={width}x{height}:fps={fps}"
        };
    }

    private static string BuildColorFilter(string? filterPreset)
    {
        return (filterPreset ?? "normal").ToLowerInvariant() switch
        {
            "noir" => ",hue=s=0,eq=contrast=1.18:brightness=-0.03",
            "warm" => ",colorbalance=rs=0.08:gs=0.03:bs=-0.06",
            "cool" => ",colorbalance=rs=-0.06:gs=0.01:bs=0.08",
            "cyberpunk" => ",eq=saturation=1.35:contrast=1.12",
            _ => string.Empty
        };
    }

    private static async Task WriteAssSubtitlesAsync(
        List<PreparedScene> scenes,
        string assPath,
        int width,
        int height,
        double fadeDur,
        CancellationToken ct)
    {
        int fontSize = width < height ? 46 : 42;
        int marginV = width < height ? 140 : 64;
        var sb = new StringBuilder();
        sb.AppendLine("[Script Info]");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine($"PlayResX: {width}");
        sb.AppendLine($"PlayResY: {height}");
        sb.AppendLine("WrapStyle: 1");
        sb.AppendLine();
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        sb.AppendLine($"Style: Bottom,Be Vietnam Pro,{fontSize},&H00FFFFFF,&H000000FF,&H00121216,&H9609090B,1,0,0,0,100,100,0,0,3,14,0,2,60,60,{marginV},1");
        sb.AppendLine($"Style: Top,Be Vietnam Pro,{fontSize},&H00FFFFFF,&H000000FF,&H00121216,&H9609090B,1,0,0,0,100,100,0,0,3,14,0,8,60,60,{marginV},1");
        sb.AppendLine($"Style: Center,Be Vietnam Pro,{fontSize},&H00FFFFFF,&H000000FF,&H00121216,&H9609090B,1,0,0,0,100,100,0,0,3,14,0,5,60,60,40,1");
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        double timelineOffset = 0;
        for (int i = 0; i < scenes.Count; i++)
        {
            var ps = scenes[i];
            double start = timelineOffset;
            double end = timelineOffset + ps.DurationSeconds - (i < scenes.Count - 1 ? fadeDur : 0);
            timelineOffset = end;

            var text = (ps.Scene.Content ?? ps.Scene.Title ?? "").Replace("\r", " ").Replace("\n", "\\N").Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            var style = (ps.Scene.SubtitlePosition ?? "bottom").ToLowerInvariant() switch
            {
                "top" => "Top",
                "center" => "Center",
                _ => "Bottom"
            };
            sb.AppendLine($"Dialogue: 0,{FormatAssTime(start)},{FormatAssTime(Math.Max(start + 0.5, end))},{style},,0,0,0,,{text}");
        }

        await File.WriteAllTextAsync(assPath, sb.ToString(), new UTF8Encoding(true), ct);
    }

    private static string FormatAssTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}";
    }

    private static string EscapeFfmpegFilterPath(string path)
    {
        return path.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");
    }

    private async Task<bool> SupportsNvencAsync(CancellationToken ct)
    {
        if (_nvencSupported.HasValue) return _nvencSupported.Value;
        var (code, err) = await RunProcessAsync(_ffmpegPath, "-hide_banner -encoders", timeoutMs: 5000, ct: ct);
        // FFmpeg prints encoders to stdout; run quick null encode probe for real RTX driver check
        var (probeCode, _) = await RunProcessAsync(
            _ffmpegPath,
            "-y -f lavfi -i color=c=black:s=256x256:d=0.1 -c:v h264_nvenc -f null -",
            timeoutMs: 5000,
            ct: ct);
        _nvencSupported = probeCode == 0;
        _logger.LogInformation("FFmpeg NVENC hardware acceleration: {Supported}", _nvencSupported.Value);
        return _nvencSupported.Value;
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private async Task<bool> GeneratePlaceholderImageAsync(string outputPath, int width, int height, string label)
    {
        var args = $"-y -f lavfi -i color=c=0x0D0A1A:s={width}x{height}:d=1 -frames:v 1 \"{outputPath}\"";
        var (code, _) = await RunProcessAsync(_ffmpegPath, args);
        return code == 0;
    }

    private static async Task<bool> GenerateSilenceWavAsync(string outputPath, double durationSeconds)
    {
        try
        {
            await File.WriteAllBytesAsync(outputPath, CreateSilentWavBytes(durationSeconds));
            return true;
        }
        catch { return false; }
    }

    private static byte[] CreateSilentWavBytes(double durationSeconds, int sampleRate = 48000)
    {
        int numChannels = 2;
        int bitsPerSample = 16;
        int numSamples = Math.Max(1, (int)(durationSeconds * sampleRate));
        int dataSize = numSamples * numChannels * (bitsPerSample / 8);
        int byteRate = sampleRate * numChannels * (bitsPerSample / 8);
        int blockAlign = numChannels * (bitsPerSample / 8);

        using var ms = new MemoryStream(44 + dataSize);
        using var bw = new BinaryWriter(ms);
        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataSize);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)numChannels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)blockAlign);
        bw.Write((short)bitsPerSample);
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);
        bw.Write(new byte[dataSize]);
        return ms.ToArray();
    }

    private async Task<bool> ConcatAudioFilesAsync(List<string> audioFiles, string outputPath, CancellationToken ct)
    {
        if (audioFiles.Count == 1)
        {
            File.Copy(audioFiles[0], outputPath, true);
            return true;
        }

        var listFile = Path.Combine(Path.GetDirectoryName(outputPath)!, "audiolist.txt");
        var sb = new StringBuilder();
        foreach (var af in audioFiles)
        {
            sb.AppendLine($"file '{af.Replace('\\', '/')}'");
        }
        await File.WriteAllTextAsync(listFile, sb.ToString(), new UTF8Encoding(false), ct);

        var args = $"-y -f concat -safe 0 -i \"{listFile}\" -c:a pcm_s16le -ar 48000 -ac 2 \"{outputPath}\"";
        var (code, _) = await RunProcessAsync(_ffmpegPath, args, ct: ct);
        return code == 0;
    }

    private static async Task<(int ExitCode, string StdErr)> RunProcessAsync(
        string exe,
        string args,
        string? workingDir = null,
        Dictionary<string, string>? extraEnv = null,
        int timeoutMs = 180_000,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (!string.IsNullOrWhiteSpace(workingDir)) psi.WorkingDirectory = workingDir;
        if (extraEnv != null)
        {
            foreach (var kv in extraEnv) psi.Environment[kv.Key] = kv.Value;
        }

        using var proc = Process.Start(psi);
        if (proc == null) return (-1, "Không thể khởi động tiến trình.");

        var stdErrTask = proc.StandardError.ReadToEndAsync(ct);
        var stdOutTask = proc.StandardOutput.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);
        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return (-1, "Tiến trình hết thời gian chờ.");
        }

        var stdErr = await stdErrTask;
        _ = await stdOutTask;
        return (proc.ExitCode, stdErr);
    }

    private sealed record PreparedScene(Scene Scene, string ImagePath, string AudioPath, double StartSeconds, double DurationSeconds);
}
