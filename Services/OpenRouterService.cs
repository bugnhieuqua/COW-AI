using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace COW_AI.Services;

public class OpenRouterService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<OpenRouterService>? _logger;
    private readonly string _defaultModel;
    private readonly string _codeGenModel;
    private readonly string _summaryModel;
    private readonly string _omniRouteModel;

    public OpenRouterService(HttpClient http, IConfiguration config, ILogger<OpenRouterService>? logger = null)
    {
        _http = http;
        _config = config;
        _logger = logger;

        if (_http.BaseAddress == null)
        {
            var baseUrl = config["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1";
            if (!baseUrl.EndsWith("/"))
            {
                baseUrl += "/";
            }
            _http.BaseAddress = new Uri(baseUrl);
        }

        // Đọc các Model từ cấu hình
        _defaultModel = config["OpenRouter:DefaultModel"] ?? "google/gemini-2.0-flash-exp:free";
        _codeGenModel = config["OpenRouter:CodeGenModel"] ?? "deepseek/deepseek-r1:free";
        _summaryModel = config["OpenRouter:SummaryModel"] ?? "meta-llama/llama-3-8b-instruct:free";
        _omniRouteModel = config["OpenRouter:OmniRouteModel"] ?? "openrouter/auto";
    }

    /// <summary>
    /// Gửi yêu cầu Chat và trả về luồng dữ liệu SSE (Server-Sent Events) từ OpenRouter API.
    /// Hỗ trợ truyền linh hoạt temperature và maxTokens.
    /// </summary>
    public async Task<Stream> StreamChatAsync(
        string prompt, 
        string? customModel = null, 
        float? temperature = null, 
        int? maxTokens = null, 
        CancellationToken cancellationToken = default)
    {
        // Bảo mật: Đọc Key từ User Secrets / Biến môi trường
        var apiKey = _config["OpenRouter:ApiKey"] 
            ?? _config["OPENROUTER_API_KEY"] 
            ?? Environment.GetEnvironmentVariable("OpenRouter__ApiKey")
            ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey) || 
            apiKey.Contains("xxxxxxxx", StringComparison.OrdinalIgnoreCase) ||
            apiKey.Contains("your_", StringComparison.OrdinalIgnoreCase) ||
            apiKey.Contains("placeholder", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogError("Chưa cấu hình OpenRouter:ApiKey hoặc API Key không hợp lệ.");
            throw new InvalidOperationException("Chưa cấu hình OpenRouter:ApiKey.");
        }

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

        var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payloadDict)
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
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
                _logger?.LogError("OpenRouter API trả về HTTP {StatusCode}: {ErrorBody}", (int)response.StatusCode, errorBody);
                throw new HttpRequestException($"OpenRouter API returned HTTP {(int)response.StatusCode}");
            }

            return await response.Content.ReadAsStreamAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Lỗi xảy ra khi gọi OpenRouter API cho model {Model}", modelToUse);
            throw;
        }
    }

    /// <summary>
    /// Gửi yêu cầu Chat không streaming và trả về chuỗi nội dung văn bản hoàn chỉnh.
    /// Dùng cho các tác vụ dịch thuật, tóm tắt nhanh hoặc tối ưu prompt.
    /// </summary>
    public async Task<string> ChatAsync(string prompt, string? customModel = null, CancellationToken cancellationToken = default)
    {
        var apiKey = _config["OpenRouter:ApiKey"] 
            ?? _config["OPENROUTER_API_KEY"] 
            ?? Environment.GetEnvironmentVariable("OpenRouter__ApiKey")
            ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey) || 
            apiKey.Contains("xxxxxxxx", StringComparison.OrdinalIgnoreCase) ||
            apiKey.Contains("your_", StringComparison.OrdinalIgnoreCase) ||
            apiKey.Contains("placeholder", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning("Chưa cấu hình OpenRouter:ApiKey hoặc API Key không hợp lệ khi gọi ChatAsync.");
            throw new InvalidOperationException("Chưa cấu hình OpenRouter:ApiKey.");
        }

        var modelToUse = ResolveModel(customModel ?? "gemini");

        var payload = new
        {
            model = modelToUse,
            messages = new[] { new { role = "user", content = prompt } },
            stream = false
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
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
            var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger?.LogError("OpenRouter ChatAsync HTTP {StatusCode}: {ErrorBody}", (int)response.StatusCode, errorBody);
                throw new HttpRequestException($"OpenRouter ChatAsync returned HTTP {(int)response.StatusCode}: {errorBody}");
            }

            using var doc = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var contentElem))
                {
                    return contentElem.GetString()?.Trim() ?? string.Empty;
                }
                if (firstChoice.TryGetProperty("text", out var textElem))
                {
                    return textElem.GetString()?.Trim() ?? string.Empty;
                }
            }

            return string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Lỗi xảy ra khi gọi OpenRouter ChatAsync cho model {Model}", modelToUse);
            throw;
        }
    }

    /// <summary>
    /// Ánh xạ lựa chọn từ giao diện (Dropdown) sang Model định danh OpenRouter.
    /// </summary>
    public string ResolveModel(string? modelKey)
    {
        if (string.IsNullOrWhiteSpace(modelKey))
        {
            return _defaultModel;
        }

        return modelKey.Trim().ToLowerInvariant() switch
        {
            "gemini" => _defaultModel,
            "deepseek" => _codeGenModel,
            "llama3" or "llama" => _summaryModel,
            "omniroute" => _omniRouteModel,
            "auto" => _defaultModel,
            _ => modelKey.Trim()
        };
    }
}
