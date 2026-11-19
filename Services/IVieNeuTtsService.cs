namespace COW_AI.Services;

public record VieNeuVoiceOption(string Id, string Name, string Gender, string Engine);

public interface IVieNeuTtsService
{
    Task<(bool Success, string? Error, string AudioUrl, double DurationSeconds)> GenerateAudioAsync(
        string text, 
        string voiceName, 
        string relativeSubDir, 
        CancellationToken ct = default);

    Task<double> MeasureAudioDurationAsync(string localAudioPath, CancellationToken ct = default);

    List<VieNeuVoiceOption> GetAvailableVoices();
}
