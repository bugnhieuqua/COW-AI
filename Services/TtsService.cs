using System.Text;
using Microsoft.Extensions.Configuration;

namespace COW_AI.Services;

/// <summary>
/// Facade service cung cấp giao diện tinh gọn, đóng gói toàn bộ logic gọi
/// EdgeTtsClient (với DRM v2, chia câu tự nhiên và cơ chế retry khi socket bị gián đoạn).
/// </summary>
public class TtsService : ITtsService
{
    private const int MaxChunkLength = 1800;
    private readonly EdgeTtsClient _client;
    private readonly string _defaultVoice;

    public TtsService(EdgeTtsClient client, IConfiguration config)
    {
        _client = client;
        var configured = config["EdgeTts:DefaultVoice"];
        _defaultVoice = string.IsNullOrWhiteSpace(configured) ? "vi-VN-HoaiMyNeural" : configured.Trim();
    }

    /// <summary>
    /// Stream âm thanh MP3 trực tiếp vào outputStream (Response.Body).
    /// Sử dụng bộ đệm MemoryStream từng phân đoạn để bảo vệ Response stream không bị hỏng
    /// và hỗ trợ retry tự động với backoff khi socket gặp lỗi 10054/gián đoạn mạng.
    /// </summary>
    public async Task StreamToResponseAsync(
        string text, 
        Stream outputStream, 
        string? voiceName = null, 
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) || outputStream is null)
        {
            return;
        }

        var voice = string.IsNullOrWhiteSpace(voiceName) ? _defaultVoice : voiceName.Trim();

        foreach (var chunk in SplitIntoChunks(text, MaxChunkLength))
        {
            ct.ThrowIfCancellationRequested();

            var chunkReceived = false;
            var currentVoice = voice;
            Exception? lastException = null;

            // Thử tối đa 3 lần với backoff nếu gặp sự cố kết nối WebSocket
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                using var memoryStream = new MemoryStream();
                try
                {
                    await _client.StreamAsync(chunk, memoryStream, currentVoice, ct);
                    if (memoryStream.Length > 0)
                    {
                        memoryStream.Position = 0;
                        await memoryStream.CopyToAsync(outputStream, ct);
                        await outputStream.FlushAsync(ct);
                        chunkReceived = true;
                        break;
                    }
                }
                catch (Exception ex) when (ex is System.Net.WebSockets.WebSocketException or System.IO.IOException or InvalidOperationException)
                {
                    lastException = ex;
                    if (attempt < 3)
                    {
                        // Giọng dự phòng ở lần thử thứ 3
                        if (attempt == 2)
                        {
                            currentVoice = voice == "vi-VN-HoaiMyNeural" ? "vi-VN-NamMinhNeural" : "vi-VN-HoaiMyNeural";
                        }
                        await Task.Delay(attempt * 300, ct);
                    }
                }
            }

            if (!chunkReceived)
            {
                throw lastException ?? new InvalidOperationException("Không thể tạo dữ liệu âm thanh sau nhiều lần thử.");
            }
        }
    }

    private static IEnumerable<string> SplitIntoChunks(string text, int maxLength)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.Length <= maxLength)
        {
            yield return normalized;
            yield break;
        }

        var buffer = new StringBuilder();
        var sentences = SplitSentences(normalized);

        foreach (var sentence in sentences)
        {
            if (sentence.Length > maxLength)
            {
                if (buffer.Length > 0)
                {
                    yield return buffer.ToString().Trim();
                    buffer.Clear();
                }

                for (var i = 0; i < sentence.Length; i += maxLength)
                {
                    var slice = sentence.Substring(i, Math.Min(maxLength, sentence.Length - i)).Trim();
                    if (slice.Length > 0)
                    {
                        yield return slice;
                    }
                }

                continue;
            }

            if (buffer.Length + sentence.Length + 1 > maxLength)
            {
                yield return buffer.ToString().Trim();
                buffer.Clear();
            }

            if (buffer.Length > 0)
            {
                buffer.Append(' ');
            }
            buffer.Append(sentence);
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString().Trim();
        }
    }

    private static List<string> SplitSentences(string text)
    {
        var result = new List<string>();
        var current = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            current.Append(ch);

            var isTerminator = ch is '.' or '!' or '?' or ';' or '。' or '！' or '？' or '\n';
            if (!isTerminator) continue;

            if (i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]))
            {
                var sentence = current.ToString().Trim();
                if (sentence.Length > 0)
                {
                    result.Add(sentence);
                }
                current.Clear();
            }
            else if (ch == '\n')
            {
                var sentence = current.ToString().Trim();
                if (sentence.Length > 0)
                {
                    result.Add(sentence);
                }
                current.Clear();
            }
        }

        var rest = current.ToString().Trim();
        if (rest.Length > 0)
        {
            result.Add(rest);
        }

        return result;
    }
}
