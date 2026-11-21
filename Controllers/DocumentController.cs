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

    [HttpGet("/Document/TrainedModelStatus")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> TrainedModelStatus(CancellationToken cancellationToken)
    {
        var (online, models) = await _ollama.GetLocalModelsAsync(cancellationToken);
        var hasCowAi = models.Any(m => m.StartsWith("cow-ai", StringComparison.OrdinalIgnoreCase));
        var hasCoder = models.Any(m => m.StartsWith("qwen2.5-coder", StringComparison.OrdinalIgnoreCase));
        return Json(new
        {
            online,
            hasTrainedModel = hasCowAi,
            hasCoderModel = hasCoder,
            defaultModel = "cow-ai-local",
            trainedModelName = "cow-ai:latest",
            availableModels = models
        });
    }

    [HttpPost("/Document/ExtractFile")]
    [RequestSizeLimit(DocumentTextExtractor.MaxBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = DocumentTextExtractor.MaxBytes)]
    public async Task<IActionResult> ExtractFile([FromForm] IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "Vui lòng chọn tệp tài liệu cần trích xuất." });
        }
        if (file.Length > DocumentTextExtractor.MaxBytes)
        {
            return BadRequest(new { error = "Tệp tài liệu vượt quá giới hạn 100 MB." });
        }
        if (!DocumentTextExtractor.IsAllowed(file.FileName))
        {
            return BadRequest(new { error = "Chỉ hỗ trợ định dạng .pdf, .docx, .pptx, .xlsx, .csv, .html, .epub, .ipynb, .json, .xml, .txt hoặc .md." });
        }

        var (content, warning) = await DocumentTextExtractor.ExtractAsync(file, _logger, HttpContext.RequestAborted);
        return Ok(new
        {
            success = true,
            fileName = Path.GetFileName(file.FileName),
            content,
            warning
        });
    }

    [HttpPost]
    public async Task<IActionResult> StreamSummary(string content, string? length, string? model = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return BadRequest(new { error = "Nội dung tóm tắt không được để trống." });
        }

        // Tải prompt từ file ngoài DocumentSummary.json qua PromptService
        var prompt = _promptService.GetSummaryPrompt(content, length);
        var targetModel = string.IsNullOrWhiteSpace(model) ? "cow-ai-local" : model.Trim();

        return await StreamInternal(prompt, targetModel, cancellationToken);
    }

    [HttpPost]
    public async Task<IActionResult> StreamCodeGen(string prompt, string language, string? standards, string? model = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return BadRequest(new { error = "Yêu cầu lập trình không được để trống." });
        }

        // Tải prompt từ file ngoài CodeGeneration.txt qua PromptService
        var fullPrompt = _promptService.GetCodeGenPrompt(prompt, language, standards);
        var targetModel = string.IsNullOrWhiteSpace(model) ? "cow-ai-local" : model.Trim();

        return await StreamInternal(fullPrompt, targetModel, cancellationToken);
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
            // Ưu tiên trực tiếp model AI đã train cục bộ (cow-ai:latest, qwen2.5-coder:7b, qwen2.5:7b) khi người dùng chọn hoặc mặc định
            var stream = (OllamaService.IsOllamaModel(model) || !_openRouter.HasValidApiKey())
                ? await _ollama.StreamChatAsync(prompt, model, cancellationToken: cancellationToken)
                : await _openRouter.StreamChatAsync(prompt, model, cancellationToken: cancellationToken);

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
