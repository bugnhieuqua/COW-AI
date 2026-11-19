using COW_AI.Models;

namespace COW_AI.Services;

public record VideoRenderResult(
    bool Success, 
    string? Error, 
    string? VideoUrl, 
    string? LocalFilePath, 
    double TotalDurationSeconds);

public interface IVideoRenderService
{
    Task<VideoRenderResult> RenderProjectVideoAsync(
        int projectId, 
        string aspectRatio = "16:9", 
        bool includeBgm = true,
        string renderMode = "fast",
        CancellationToken ct = default);

    bool IsFfmpegAvailable();
}
