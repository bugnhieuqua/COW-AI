using System.Text;

namespace COW_AI.Services;

public class EdgeTtsService
{
    private const int MaxChunkLength = 1800;
    private const string FallbackVoice = "vi-VN-HoaiMyNeural";

    private readonly EdgeTtsClient _client;
    private readonly string _defaultVoice;

    public EdgeTtsService(EdgeTtsClient client, IConfiguration config)
    {
        _client = client;

        var configured = config["EdgeTts:DefaultVoice"];
        _defaultVoice = string.IsNullOrWhiteSpace(configured) ? FallbackVoice : configured.Trim();
    }

    /// <summary>
    /// Stream âm thanh MP3 trực tiếp vào outputStream (thường là Response.Body).
    /// Văn bản dài được chia nhỏ thành nhiều đoạn để tránh giới hạn của Edge TTS.
    /// </summary>
    public async Task StreamToResponseAsync(
        string text,
        Stream outputStream,
        string? voiceName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || outputStream is null)
        {
            return;
        }

        var voice = ResolveVoiceName(voiceName);

        foreach (var chunk in SplitIntoChunks(text, MaxChunkLength))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _client.StreamAsync(chunk, outputStream, voice, cancellationToken);
        }
    }

    /// <summary>
    /// Lấy danh sách toàn bộ giọng đọc có sẵn (dùng cho dropdown nếu mở rộng).
    /// </summary>
    public Task<IReadOnlyList<EdgeTtsVoice>> GetAvailableVoicesAsync(CancellationToken cancellationToken = default)
    {
        return _client.GetVoicesAsync(cancellationToken);
    }

    private string ResolveVoiceName(string? voiceName)
    {
        return string.IsNullOrWhiteSpace(voiceName) ? _defaultVoice : voiceName.Trim();
    }

    /// <summary>
    /// Chia văn bản theo ranh giới câu để giữ ngữ điệu tự nhiên và tránh vượt giới hạn request.
    /// </summary>
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

            if (buffer.Length > 0 && buffer.Length + 1 + sentence.Length > maxLength)
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

    private static IEnumerable<string> SplitSentences(string text)
    {
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var isSentenceEnd = ch is '.' or '!' or '?' or ';' or ':' or '…' or '\n';
            if (!isSentenceEnd)
            {
                continue;
            }

            var end = i + 1;
            while (end < text.Length && char.IsWhiteSpace(text[end]))
            {
                end++;
            }

            var sentence = text.Substring(start, end - start).Trim();
            if (sentence.Length > 0)
            {
                yield return sentence;
            }

            start = end;
            i = end - 1;
        }

        if (start < text.Length)
        {
            var tail = text.Substring(start).Trim();
            if (tail.Length > 0)
            {
                yield return tail;
            }
        }
    }
}
