namespace COW_AI.Services;

/// <summary>
/// Interface trừu tượng cho dịch vụ Text-to-Speech (TTS),
/// cho phép gọi phát âm thanh trực tiếp vào luồng Stream.
/// </summary>
public interface ITtsService
{
    Task StreamToResponseAsync(
        string text, 
        Stream outputStream, 
        string? voiceName = null, 
        CancellationToken ct = default);
}
