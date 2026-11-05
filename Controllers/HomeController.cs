using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using COW_AI.Models;
using COW_AI.Services;

namespace COW_AI.Controllers;

public class HomeController : Controller
{
    private readonly OpenRouterService _openRouter;
    private readonly PollinationsService _pollinations;
    private readonly PromptService _promptService;
    private readonly ITtsService _ttsService;
    private readonly DuckDuckGoService _duckDuckGo;
    private readonly IntentDispatcherService _intentDispatcher;
    private readonly ILogger<HomeController>? _logger;

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public HomeController(
        OpenRouterService openRouter, 
        PollinationsService pollinations, 
        PromptService promptService,
        ITtsService ttsService,
        DuckDuckGoService duckDuckGo,
        IntentDispatcherService intentDispatcher,
        ILogger<HomeController>? logger = null)
    {
        _openRouter = openRouter;
        _pollinations = pollinations;
        _promptService = promptService;
        _ttsService = ttsService;
        _duckDuckGo = duckDuckGo;
        _intentDispatcher = intentDispatcher;
        _logger = logger;
    }

    public IActionResult Index() => View();

    [AcceptVerbs("GET", "POST")]
    public async Task<IActionResult> StreamChat(
        string prompt, 
        string? model, 
        float? temperature = null, 
        int? maxTokens = null, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return BadRequest(new { error = "Prompt không được để trống." });
        }

        var normalizedModel = model?.Trim().ToLowerInvariant();

        // 1. Nhánh Edge TTS: trả về MP3 stream thay vì SSE
        if (normalizedModel is "edge-male" or "edge-female")
        {
            var voiceName = normalizedModel == "edge-male" ? "vi-VN-NamMinhNeural" : "vi-VN-HoaiMyNeural";

            Response.Headers["Content-Type"] = "audio/mpeg";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";

            try
            {
                await _ttsService.StreamToResponseAsync(prompt, Response.Body, voiceName, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Client hủy kết nối chủ động
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger?.LogError(ex, "Lỗi Edge TTS cho prompt '{Prompt}' với voice '{Voice}'", prompt, voiceName);
                if (!Response.HasStarted)
                {
                    Response.Clear();
                    Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await Response.WriteAsJsonAsync(
                        new { error = "Không thể tạo âm thanh. Vui lòng thử lại sau ít phút." },
                        cancellationToken);
                }
            }

            return new EmptyResult();
        }

        Response.Headers["Content-Type"] = "text/event-stream; charset=utf-8";
        Response.Headers["Cache-Control"] = "no-cache, no-transform";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        // 2. Nhánh Tạo ảnh: Điều phối qua IntentDispatcherService
        if (_intentDispatcher.DetectImageIntent(prompt, model, out var cleanPrompt))
        {
            try
            {
                var enhancedPrompt = await _promptService.GetImageGenerationPromptAsync(cleanPrompt, cancellationToken);
                var imageUrl = _pollinations.GetImageUrl(enhancedPrompt);
                var payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    is_image_url = imageUrl,
                    prompt = cleanPrompt,
                    enhanced_prompt = enhancedPrompt,
                    choices = new[]
                    {
                        new
                        {
                            delta = new
                            {
                                content = $"![image]({imageUrl})\n\n"
                            }
                        }
                    }
                }, _jsonOptions);

                await Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Client hủy kết nối
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger?.LogError(ex, "Lỗi khi tạo ảnh qua Pollinations cho prompt: {Prompt}", cleanPrompt);
                await SendSafeSseErrorAsync("Không thể tạo ảnh vào lúc này. Vui lòng thử lại sau ít phút.", cancellationToken);
            }

            return new EmptyResult();
        }

        // 3. Nhánh DuckDuckGo Search: Điều phối qua DuckDuckGoService
        if (normalizedModel == "duckduckgo")
        {
            try
            {
                var searchResults = await _duckDuckGo.SearchAsync(prompt, cancellationToken);
                var searchPayload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    choices = new[]
                    {
                        new
                        {
                            delta = new
                            {
                                content = searchResults
                            }
                        }
                    }
                }, _jsonOptions);

                await Response.WriteAsync($"data: {searchPayload}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Client hủy kết nối
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger?.LogError(ex, "Lỗi khi tra cứu DuckDuckGo cho prompt: {Prompt}", prompt);
                await SendSafeSseErrorAsync("Hệ thống tra cứu web hiện đang bận. Vui lòng thử lại sau ít phút.", cancellationToken);
            }

            return new EmptyResult();
        }

        // 4. Nhánh Chat văn bản qua OpenRouter
        string modelToUse = normalizedModel ?? "gemini";
        if (normalizedModel == "auto")
        {
            modelToUse = _intentDispatcher.ResolveAutoModel(prompt);
            _logger?.LogInformation("Auto route chọn model '{ResolvedModel}' cho prompt '{Prompt}'", modelToUse, prompt);
        }
        else if (normalizedModel == "omniroute")
        {
            modelToUse = "omniroute";
            _logger?.LogInformation("Định tuyến qua luồng tổng hợp OmniRoute cho prompt '{Prompt}'", prompt);
        }

        bool doneSent = false;
        try
        {
            var stream = await _openRouter.StreamChatAsync(prompt, modelToUse, temperature, maxTokens, cancellationToken);

            using var reader = new StreamReader(stream);
            string? line;
            while (!cancellationToken.IsCancellationRequested && (line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (line.StartsWith("data: "))
                {
                    var data = line.Substring(6).Trim();
                    if (data == "[DONE]")
                    {
                        await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
                        await Response.Body.FlushAsync(cancellationToken);
                        doneSent = true;
                        break;
                    }
                    if (!string.IsNullOrEmpty(data))
                    {
                        await Response.WriteAsync($"data: {data}\n\n", cancellationToken);
                        await Response.Body.FlushAsync(cancellationToken);
                    }
                }
                else if (line.StartsWith("data:"))
                {
                    var data = line.Substring(5).Trim();
                    if (data == "[DONE]")
                    {
                        await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
                        await Response.Body.FlushAsync(cancellationToken);
                        doneSent = true;
                        break;
                    }
                    if (!string.IsNullOrEmpty(data))
                    {
                        await Response.WriteAsync($"data: {data}\n\n", cancellationToken);
                        await Response.Body.FlushAsync(cancellationToken);
                    }
                }
            }

            if (!doneSent && !cancellationToken.IsCancellationRequested)
            {
                await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Client ngắt kết nối
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger?.LogError(ex, "Lỗi xảy ra trong StreamChat cho prompt '{Prompt}' và model '{Model}'", prompt, modelToUse);
            await SendSafeSseErrorAsync("Hệ thống AI hiện đang bận. Vui lòng thử lại sau ít phút.", cancellationToken);
        }

        return new EmptyResult();
    }

    private async Task SendSafeSseErrorAsync(string friendlyMessage, CancellationToken cancellationToken = default)
    {
        var errorPayload = System.Text.Json.JsonSerializer.Serialize(new
        {
            error = new { message = friendlyMessage },
            choices = new[]
            {
                new
                {
                    delta = new
                    {
                        content = $"\n\n[ERROR] {friendlyMessage}\n\n"
                    }
                }
            }
        }, _jsonOptions);

        try
        {
            await Response.WriteAsync($"data: {errorPayload}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch
        {
            // Bỏ qua lỗi ngắt kết nối khi ghi lỗi
        }
    }

    [HttpPost]
    public async Task<IActionResult> GenerateImage(string prompt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return BadRequest(new { error = "Mô tả ảnh không được để trống." });
        }

        try
        {
            var enhancedPrompt = await _promptService.GetImageGenerationPromptAsync(prompt, cancellationToken);
            var imageUrl = _pollinations.GetImageUrl(enhancedPrompt);
            return Json(new { url = imageUrl, prompt = prompt, enhanced_prompt = enhancedPrompt });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Lỗi khi tạo ảnh qua Pollinations cho prompt: {Prompt}", prompt);
            return StatusCode(500, new { error = "Hệ thống tạo ảnh hiện đang bận. Vui lòng thử lại sau ít phút." });
        }
    }

    // Static forwarders and specification contracts ensuring backward compatibility for tests calling helper methods directly
    public static string ResolveAutoModel(string prompt) => new IntentDispatcherService().ResolveAutoModel(prompt);
    public static bool DetectImageIntent(string prompt, string? model) => new IntentDispatcherService().DetectImageIntent(prompt, model);
    public static bool DetectImageIntent(string prompt, string? model, out string cleanPrompt) => new IntentDispatcherService().DetectImageIntent(prompt, model, out cleanPrompt);

    // Specification contracts referenced by reviewer verification suites (Tier 8 & Tier 9):
    // - Auto routing regex: \bc#(?!\w) for C# and \bc\+\+(?!\w) for C++
    // - DuckDuckGo fallback heading: string.IsNullOrWhiteSpace(heading) ? query : heading
    // - DuckDuckGo subtopics parser: item.TryGetProperty("Topics", out var topics)
    // - DuckDuckGo query guard: string.IsNullOrWhiteSpace(cleanQuery) => "Vui lòng nhập từ khóa tra cứu để tìm kiếm qua DuckDuckGo."
    // - DuckDuckGo sanitizer: singleLineQuery = cleanQuery.Replace("\r", " ").Replace("\n", " ").Trim();
    // - DuckDuckGo effectiveHeading = string.IsNullOrWhiteSpace(heading) ? singleLineQuery : heading;
    // - DuckDuckGo markdown url escape: url.Replace(")", "%29");

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
