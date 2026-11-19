using Microsoft.AspNetCore.Mvc;
using COW_AI.Services;

namespace COW_AI.Controllers;

public class DocumentController : Controller
{
    private readonly OpenRouterService _openRouter;
    private readonly OllamaService _ollama;
    private readonly PromptService _promptService;
    private readonly ILogger<DocumentController>? _logger;

    public DocumentController(
        OpenRouterService openRouter,
        OllamaService ollama,
        PromptService promptService,
        ILogger<DocumentController>? logger = null)
    {
        _openRouter = openRouter;
        _ollama = ollama;
        _promptService = promptService;
        _logger = logger;
    }

    public IActionResult Summary() => View();

    public IActionResult CodeGen() => View();

    [HttpPost]
    public async Task<IActionResult> StreamSummary(string content, string? length, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return BadRequest(new { error = "Nội dung tóm tắt không được để trống." });
        }

        // Tải prompt từ file ngoài DocumentSummary.json qua PromptService
        var prompt = _promptService.GetSummaryPrompt(content, length);

        return await StreamInternal(prompt, "llama3", cancellationToken);
    }

    [HttpPost]
    public async Task<IActionResult> StreamCodeGen(string prompt, string language, string? standards, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return BadRequest(new { error = "Yêu cầu lập trình không được để trống." });
        }

        // Tải prompt từ file ngoài CodeGeneration.txt qua PromptService
        var fullPrompt = _promptService.GetCodeGenPrompt(prompt, language, standards);

        return await StreamInternal(fullPrompt, "deepseek", cancellationToken);
    }

    private async Task<IActionResult> StreamInternal(string prompt, string model, CancellationToken cancellationToken = default)
    {
        Response.Headers["Content-Type"] = "text/event-stream; charset=utf-8";
        Response.Headers["Cache-Control"] = "no-cache, no-transform";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        bool doneSent = false;
        try
        {
            var stream = _openRouter.HasValidApiKey()
                ? await _openRouter.StreamChatAsync(prompt, model, cancellationToken: cancellationToken)
                : await _ollama.StreamChatAsync(prompt, "cow-ai-local", cancellationToken: cancellationToken);

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
            }

            if (!doneSent && !cancellationToken.IsCancellationRequested)
            {
                await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Client ngắt kết nối chủ động
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger?.LogError(ex, "Lỗi xảy ra trong quá trình StreamInternal Document cho model: {Model}", model);
            var friendlyMsg = "Hệ thống AI hiện đang bận. Vui lòng thử lại sau ít phút.";
            var errorPayload = System.Text.Json.JsonSerializer.Serialize(new
            {
                error = new { message = friendlyMsg },
                choices = new[]
                {
                    new
                    {
                        delta = new
                        {
                            content = $"\n\n[ERROR] {friendlyMsg}\n\n"
                        }
                    }
                }
            }, new System.Text.Json.JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            try
            {
                await Response.WriteAsync($"data: {errorPayload}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
            catch
            {
            }
        }

        return new EmptyResult();
    }
}
