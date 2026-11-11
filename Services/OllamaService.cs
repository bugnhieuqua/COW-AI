using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace COW_AI.Services;

public class OllamaService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<OllamaService>? _logger;
    private readonly string _cloudBaseUrl;
    private readonly string _localBaseUrl;
    private readonly string _defaultModel;

    public OllamaService(HttpClient http, IConfiguration config, ILogger<OllamaService>? logger = null)
    {
        _http = http;
        _config = config;
        _logger = logger;

        _cloudBaseUrl = config["Ollama:BaseUrl"] ?? "https://ollama.com/v1";
        _localBaseUrl = config["Ollama:FallbackLocalUrl"] ?? "http://localhost:11434/v1";
        _defaultModel = config["Ollama:DefaultModel"] ?? "gpt-oss:20b";
    }

    /// <summary>
    /// Phân giải model key từ giao diện người dùng thành định danh model tương ứng trên Ollama.
    /// </summary>
    public string ResolveModel(string? modelKey)
    {
        if (string.IsNullOrWhiteSpace(modelKey))
        {
            return _defaultModel;
        }

        var key = modelKey.Trim();
        if (key.StartsWith("ollama:", StringComparison.OrdinalIgnoreCase))
        {
            key = key["ollama:".Length..].Trim();
        }

        return key.ToLowerInvariant() switch
        {
            "gpt20-cloud" or "gpt20" or "gpt-oss-20b" => "gpt-oss:20b",
            "120b-cloud" or "120b" or "gpt-oss-120b" => "gpt-oss:120b",
            "mistral-nemo" or "neu" or "nemo" => "mistral-nemo:latest",
            "phi4-cloud" or "phi4" or "phi-4" => "phi4:latest",
            _ => key
        };
    }

    /// <summary>
    /// Kiểm tra xem một model key có thuộc hệ sinh thái Ollama hay không.
    /// </summary>
    public static bool IsOllamaModel(string? modelKey)
    {
        if (string.IsNullOrWhiteSpace(modelKey)) return false;
        var key = modelKey.Trim().ToLowerInvariant();
        if (key.StartsWith("ollama:")) return true;

        return key is "gpt20-cloud" or "gpt20" or "gpt-oss-20b" or "gpt-oss:20b"
                  or "120b-cloud" or "120b" or "gpt-oss-120b" or "gpt-oss:120b"
                  or "mistral-nemo" or "neu" or "nemo"
                  or "phi4-cloud" or "phi4" or "phi-4";
    }

    /// <summary>
    /// Gửi yêu cầu Chat và trả về luồng dữ liệu SSE (Server-Sent Events) từ Ollama Cloud hoặc Local Daemon.
    /// </summary>
    public async Task<Stream> StreamChatAsync(
        string prompt, 
        string? customModel = null, 
        float? temperature = null, 
        int? maxTokens = null, 
        CancellationToken cancellationToken = default)
    {
        var (targetBaseUrl, apiKey) = GetEndpointAndAuth();
        var modelToUse = ResolveModel(customModel);

        var payloadDict = new Dictionary<string, object>
        {
            ["model"] = modelToUse,
            ["messages"] = new[] { new { role = "user", content = prompt } },
            ["stream"] = true
        };

        if (temperature.HasValue)
        {
            payloadDict["temperature"] = temperature.Value;
        }

        if (maxTokens.HasValue && maxTokens.Value > 0)
        {
            payloadDict["max_tokens"] = maxTokens.Value;
        }

        var endpoint = targetBaseUrl.TrimEnd('/') + "/chat/completions";
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payloadDict)
        };

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        if (!request.Headers.Contains("HTTP-Referer"))
        {
            request.Headers.Add("HTTP-Referer", "https://cow-ai.local");
        }
        if (!request.Headers.Contains("X-Title"))
        {
            request.Headers.Add("X-Title", "COW.AI");
        }

        try
        {
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger?.LogError("Ollama API trả về HTTP {StatusCode}: {ErrorBody}", (int)response.StatusCode, errorBody);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new InvalidOperationException("Chưa cấu hình hoặc API Key Ollama Cloud không hợp lệ. Vui lòng thiết lập OLLAMA_API_KEY trong Key.env hoặc bật Ollama daemon nội bộ (localhost:11434).");
                }

                if ((int)response.StatusCode == 402)
                {
                    var msg = "Tài khoản Ollama Cloud của bạn chưa có credits (HTTP 402: Payment Required). Vui lòng nạp thêm lượt dùng tại https://ollama.com/plans hoặc khởi động Ollama trên máy tính (localhost:11434) để dùng miễn phí.";
                    try
                    {
                        using var doc = JsonDocument.Parse(errorBody);
                        if (doc.RootElement.TryGetProperty("error", out var errElem) && errElem.TryGetProperty("message", out var msgElem))
                        {
                            msg = $"[Ollama Cloud 402]: {msgElem.GetString()}";
                        }
                    }
                    catch { }
                    throw new InvalidOperationException(msg);
                }

                string genericMsg = $"Ollama API HTTP {(int)response.StatusCode}";
                try
                {
                    using var doc = JsonDocument.Parse(errorBody);
                    if (doc.RootElement.TryGetProperty("error", out var errElem) && errElem.TryGetProperty("message", out var msgElem))
                    {
                        genericMsg = $"[Ollama API {(int)response.StatusCode}]: {msgElem.GetString()}";
                    }
                }
                catch { }

                throw new InvalidOperationException(genericMsg);
            }

            return await response.Content.ReadAsStreamAsync(cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException || ex.Message.Contains("actively refused", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogError(ex, "Không thể kết nối đến Ollama endpoint {Url}", endpoint);
            throw new InvalidOperationException($"Không thể kết nối đến dịch vụ Ollama ({targetBaseUrl}). Vui lòng kiểm tra kết nối mạng hoặc khởi động Ollama daemon tại máy cục bộ.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not InvalidOperationException)
        {
            _logger?.LogError(ex, "Lỗi xảy ra khi gọi Ollama API cho model {Model}", modelToUse);
            throw;
        }
    }

    /// <summary>
    /// Gửi yêu cầu Chat không streaming và trả về chuỗi nội dung văn bản hoàn chỉnh.
    /// </summary>
    public async Task<string> ChatAsync(string prompt, string? customModel = null, CancellationToken cancellationToken = default)
    {
        var (targetBaseUrl, apiKey) = GetEndpointAndAuth();
        var modelToUse = ResolveModel(customModel);

        var payload = new
        {
            model = modelToUse,
            messages = new[] { new { role = "user", content = prompt } },
            stream = false
        };

        var endpoint = targetBaseUrl.TrimEnd('/') + "/chat/completions";
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload)
        };

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        try
        {
            var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger?.LogError("Ollama ChatAsync HTTP {StatusCode}: {ErrorBody}", (int)response.StatusCode, errorBody);

                string errorMsg = $"Ollama ChatAsync returned HTTP {(int)response.StatusCode}";
                try
                {
                    using var errDoc = JsonDocument.Parse(errorBody);
                    if (errDoc.RootElement.TryGetProperty("error", out var errElem) && errElem.TryGetProperty("message", out var msgElem))
                    {
                        errorMsg = $"[Ollama {(int)response.StatusCode}]: {msgElem.GetString()}";
                    }
                }
                catch { }

                throw new InvalidOperationException(errorMsg);
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var contentElem))
                {
                    return contentElem.GetString()?.Trim() ?? string.Empty;
                }
            }

            return string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Lỗi xảy ra khi gọi Ollama ChatAsync cho model {Model}", modelToUse);
            throw;
        }
    }

    private (string TargetBaseUrl, string? ApiKey) GetEndpointAndAuth()
    {
        var apiKey = _config["Ollama:ApiKey"] 
            ?? _config["OLLAMA_API_KEY"] 
            ?? _config["OLLAMA"]
            ?? Environment.GetEnvironmentVariable("Ollama__ApiKey")
            ?? Environment.GetEnvironmentVariable("OLLAMA_API_KEY")
            ?? Environment.GetEnvironmentVariable("OLLAMA");

        var isPlaceholder = string.IsNullOrWhiteSpace(apiKey) ||
            apiKey.Contains("your_", StringComparison.OrdinalIgnoreCase) ||
            apiKey.Contains("placeholder", StringComparison.OrdinalIgnoreCase) ||
            apiKey.Contains("xxxxxxxx", StringComparison.OrdinalIgnoreCase);

        if (!isPlaceholder && !string.IsNullOrWhiteSpace(apiKey))
        {
            return (_cloudBaseUrl, apiKey.Trim());
        }

        // Nếu không có API Key, ưu tiên kết nối local daemon
        return (_localBaseUrl, null);
    }
}
