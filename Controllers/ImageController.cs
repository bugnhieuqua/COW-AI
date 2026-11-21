using Microsoft.AspNetCore.Mvc;
using COW_AI.Services;

namespace COW_AI.Controllers;

/// <summary>
/// Trang "Tạo ảnh AI" — chỉ dùng model ảnh cục bộ (SDXL Turbo qua sd-server).
/// Không gọi dịch vụ đám mây nào: ảnh được giữ trong RAM và phục vụ qua /Image/LocalOutput/{id}.
/// </summary>
public class ImageController : Controller
{
    private readonly LocalImageService _localImage;
    private readonly PromptService _promptService;
    private readonly ImageQuotaService _quotaService;
    private readonly ILogger<ImageController>? _logger;

    public ImageController(
        LocalImageService localImage,
        PromptService promptService,
        ImageQuotaService quotaService,
        ILogger<ImageController>? logger = null)
    {
        _localImage = localImage;
        _promptService = promptService;
        _quotaService = quotaService;
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult QuotaStatus()
    {
        return Json(_quotaService.GetQuota(HttpContext));
    }

    [HttpGet]
    public async Task<IActionResult> LocalStatus(CancellationToken cancellationToken)
    {
        var online = await _localImage.IsServerHealthyAsync(cancellationToken);
        return Json(new
        {
            online,
            engine = "SDXL Turbo",
            provider = "local-sdxl-turbo",
            steps = _localImage.DefaultSteps,
            cfg_scale = _localImage.DefaultCfgScale,
            sampling = _localImage.SamplingMethod,
            auto_start = _localImage.AutoStart
        });
    }

    [HttpGet]
    public IActionResult LocalOutput(string id)
    {
        var entry = _localImage.GetCachedImage(id);
        if (entry == null)
        {
            return NotFound();
        }

        Response.Headers["Cache-Control"] = "private, max-age=600";
        return File(entry.Bytes, entry.ContentType);
    }

    [HttpPost]
    public async Task<IActionResult> GenerateImage(string prompt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return BadRequest(new { error = "Mô tả ảnh không được để trống." });
        }

        if (!_quotaService.TryConsumeQuota(HttpContext, "image_page", out var quotaAfter, out var quotaErr))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = quotaErr, quota = quotaAfter });
        }

        bool consumed = true;
        try
        {
            var (width, height) = IntentDispatcherService.ExtractDimensions(prompt, out var finalPrompt);
            var enhancedPrompt = await _promptService.GetImageGenerationPromptAsync(finalPrompt, cancellationToken);

            if (string.IsNullOrWhiteSpace(enhancedPrompt))
            {
                _quotaService.RefundQuota(HttpContext, "image_page");
                consumed = false;
                return BadRequest(new { error = "Không đọc được nội dung mô tả. Hãy thử diễn đạt lại bằng một câu đầy đủ." });
            }

            var (imageUrl, actualWidth, actualHeight) =
                await _localImage.GenerateImageAsync(enhancedPrompt, width, height, cancellationToken);

            return Json(new
            {
                url = imageUrl,
                prompt,
                enhanced_prompt = enhancedPrompt,
                width = actualWidth,
                height = actualHeight,
                provider = "local-sdxl-turbo",
                engine = "SDXL Turbo",
                steps = _localImage.DefaultSteps,
                quota = _quotaService.GetQuota(HttpContext)
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (consumed) _quotaService.RefundQuota(HttpContext, "image_page");
            _logger?.LogInformation("Client huỷ yêu cầu tạo ảnh Local.");
            return StatusCode(499, new { error = "Yêu cầu đã bị huỷ." });
        }
        catch (Exception ex)
        {
            if (consumed) _quotaService.RefundQuota(HttpContext, "image_page");
            _logger?.LogError(ex, "Lỗi khi tạo ảnh Local cho prompt: {Prompt}", prompt);
            return StatusCode(503, new { error = BuildLocalErrorMessage(ex) });
        }
    }

    /// <summary>
    /// Dịch lỗi kỹ thuật của sd-server thành thông báo có thể hành động được cho người dùng.
    /// </summary>
    private string BuildLocalErrorMessage(Exception ex)
    {
        var message = ex.Message ?? string.Empty;

        if (message.Contains("Không tìm thấy sd-server", StringComparison.OrdinalIgnoreCase))
        {
            return $"Thiếu tệp sd-server tại “{_localImage.ExecutablePath}”. Cài lại stable-diffusion.cpp trong D:\\LocalImageAI rồi thử lại.";
        }

        if (message.Contains("Không tìm thấy model", StringComparison.OrdinalIgnoreCase))
        {
            return $"Thiếu model SDXL Turbo tại “{_localImage.ModelPath}”. Tải model vào D:\\LocalImageAI\\models rồi thử lại.";
        }

        if (message.Contains("chưa được khởi động", StringComparison.OrdinalIgnoreCase))
        {
            return $"Dịch vụ ảnh cục bộ chưa chạy. Mở D:\\LocalImageAI\\start-sd-server.cmd rồi thử lại.";
        }

        if (message.Contains("thời gian chờ khởi động", StringComparison.OrdinalIgnoreCase))
        {
            return "Máy chủ ảnh cục bộ khởi động quá lâu (quá 90 giây). Lần đầu nạp SDXL Turbo rất nặng — chờ một lát rồi bấm tạo lại.";
        }

        if (message.Contains("thoát đột ngột", StringComparison.OrdinalIgnoreCase))
        {
            return "sd-server vừa dừng đột ngột, thường do hết VRAM. Đóng ứng dụng đang dùng GPU (game, trình duyệt nặng) rồi thử lại.";
        }

        if (message.Contains("HTTP", StringComparison.OrdinalIgnoreCase))
        {
            return "GPU cục bộ từ chối yêu cầu render (có thể hết VRAM). Đóng bớt ứng dụng dùng GPU rồi thử lại.";
        }

        if (message.Contains("không trả về dữ liệu hình ảnh", StringComparison.OrdinalIgnoreCase))
        {
            return "sd-server chạy nhưng không trả về ảnh. Kiểm tra cửa sổ console của sd-server để xem lỗi chi tiết.";
        }

        return "Không tạo được ảnh trên GPU cục bộ. Kiểm tra sd-server đang chạy rồi thử lại.";
    }
}
