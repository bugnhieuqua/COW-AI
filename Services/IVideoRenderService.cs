using COW_AI.Models;

namespace COW_AI.Services;

public record VideoRenderResult(
    bool Success, 
    string? Error, 
    string? VideoUrl, 
    string? LocalFilePath, 
    double TotalDurationSeconds);

public record RenderProgressSnapshot(
    string Step,
    int Percent,
    string Detail,
    bool IsRunning,
    DateTime UpdatedAt);

public interface IVideoRenderService
{
    Task<VideoRenderResult> RenderProjectVideoAsync(
        int projectId, 
        string aspectRatio = "16:9", 
        bool includeBgm = true,
        string renderMode = "fast",
        CancellationToken ct = default);

    RenderProgressSnapshot GetRenderProgress(int projectId);

    bool IsFfmpegAvailable();
}
