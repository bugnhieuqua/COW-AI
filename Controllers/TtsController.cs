using Microsoft.AspNetCore.Mvc;
using COW_AI.Services;

namespace COW_AI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TtsController : ControllerBase
{
    private const int MaxTextLength = 12000;

    private readonly IVieNeuTtsService _vieNeuTts;
    private readonly ILogger<TtsController>? _logger;

    public TtsController(IVieNeuTtsService vieNeuTts, ILogger<TtsController>? logger = null)
    {
        _vieNeuTts = vieNeuTts;
        _logger = logger;
    }

    [HttpGet("status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Status([FromQuery] bool warmup = false)
    {
        if (warmup)
        {
            _vieNeuTts.TriggerWarmUp();
        }
        return Ok(_vieNeuTts.GetServerStatus());
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
            var (success, error, audioBytes, contentType) = await _vieNeuTts.SynthesizeWavBytesAsync(text, voice ?? "Hải Đăng", cancellationToken);
            if (!success || audioBytes.Length == 0)
            {
                return StatusCode(503, new
                {
                    error = error ?? "Server âm thanh VieNeu-TTS đang load hoặc chưa sẵn sàng. Vui lòng đợi giây lát và thử lại.",
                    status = _vieNeuTts.GetServerStatus()
                });
            }

            Response.Headers.CacheControl = "no-cache";
            return File(audioBytes, contentType);
        }
        catch (OperationCanceledException)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Lỗi VieNeu-TTS khi tạo âm thanh cho voice: {Voice}", voice ?? "Hải Đăng");
            return StatusCode(500, new { error = "Không thể tạo âm thanh VieNeu-TTS. Vui lòng thử lại sau." });
        }
    }
}
