using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace COW_AI.Services;

public sealed record EdgeTtsVoice(
    string ShortName,
    string Locale,
    string Gender,
    string FriendlyName);

/// <summary>
/// Client tối giản cho Edge TTS Read Aloud (WebSocket), dùng token Sec-MS-GEC v2
/// để tương thích dịch vụ hiện tại của Microsoft.
/// </summary>
public class EdgeTtsClient
{
    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string ChromiumFullVersion = "143.0.3650.75";
    private const string SecMsGecVersion = "2-" + ChromiumFullVersion;
    private const string OutputFormat = "audio-24khz-48kbitrate-mono-mp3";
    private const string WssBaseUrl =
        "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1" +
        "?TrustedClientToken=" + TrustedClientToken;
    private const string VoiceListUrl =
        "https://speech.platform.bing.com/consumer/speech/synthesize/readaloud/voices/list" +
        "?trustedclienttoken=" + TrustedClientToken;
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0";

    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    /// <summary>
    /// Stream âm thanh MP3 của một đoạn văn bản trực tiếp vào outputStream.
    /// </summary>
    public async Task StreamAsync(string text, Stream outputStream, string voiceName, CancellationToken cancellationToken = default)
    {
        var cleanedText = PrepareText(text);
        if (string.IsNullOrWhiteSpace(cleanedText))
        {
            return;
        }

        var secMsGec = EdgeTtsDrm.GenerateSecMsGec();
        var connectionId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");

        var url = $"{WssBaseUrl}&ConnectionId={connectionId}&Sec-MS-GEC={secMsGec}&Sec-MS-GEC-Version={SecMsGecVersion}";

        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Pragma", "no-cache");
        socket.Options.SetRequestHeader("Cache-Control", "no-cache");
        socket.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        socket.Options.SetRequestHeader("User-Agent", UserAgent);
        socket.Options.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");
        socket.Options.SetRequestHeader("Cookie", $"muid={Guid.NewGuid():N}".ToUpperInvariant() + ";");

        await socket.ConnectAsync(new Uri(url), cancellationToken);

        await SendTextAsync(socket, BuildConfigMessage(), cancellationToken);
        await SendTextAsync(socket, BuildSsmlMessage(requestId, voiceName, cleanedText), cancellationToken);

        var buffer = new byte[16 * 1024];
        using var messageBuffer = new MemoryStream();
        var audioReceived = false;

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            messageBuffer.SetLength(0);

            ValueWebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
                messageBuffer.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                var data = messageBuffer.ToArray();
                if (data.Length < 2)
                {
                    continue;
                }

                var headerLength = (data[0] << 8) | data[1];
                if (headerLength > data.Length || headerLength < 2)
                {
                    continue;
                }

                var headerText = Encoding.UTF8.GetString(data, 2, headerLength - 2);
                var audioStart = headerLength + 2;

                if (headerText.Contains("Path:audio", StringComparison.Ordinal) && audioStart < data.Length)
                {
                    audioReceived = true;
                    await outputStream.WriteAsync(data.AsMemory(audioStart), cancellationToken);
                    await outputStream.FlushAsync(cancellationToken);
                }
            }
            else if (result.MessageType == WebSocketMessageType.Text)
            {
                var textMessage = Encoding.UTF8.GetString(messageBuffer.ToArray());
                var separatorIndex = textMessage.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                var headers = separatorIndex >= 0 ? textMessage[..separatorIndex] : textMessage;

                if (headers.Contains("Path:turn.end", StringComparison.Ordinal))
                {
                    break;
                }
            }
        }

        if (!audioReceived)
        {
            throw new InvalidOperationException("Edge TTS không trả về dữ liệu âm thanh. Vui lòng thử lại sau.");
        }
    }

    /// <summary>
    /// Lấy danh sách giọng đọc từ dịch vụ Edge TTS.
    /// </summary>
    public async Task<IReadOnlyList<EdgeTtsVoice>> GetVoicesAsync(CancellationToken cancellationToken = default)
    {
        var url = $"{VoiceListUrl}&Sec-MS-GEC={EdgeTtsDrm.GenerateSecMsGec()}&Sec-MS-GEC-Version={SecMsGecVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Cookie", $"muid={Guid.NewGuid():N}".ToUpperInvariant() + ";");

        using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var voices = new List<EdgeTtsVoice>();
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return voices;
        }

        foreach (var item in document.RootElement.EnumerateArray())
        {
            var shortName = GetString(item, "ShortName");
            if (string.IsNullOrWhiteSpace(shortName))
            {
                continue;
            }

            voices.Add(new EdgeTtsVoice(
                shortName,
                GetString(item, "Locale"),
                GetString(item, "Gender"),
                GetString(item, "FriendlyName")));
        }

        return voices;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        return client;
    }

    private static async Task SendTextAsync(ClientWebSocket socket, string message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, cancellationToken);
    }

    private static string BuildConfigMessage()
    {
        var json = "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{" +
                   "\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"}," +
                   "\"outputFormat\":\"" + OutputFormat + "\"}}}}";

        return $"X-Timestamp:{BuildTimestamp()}\r\n" +
               "Content-Type:application/json; charset=utf-8\r\n" +
               "Path:speech.config\r\n\r\n" +
               json;
    }

    private static string BuildSsmlMessage(string requestId, string voiceName, string escapedText)
    {
        var locale = ResolveLocale(voiceName);
        var ssml = "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='" + locale + "'>" +
                   "<voice name='" + voiceName + "'>" +
                   "<prosody pitch='+0Hz' rate='+0%' volume='+0%'>" +
                   escapedText +
                   "</prosody></voice></speak>";

        return $"X-RequestId:{requestId}\r\n" +
               "Content-Type:application/ssml+xml\r\n" +
               $"X-Timestamp:{BuildTimestamp()}\r\n" +
               "Path:ssml\r\n\r\n" +
               ssml;
    }

    private static string BuildTimestamp()
    {
        return DateTime.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'",
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ResolveLocale(string voiceName)
    {
        var parts = voiceName.Split('-');
        return parts.Length >= 2 ? parts[0] + "-" + parts[1] : "en-US";
    }

    /// <summary>
    /// Loại bỏ ký tự không tương thích và escape XML để nhúng an toàn vào SSML.
    /// </summary>
    private static string PrepareText(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var ch in text.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            var code = (int)ch;
            var incompatible = code <= 8 || code is 11 or 12 || (code >= 14 && code <= 31);
            builder.Append(incompatible ? ' ' : ch);
        }

        var cleaned = builder.ToString().Trim();
        return System.Security.SecurityElement.Escape(cleaned) ?? string.Empty;
    }
}
