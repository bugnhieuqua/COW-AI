using Microsoft.AspNetCore.Mvc;
using COW_AI.Services;

namespace COW_AI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TtsController : ControllerBase
{
    private const int MaxTextLength = 12000;

    private readonly EdgeTtsService _ttsService;
    private readonly ILogger<TtsController>? _logger;

    public TtsController(EdgeTtsService ttsService, ILogger<TtsController>? logger = null)
    {
        _ttsService = ttsService;
        _logger = logger;
    }

    [HttpGet("speak")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Speak(
        [FromQuery] string? text,
        [FromQuery] string? voice,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return BadRequest(new { error = "Văn bản không được để trống." });
        }

        if (text.Length > MaxTextLength)
        {
            return BadRequest(new { error = "Văn bản quá dài để đọc. Vui lòng chọn đoạn ngắn hơn." });
        }

        try
        {
            Response.ContentType = "audio/mpeg";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";

            await _ttsService.StreamToResponseAsync(text, Response.Body, voice, cancellationToken);

            return new EmptyResult();
        }
        catch (OperationCanceledException)
        {
            // Người dùng hủy phát hoặc đóng kết nối
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Lỗi Edge TTS khi tạo âm thanh cho voice: {Voice}", voice ?? "default");

            if (Response.HasStarted)
            {
                // Header/audio đã gửi đi, không thể trả JSON lỗi nữa
                return new EmptyResult();
            }

            Response.Clear();
            return StatusCode(500, new { error = "Không thể tạo âm thanh. Vui lòng thử lại sau." });
        }
    }
}
