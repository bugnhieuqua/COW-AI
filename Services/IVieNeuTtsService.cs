namespace COW_AI.Services;

public record VieNeuVoiceOption(string Id, string Name, string Gender, string Engine);

public record VieNeuServerStatus(
    string State,
    string Message,
    bool IsReady,
    bool IsLoading,
    string Engine,
    int SampleRate
);

public interface IVieNeuTtsService
{
    VieNeuServerStatus GetServerStatus();

    void TriggerWarmUp();

    Task<VieNeuServerStatus> EnsureServerLoadedAsync(CancellationToken ct = default);

    Task<(bool Success, string? Error, string AudioUrl, double DurationSeconds)> GenerateAudioAsync(
        string text,
        string voiceName,
        string relativeSubDir,
        CancellationToken ct = default);

    Task<(bool Success, string? Error, byte[] AudioBytes, string ContentType)> SynthesizeWavBytesAsync(
        string text,
        string voiceName,
        CancellationToken ct = default);

    Task<double> MeasureAudioDurationAsync(string localAudioPath, CancellationToken ct = default);

    List<VieNeuVoiceOption> GetAvailableVoices();
}
