using Microsoft.AspNetCore.Mvc;
using COW_AI.Services;

namespace COW_AI.Controllers;

public class ImageController : Controller
{
    private readonly PollinationsService _pollinations;
    private readonly PromptService _promptService;
    private readonly ILogger<ImageController>? _logger;

    public ImageController(
        PollinationsService pollinations, 
        PromptService promptService,
        ILogger<ImageController>? logger = null)
    {
        _pollinations = pollinations;
        _promptService = promptService;
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
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
            _logger?.LogError(ex, "Lỗi khi tạo ảnh trong ImageController cho prompt: {Prompt}", prompt);
            return StatusCode(500, new { error = "Hệ thống tạo ảnh hiện đang bận. Vui lòng thử lại sau ít phút." });
        }
    }
}
