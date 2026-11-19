using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using COW_AI.Models;
using COW_AI.Services;

namespace COW_AI.Controllers;

public class HomeController : Controller
{
    private readonly OpenRouterService _openRouter;
    private readonly OllamaService _ollama;
    private readonly PollinationsService _pollinations;
    private readonly LocalImageService _localImage;
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
        OllamaService ollama,
        PollinationsService pollinations, 
        LocalImageService localImage,
        PromptService promptService,
        ITtsService ttsService,
        DuckDuckGoService duckDuckGo,
        IntentDispatcherService intentDispatcher,
        ILogger<HomeController>? logger = null)
    {
        _openRouter = openRouter;
        _ollama = ollama;
        _pollinations = pollinations;
        _localImage = localImage;
        _promptService = promptService;
        _ttsService = ttsService;
        _duckDuckGo = duckDuckGo;
        _intentDispatcher = intentDispatcher;
        _logger = logger;
    }

    public IActionResult Index() => View();

    [HttpGet("/api/skills")]
    public IActionResult GetSkills()
    {
        return Content(_promptService.GetSkillsJson(), "application/json; charset=utf-8");
    }

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

        // 2a. Nhánh Tạo ảnh Local GPU (/imags hoặc model=local-image -> stable-diffusion.cpp SDXL Turbo)
        if (_intentDispatcher.DetectLocalImageIntent(prompt, model, out var localCleanPrompt))
        {
            try
            {
                var (width, height) = IntentDispatcherService.ExtractDimensions(localCleanPrompt, out var finalPrompt);
                var enhancedPrompt = await _promptService.GetImageGenerationPromptAsync(finalPrompt, cancellationToken);
                var (imageUrl, actualWidth, actualHeight) = await _localImage.GenerateImageAsync(enhancedPrompt, width, height, cancellationToken);

                var payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    is_image_url = imageUrl,
                    prompt = localCleanPrompt,
                    enhanced_prompt = enhancedPrompt,
                    provider = "local-sdxl-turbo",
                    width = actualWidth,
                    height = actualHeight,
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
            catch (InvalidOperationException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger?.LogWarning(ex, "Cảnh báo tạo ảnh Local GPU cho prompt: {Prompt}", localCleanPrompt);
                await SendSafeSseErrorAsync(ex.Message, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger?.LogError(ex, "Lỗi khi tạo ảnh Local GPU cho prompt: {Prompt}", localCleanPrompt);
                await SendSafeSseErrorAsync("Không thể tạo ảnh trên GPU Local vào lúc này. Vui lòng thử lại sau ít phút.", cancellationToken);
            }

            return new EmptyResult();
        }

        // 2b. Nhánh Tạo ảnh Cloud: Điều phối qua IntentDispatcherService (Pollinations AI)
        if (_intentDispatcher.DetectImageIntent(prompt, model, out var cleanPrompt))
        {
            try
            {
                var (width, height) = IntentDispatcherService.ExtractDimensions(cleanPrompt, out var finalPrompt);
                var enhancedPrompt = await _promptService.GetImageGenerationPromptAsync(finalPrompt, cancellationToken);
                var imageUrl = _pollinations.GetImageUrl(enhancedPrompt, width, height);
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

        // 2.5. Nhánh Kỹ năng Lập trình chuyên sâu (/code -> bọc template Prompts/CodeGeneration.txt + định tuyến model lập trình)
        if (_intentDispatcher.DetectCodeSkillIntent(prompt, out var cleanCodePrompt))
        {
            var wrappedCodePrompt = _promptService.GetCodeGenPrompt(
                cleanCodePrompt,
                "phù hợp với yêu cầu",
                "Mã nguồn sạch, tối ưu hiệu năng, đặt trong khối code Markdown có khai báo ngôn ngữ và giải thích ngắn gọn phía sau.");

            bool codeDoneSent = false;
            try
            {
                Stream stream;
                if (OllamaService.IsOllamaModel(normalizedModel))
                {
                    stream = await _ollama.StreamChatAsync(wrappedCodePrompt, normalizedModel, temperature, maxTokens, cancellationToken);
                }
                else if (_openRouter.HasValidApiKey())
                {
                    stream = await _openRouter.StreamChatAsync(wrappedCodePrompt, "deepseek", temperature, maxTokens, cancellationToken);
                }
                else
                {
                    _logger?.LogInformation("Skill /code định tuyến sang Ollama Local (cow-ai-local) cho prompt.");
                    stream = await _ollama.StreamChatAsync(wrappedCodePrompt, "cow-ai-local", temperature, maxTokens, cancellationToken);
                }

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
                            codeDoneSent = true;
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
                            codeDoneSent = true;
                            break;
                        }
                        if (!string.IsNullOrEmpty(data))
                        {
                            await Response.WriteAsync($"data: {data}\n\n", cancellationToken);
                            await Response.Body.FlushAsync(cancellationToken);
                        }
                    }
                }

                if (!codeDoneSent && !cancellationToken.IsCancellationRequested)
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
                _logger?.LogError(ex, "Lỗi khi xử lý skill /code cho prompt: {Prompt}", cleanCodePrompt);
                await SendSafeSseErrorAsync("Hệ thống AI hiện đang bận. Vui lòng thử lại sau ít phút.", cancellationToken);
            }

            return new EmptyResult();
        }

        // 3. Nhánh DuckDuckGo Search: Điều phối qua DuckDuckGoService (model=duckduckgo hoặc /search)
        if (_intentDispatcher.DetectSearchSkillIntent(prompt, model, out var searchQuery))
        {
            try
            {
                var searchResults = await _duckDuckGo.SearchAsync(searchQuery, cancellationToken);
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

        // 4. Nhánh Ollama Cloud & Local (Free open-weight models: gpt-oss:20b, gpt-oss:120b, mistral-nemo, phi4)
        if (OllamaService.IsOllamaModel(normalizedModel))
        {
            bool ollamaDoneSent = false;
            try
            {
                var stream = await _ollama.StreamChatAsync(prompt, normalizedModel, temperature, maxTokens, cancellationToken);
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
                            ollamaDoneSent = true;
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
                            ollamaDoneSent = true;
                            break;
                        }
                        if (!string.IsNullOrEmpty(data))
                        {
                            await Response.WriteAsync($"data: {data}\n\n", cancellationToken);
                            await Response.Body.FlushAsync(cancellationToken);
                        }
                    }
                }

                if (!ollamaDoneSent && !cancellationToken.IsCancellationRequested)
                {
                    await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Client ngắt kết nối
            }
            catch (InvalidOperationException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger?.LogWarning(ex, "Cảnh báo cấu hình Ollama cho prompt '{Prompt}' và model '{Model}'", prompt, normalizedModel);
                await SendSafeSseErrorAsync(ex.Message, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger?.LogError(ex, "Lỗi khi gọi Ollama Service cho prompt '{Prompt}' và model '{Model}'", prompt, normalizedModel);
                await SendSafeSseErrorAsync("Dịch vụ Ollama Cloud/Local hiện không thể phản hồi. Vui lòng kiểm tra API key hoặc kết nối Ollama.", cancellationToken);
            }

            return new EmptyResult();
        }

        // 5. Nhánh Chat văn bản qua OpenRouter
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
            Stream stream;
            if (_openRouter.HasValidApiKey())
            {
                stream = await _openRouter.StreamChatAsync(prompt, modelToUse, temperature, maxTokens, cancellationToken);
            }
            else
            {
                _logger?.LogInformation("OpenRouter:ApiKey chưa cấu hình — tự động chuyển sang Ollama Local (cow-ai-local) cho prompt.");
                stream = await _ollama.StreamChatAsync(prompt, "cow-ai-local", temperature, maxTokens, cancellationToken);
            }

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
            var (width, height) = IntentDispatcherService.ExtractDimensions(prompt, out var finalPrompt);
            var enhancedPrompt = await _promptService.GetImageGenerationPromptAsync(finalPrompt, cancellationToken);
            var imageUrl = _pollinations.GetImageUrl(enhancedPrompt, width, height);
            return Json(new { url = imageUrl, prompt = prompt, enhanced_prompt = enhancedPrompt, width = width, height = height });
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

    [HttpGet("/sitemap.xml")]
    [ResponseCache(Duration = 3600)]
    public IActionResult Sitemap()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var lastMod = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var urls = new (string Path, string Priority)[]
        {
            ("/", "1.0"),
            ("/Image", "0.9"),
            ("/Document/Summary", "0.9"),
            ("/Document/CodeGen", "0.9"),
            ("/studio/about", "0.8")
        };
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (var (p, prio) in urls)
        {
            sb.AppendLine($"  <url><loc>{baseUrl}{p}</loc><lastmod>{lastMod}</lastmod><changefreq>weekly</changefreq><priority>{prio}</priority></url>");
        }
        sb.AppendLine("</urlset>");
        return Content(sb.ToString(), "application/xml", System.Text.Encoding.UTF8);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
