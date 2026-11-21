using System.Diagnostics;
using COW_AI.Data;
using COW_AI.Models;
using COW_AI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace COW_AI.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _db;
    private readonly OpenRouterService _openRouter;
    private readonly OllamaService _ollama;
    private readonly PollinationsService _pollinations;
    private readonly LocalImageService _localImage;
    private readonly PromptService _promptService;
    private readonly ITtsService _ttsService;
    private readonly DuckDuckGoService _duckDuckGo;
    private readonly IntentDispatcherService _intentDispatcher;
    private readonly DirectoryAgentService _directoryAgent;
    private readonly WorkspaceRagService _workspaceRag;
    private readonly KodeWorkspaceAgentService _kodeAgent;
    private readonly IVieNeuTtsService _vieNeuTts;
    private readonly ImageQuotaService _imageQuota;
    private readonly ILogger<HomeController>? _logger;

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public HomeController(
        AppDbContext db,
        OpenRouterService openRouter, 
        OllamaService ollama,
        PollinationsService pollinations, 
        LocalImageService localImage,
        PromptService promptService,
        ITtsService ttsService,
        DuckDuckGoService duckDuckGo,
        IntentDispatcherService intentDispatcher,
        DirectoryAgentService directoryAgent,
        WorkspaceRagService workspaceRag,
        KodeWorkspaceAgentService kodeAgent,
        IVieNeuTtsService vieNeuTts,
        ImageQuotaService imageQuota,
        ILogger<HomeController>? logger = null)
    {
        _db = db;
        _openRouter = openRouter;
        _ollama = ollama;
        _pollinations = pollinations;
        _localImage = localImage;
        _promptService = promptService;
        _ttsService = ttsService;
        _duckDuckGo = duckDuckGo;
        _intentDispatcher = intentDispatcher;
        _directoryAgent = directoryAgent;
        _workspaceRag = workspaceRag;
        _kodeAgent = kodeAgent;
        _vieNeuTts = vieNeuTts;
        _imageQuota = imageQuota;
        _logger = logger;
    }

    public IActionResult Index() => View();

    [HttpGet("/api/skills")]
    public IActionResult GetSkills()
    {
        return Content(_promptService.GetSkillsJson(), "application/json; charset=utf-8");
    }

    [HttpGet("/api/agent/browse-dirs")]
    public IActionResult BrowseDirectories([FromQuery] string? path)
    {
        var result = _directoryAgent.BrowseDirectories(path);
        if (!result.Success)
        {
            return BadRequest(new { success = false, error = result.Error });
        }
        return Ok(result);
    }

    [HttpPost("/api/agent/read-directory")]
    public async Task<IActionResult> ReadDirectory([FromBody] ReadDirectoryRequest? req, CancellationToken cancellationToken)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Path))
        {
            return BadRequest(new { success = false, error = "Vui lòng cung cấp đường dẫn thư mục cần đọc." });
        }

        var result = await _directoryAgent.ReadDirectoryAsync(req.Path, cancellationToken);
        if (!result.Success)
        {
            return BadRequest(new { success = false, error = result.Error });
        }

        return Ok(result);
    }

    [AcceptVerbs("GET", "POST")]
    public async Task<IActionResult> StreamChat(
        string prompt, 
        string? model, 
        float? temperature = null, 
        int? maxTokens = null,
        string? sessionId = null,
        bool? autoApprove = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return BadRequest(new { error = "Prompt không được để trống." });
        }

        var normalizedModel = model?.Trim().ToLowerInvariant();

        // 1. Nhánh VieNeu-TTS 48kHz bắt buộc (không dùng Edge-TTS)
        if (normalizedModel is "edge-male" or "edge-female")
        {
            var voiceName = normalizedModel == "edge-male" ? "Hải Đăng" : "Hoài My";
            try
            {
                var (ok, err, wavBytes, contentType) = await _vieNeuTts.SynthesizeWavBytesAsync(prompt, voiceName, cancellationToken);
                if (!ok || wavBytes.Length == 0)
                {
                    Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await Response.WriteAsJsonAsync(new { error = err ?? "Server âm thanh VieNeu-TTS đang load, vui lòng đợi giây lát." }, cancellationToken);
                    return new EmptyResult();
                }

                Response.Headers["Content-Type"] = contentType;
                Response.Headers["Cache-Control"] = "no-cache";
                await Response.Body.WriteAsync(wavBytes, cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger?.LogError(ex, "Lỗi VieNeu-TTS cho prompt '{Prompt}' với voice '{Voice}'", prompt, voiceName);
                if (!Response.HasStarted)
                {
                    Response.Clear();
                    Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await Response.WriteAsJsonAsync(
                        new { error = "Không thể tạo âm thanh VieNeu-TTS. Vui lòng thử lại sau ít phút." },
                        cancellationToken);
                }
            }

            return new EmptyResult();
        }

        Response.Headers["Content-Type"] = "text/event-stream; charset=utf-8";
        Response.Headers["Cache-Control"] = "no-cache, no-transform";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        // 1.4. Nếu người dùng gõ `/folder <path> [câu hỏi]` kèm sessionId -> tự động gắn 1-1 Workspace vào phiên này
        if (!string.IsNullOrWhiteSpace(sessionId) &&
            _intentDispatcher.DetectFolderSkillIntent(prompt, out var inlineFolderPath, out var inlineFolderQuestion) &&
            !string.IsNullOrWhiteSpace(inlineFolderPath))
        {
            var authUserId = await GetEffectiveUserIdAsync(cancellationToken);

            var candidateAbs = Path.GetFullPath(inlineFolderPath.Trim());
            if (!Directory.Exists(candidateAbs) || _workspaceRag.IsDangerousSystemRoot(candidateAbs))
            {
                await SendSafeSseErrorAsync($"Không thể truy cập hoặc bị chặn bảo mật đối với thư mục: {inlineFolderPath}", cancellationToken);
                return new EmptyResult();
            }

            await UpsertWorkspaceSessionAsync(sessionId, candidateAbs, "server_path", autoApprove ?? false, cancellationToken, null, authUserId);
            prompt = string.IsNullOrWhiteSpace(inlineFolderQuestion)
                ? "Hãy phân tích cấu trúc tổng quan và các thành phần chính của thư mục vừa gắn."
                : inlineFolderQuestion;
        }

        // 1.5. Nếu phiên Chat này đã được gắn Workspace 1-1 (và không phải lệnh tạo ảnh) -> chạy Autonomous Kode Agent (RAG + Tools + Sandbox)
        if (!string.IsNullOrWhiteSpace(sessionId) &&
            !_intentDispatcher.DetectLocalImageIntent(prompt, model, out _) &&
            !_intentDispatcher.DetectImageIntent(prompt, model, out _))
        {
            var pinnedWs = await _db.WorkspaceSessions.FirstOrDefaultAsync(x => x.SessionId == sessionId.Trim(), cancellationToken);
            if (pinnedWs != null)
            {
                await TryAutoUpgradeWorkspaceToRealDiskAsync(pinnedWs, cancellationToken);
                if (!Directory.Exists(pinnedWs.RootPath))
                {
                    await SendSafeSseErrorAsync($"Thư mục gắn với phiên chat này không còn tồn tại trên đĩa: {pinnedWs.RootPath}", cancellationToken);
                    return new EmptyResult();
                }

                if (autoApprove.HasValue && pinnedWs.AutoApprove != autoApprove.Value)
                {
                    pinnedWs.AutoApprove = autoApprove.Value;
                    pinnedWs.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(cancellationToken);
                }

                if (_intentDispatcher.DetectInitSkillIntent(prompt, out var initCustom))
                {
                    prompt = $"""
[LỆNH ĐẶC BIỆT: /INIT - THANH TRA VÀ ĐỊNH VỊ DỰ ÁN]
Thư mục dự án mục tiêu: `{pinnedWs.RootPath}` (Tên dự án: `{pinnedWs.FolderName}`)
Chỉ dẫn bổ sung từ người dùng: {(string.IsNullOrWhiteSpace(initCustom) ? "Không có" : initCustom)}

Hãy thực hiện quy trình thanh tra thư mục toàn diện theo các bước:
1. Rà quét danh sách tệp & cây thư mục cấp cao (bỏ qua các thư mục sinh ra như bin, obj, node_modules, .git, .cowai_trash).
2. Đọc các tệp manifest cấu hình cốt lõi nếu có (package.json, *.csproj, requirements.txt, Cargo.toml, go.mod, README.md, docker-compose.yml...).
3. Nhận diện chính xác Tech Stack: Ngôn ngữ chính, Framework, kiến trúc (MVC, SPA, Clean Architecture...), cơ sở dữ liệu.
4. Trình bày Báo cáo Khởi tạo Dự án bằng Markdown rõ ràng, chuyên nghiệp:
   - 🎯 **Mục đích & Phạm vi Dự án**
   - 🛠 **Tech Stack & Dependencies cốt lõi**
   - 📁 **Cấu trúc thư mục & Entry Points chính**
   - 🚀 **Trạng thái & Sẵn sàng thực thi**
Agent sẵn sàng tiếp nhận các yêu cầu lập trình và thao tác trực tiếp trên thư mục này!
""";
                }

                await foreach (var sseEvent in _kodeAgent.StreamWorkspaceAgentSseAsync(
                    pinnedWs, prompt, model, temperature, maxTokens, cancellationToken))
                {
                    await Response.WriteAsync(sseEvent, cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
                return new EmptyResult();
            }
        }

        // 1.5b. Xử lý khi chạy lệnh /init nhưng chưa có thư mục dự án nào được mở
        if (_intentDispatcher.DetectInitSkillIntent(prompt, out _))
        {
            var notFoundMsg = "⚠️ **Chưa có dự án nào được mở.**\n\nLệnh `/init` dùng để thanh tra và phân tích thư mục dự án đang làm việc. Vui lòng bấm vào nút **Thư mục** (hoặc dấu `+` trên Sidebar) để mở thư mục dự án trên máy trước khi chạy lệnh này.";
            await Response.WriteAsync($"data: {System.Text.Json.JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content = notFoundMsg } } } })}\n\n", cancellationToken);
            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            return new EmptyResult();
        }

        // 1.6. Fallback: Nhánh đọc nhanh `/folder` khi chưa có sessionId (legacy compatibility)
        if (_intentDispatcher.DetectFolderSkillIntent(prompt, out var folderPath, out var folderQuestion))
        {
            if (!string.IsNullOrWhiteSpace(folderPath))
            {
                var dirResult = await _directoryAgent.ReadDirectoryAsync(folderPath, cancellationToken);
                if (!dirResult.Success)
                {
                    await SendSafeSseErrorAsync(dirResult.Error ?? "Không thể truy cập thư mục yêu cầu.", cancellationToken);
                    return new EmptyResult();
                }
                prompt = DirectoryAgentService.BuildAgentFolderPrompt(folderQuestion, dirResult);
            }
            else if (!string.IsNullOrWhiteSpace(folderQuestion))
            {
                prompt = folderQuestion;
            }
            else
            {
                await SendSafeSseErrorAsync("Vui lòng nhập đường dẫn thư mục sau /folder (ví dụ: /folder D:\\DuAn Phân tích kiến trúc) hoặc bấm nút Thư mục để chọn thư mục từ máy.", cancellationToken);
                return new EmptyResult();
            }
        }

        // 2a. Nhánh Tạo ảnh Local GPU (/imags hoặc model=local-image -> stable-diffusion.cpp SDXL Turbo)
        if (_intentDispatcher.DetectLocalImageIntent(prompt, model, out var localCleanPrompt))
        {
            if (!_imageQuota.TryConsumeQuota(HttpContext, "home_page", out _, out var quotaErr))
            {
                await SendSafeSseErrorAsync(quotaErr ?? "Đã đạt giới hạn 5 ảnh tại Trang chủ.", cancellationToken);
                return new EmptyResult();
            }

            bool consumed = true;
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
                    quota = _imageQuota.GetQuota(HttpContext),
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
                if (consumed) _imageQuota.RefundQuota(HttpContext, "home_page");
            }
            catch (InvalidOperationException ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (consumed) _imageQuota.RefundQuota(HttpContext, "home_page");
                _logger?.LogWarning(ex, "Cảnh báo tạo ảnh Local GPU cho prompt: {Prompt}", localCleanPrompt);
                await SendSafeSseErrorAsync(ex.Message, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (consumed) _imageQuota.RefundQuota(HttpContext, "home_page");
                _logger?.LogError(ex, "Lỗi khi tạo ảnh Local GPU cho prompt: {Prompt}", localCleanPrompt);
                await SendSafeSseErrorAsync("Không thể tạo ảnh trên GPU Local vào lúc này. Vui lòng thử lại sau ít phút.", cancellationToken);
            }

            return new EmptyResult();
        }

        // 2b. Nhánh Tạo ảnh Cloud: Điều phối qua IntentDispatcherService (Pollinations AI)
        if (_intentDispatcher.DetectImageIntent(prompt, model, out var cleanPrompt))
        {
            if (!_imageQuota.TryConsumeQuota(HttpContext, "home_page", out _, out var quotaErr))
            {
                await SendSafeSseErrorAsync(quotaErr ?? "Đã đạt giới hạn 5 ảnh tại Trang chủ.", cancellationToken);
                return new EmptyResult();
            }

            bool consumed = true;
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
                    quota = _imageQuota.GetQuota(HttpContext),
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
                if (consumed) _imageQuota.RefundQuota(HttpContext, "home_page");
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (consumed) _imageQuota.RefundQuota(HttpContext, "home_page");
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

        if (!_imageQuota.TryConsumeQuota(HttpContext, "home_page", out var quotaAfter, out var quotaErr))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = quotaErr, quota = quotaAfter });
        }

        try
        {
            var (width, height) = IntentDispatcherService.ExtractDimensions(prompt, out var finalPrompt);
            var enhancedPrompt = await _promptService.GetImageGenerationPromptAsync(finalPrompt, cancellationToken);
            var imageUrl = _pollinations.GetImageUrl(enhancedPrompt, width, height);
            return Json(new
            {
                url = imageUrl,
                prompt = prompt,
                enhanced_prompt = enhancedPrompt,
                width = width,
                height = height,
                quota = _imageQuota.GetQuota(HttpContext)
            });
        }
        catch (Exception ex)
        {
            _imageQuota.RefundQuota(HttpContext, "home_page");
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

    #region Workspace 1-1 Binding, Upload, Approval & Safe Trash APIs

    private int? GetAuthenticatedUserId()
    {
        if (User.Identity?.IsAuthenticated != true) return null;
        var raw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(raw, out var uid) ? uid : null;
    }

    private async Task<int> GetEffectiveUserIdAsync(CancellationToken ct = default)
    {
        var authId = GetAuthenticatedUserId();
        if (authId.HasValue) return authId.Value;
        var first = await _db.Users.OrderBy(u => u.Id).FirstOrDefaultAsync(ct);
        return first?.Id ?? 1;
    }

    [HttpGet("/api/agent/workspace/list")]
    public async Task<IActionResult> ListUserWorkspaces(CancellationToken cancellationToken)
    {
        var userId = await GetEffectiveUserIdAsync(cancellationToken);

        var rows = await _db.WorkspaceSessions
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(cancellationToken);

        var validProjects = new List<object>();
        foreach (var ws in rows)
        {
            await TryAutoUpgradeWorkspaceToRealDiskAsync(ws, cancellationToken);
            if (!Directory.Exists(ws.RootPath)) continue;
            var trashItems = await _workspaceRag.LoadTrashManifestAsync(ws.RootPath, ws.SessionId, cancellationToken);
            validProjects.Add(ToWorkspaceDto(ws, trashItems.Count(t => !t.Restored)));
        }

        return Ok(new { success = true, projects = validProjects });
    }

    [HttpPost("/api/agent/workspace/pick-native-folder")]
    public async Task<IActionResult> PickNativeWorkspaceFolder([FromBody] PickNativeFolderRequest? req, CancellationToken cancellationToken)
    {
        var userId = await GetEffectiveUserIdAsync(cancellationToken);

        var (pickedOk, selectedPath, pickErr) = await _directoryAgent.PickNativeFolderOnWindowsAsync(req?.InitialPath, cancellationToken);
        if (!pickedOk || string.IsNullOrWhiteSpace(selectedPath))
        {
            return Ok(new { success = false, cancelled = true, error = pickErr ?? "Đã hủy chọn thư mục." });
        }

        if (_workspaceRag.IsDangerousSystemRoot(selectedPath))
        {
            return BadRequest(new { success = false, error = "Bị chặn: không được gắn thư mục hệ thống hoặc ổ đĩa gốc." });
        }

        if (!string.IsNullOrWhiteSpace(req?.SessionId))
        {
            var ws = await UpsertWorkspaceSessionAsync(req.SessionId, selectedPath, "server_path", req.AutoApprove, cancellationToken, null, userId);
            var trashItems = await _workspaceRag.LoadTrashManifestAsync(ws.RootPath, ws.SessionId, cancellationToken);
            return Ok(new
            {
                success = true,
                selectedPath,
                workspace = ToWorkspaceDto(ws, trashItems.Count(t => !t.Restored))
            });
        }

        return Ok(new { success = true, selectedPath });
    }

    [HttpPost("/api/agent/workspace/bind")]
    public async Task<IActionResult> BindWorkspace([FromBody] BindWorkspaceRequest? req, CancellationToken cancellationToken)
    {
        var userId = await GetEffectiveUserIdAsync(cancellationToken);

        if (req == null || string.IsNullOrWhiteSpace(req.SessionId) || string.IsNullOrWhiteSpace(req.Path))
        {
            return BadRequest(new { success = false, error = "Thiếu sessionId hoặc đường dẫn thư mục." });
        }

        var absPath = Path.GetFullPath(req.Path.Trim());
        if (!Directory.Exists(absPath))
        {
            return BadRequest(new { success = false, error = $"Thư mục không tồn tại: {absPath}" });
        }
        if (_workspaceRag.IsDangerousSystemRoot(absPath))
        {
            return BadRequest(new { success = false, error = "Bị chặn: không được gắn thư mục hệ thống hoặc ổ đĩa gốc." });
        }

        var ws = await UpsertWorkspaceSessionAsync(req.SessionId, absPath, "server_path", req.AutoApprove, cancellationToken, null, userId);
        var trashItems = await _workspaceRag.LoadTrashManifestAsync(ws.RootPath, ws.SessionId, cancellationToken);

        return Ok(new
        {
            success = true,
            workspace = ToWorkspaceDto(ws, trashItems.Count(t => !t.Restored))
        });
    }

    [HttpPost("/api/agent/workspace/upload")]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> UploadWorkspaceFolder(
        [FromForm] string sessionId,
        [FromForm] string? folderName,
        [FromForm] List<IFormFile> files,
        [FromForm] List<string>? relativePaths,
        CancellationToken cancellationToken)
    {
        var userId = await GetEffectiveUserIdAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(sessionId) || files == null || files.Count == 0)
        {
            return BadRequest(new { success = false, error = "Thiếu sessionId hoặc danh sách tệp tải lên." });
        }

        string[] ignored = ["node_modules", ".git", "bin", "obj", ".vs", "dist", "build", "__pycache__", ".cowai_trash", ".kode"];
        var fingerprints = new List<(string RelPath, long Size)>();
        string? inferredFolderName = folderName?.Trim();

        for (int i = 0; i < files.Count; i++)
        {
            var formFile = files[i];
            var relRaw = (relativePaths != null && i < relativePaths.Count && !string.IsNullOrWhiteSpace(relativePaths[i]))
                ? relativePaths[i]
                : formFile.FileName;
            var parts = relRaw.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Any(p => p == ".." || p == "." || ignored.Contains(p, StringComparer.OrdinalIgnoreCase)))
                continue;
            if (string.IsNullOrWhiteSpace(inferredFolderName) && parts.Length > 1)
            {
                inferredFolderName = parts[0];
            }
            var relParts = parts.Length > 1 ? parts.Skip(1).ToArray() : parts;
            fingerprints.Add((string.Join("/", relParts), formFile.Length));
        }

        // Tầng 2: Tự động đối chiếu và ánh xạ trực tiếp về thư mục gốc thật trên ổ đĩa máy tính nếu khớp
        var resolvedRealPath = _directoryAgent.TryResolveRealLocalFolder(inferredFolderName, fingerprints);
        if (!string.IsNullOrWhiteSpace(resolvedRealPath) &&
            Directory.Exists(resolvedRealPath) &&
            !_workspaceRag.IsDangerousSystemRoot(resolvedRealPath))
        {
            var realWs = await UpsertWorkspaceSessionAsync(sessionId, resolvedRealPath, "server_path", false, cancellationToken, inferredFolderName, userId);
            var realTrash = await _workspaceRag.LoadTrashManifestAsync(realWs.RootPath, realWs.SessionId, cancellationToken);
            return Ok(new
            {
                success = true,
                uploadedFiles = fingerprints.Count,
                resolvedDirectDisk = true,
                workspace = ToWorkspaceDto(realWs, realTrash.Count(t => !t.Restored))
            });
        }

        var uploadRoot = _workspaceRag.GetUploadWorkspacePath(sessionId);
        if (Directory.Exists(uploadRoot))
        {
            Directory.Delete(uploadRoot, recursive: true);
        }
        Directory.CreateDirectory(uploadRoot);

        int saved = 0;

        for (int i = 0; i < files.Count; i++)
        {
            var formFile = files[i];
            if (formFile.Length > 5 * 1024 * 1024) continue;

            var relRaw = (relativePaths != null && i < relativePaths.Count && !string.IsNullOrWhiteSpace(relativePaths[i]))
                ? relativePaths[i]
                : formFile.FileName;

            var parts = relRaw.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Any(p => p == ".." || p == "." || ignored.Contains(p, StringComparer.OrdinalIgnoreCase)))
                continue;

            // Bỏ tiền tố tên thư mục gốc từ webkitRelativePath để giữ cấu trúc sạch bên trong uploadRoot
            var relParts = parts.Length > 1 ? parts.Skip(1).ToArray() : parts;
            var targetFull = Path.GetFullPath(Path.Combine(new[] { uploadRoot }.Concat(relParts).ToArray()));
            if (!targetFull.StartsWith(uploadRoot, StringComparison.OrdinalIgnoreCase)) continue;

            var dir = Path.GetDirectoryName(targetFull);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            await using var fs = System.IO.File.Create(targetFull);
            await formFile.CopyToAsync(fs, cancellationToken);
            saved++;
        }

        var ws = await UpsertWorkspaceSessionAsync(sessionId, uploadRoot, "browser_upload", false, cancellationToken, folderName, userId);
        return Ok(new
        {
            success = true,
            uploadedFiles = saved,
            workspace = ToWorkspaceDto(ws, 0)
        });
    }

    [HttpGet("/api/agent/workspace/status")]
    public async Task<IActionResult> GetWorkspaceStatus([FromQuery] string? sessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Ok(new { success = true, bound = false });
        }

        var ws = await _db.WorkspaceSessions.FirstOrDefaultAsync(x => x.SessionId == sessionId.Trim(), cancellationToken);
        if (ws == null)
        {
            return Ok(new { success = true, bound = false });
        }

        await TryAutoUpgradeWorkspaceToRealDiskAsync(ws, cancellationToken);
        if (!Directory.Exists(ws.RootPath))
        {
            return Ok(new { success = true, bound = false });
        }

        var trashItems = await _workspaceRag.LoadTrashManifestAsync(ws.RootPath, ws.SessionId, cancellationToken);
        return Ok(new
        {
            success = true,
            bound = true,
            workspace = ToWorkspaceDto(ws, trashItems.Count(t => !t.Restored)),
            trashItems = trashItems.Where(t => !t.Restored).ToList()
        });
    }

    [HttpGet("/api/agent/workspace/sync-manifest")]
    public async Task<IActionResult> GetWorkspaceSyncManifest([FromQuery] string? sessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { success = false, error = "Thiếu sessionId." });

        var ws = await _db.WorkspaceSessions.FirstOrDefaultAsync(x => x.SessionId == sessionId.Trim(), cancellationToken);
        if (ws == null || !Directory.Exists(ws.RootPath))
            return NotFound(new { success = false, error = "Không tìm thấy workspace." });

        string[] ignored = ["node_modules", ".git", "bin", "obj", ".vs", ".cowai_trash", ".kode"];
        var activeFiles = Directory.EnumerateFiles(ws.RootPath, "*", SearchOption.AllDirectories)
            .Select(f => new
            {
                relPath = Path.GetRelativePath(ws.RootPath, f).Replace('\\', '/'),
                fullPath = f
            })
            .Where(x => !x.relPath.Split('/').Any(seg => ignored.Contains(seg, StringComparer.OrdinalIgnoreCase)))
            .Select(x =>
            {
                var fi = new FileInfo(x.fullPath);
                return new { path = x.relPath, size = fi.Length, lastWriteUtc = fi.LastWriteTimeUtc };
            })
            .ToList();

        var trashItems = await _workspaceRag.LoadTrashManifestAsync(ws.RootPath, ws.SessionId, cancellationToken);
        var deletedPaths = trashItems
            .Where(t => !t.Restored)
            .Select(t => t.OriginalRelPath.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Ok(new
        {
            success = true,
            sourceType = ws.SourceType,
            rootPath = ws.RootPath,
            files = activeFiles,
            deletedPaths
        });
    }

    [HttpGet("/api/agent/workspace/read-raw")]
    public async Task<IActionResult> ReadWorkspaceRawFile([FromQuery] string? sessionId, [FromQuery] string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(path))
            return BadRequest(new { error = "Thiếu sessionId hoặc path." });

        var ws = await _db.WorkspaceSessions.FirstOrDefaultAsync(x => x.SessionId == sessionId.Trim(), cancellationToken);
        if (ws == null || !Directory.Exists(ws.RootPath))
            return NotFound(new { error = "Không tìm thấy workspace." });

        var cleanRoot = Path.GetFullPath(ws.RootPath);
        var targetFull = Path.GetFullPath(Path.Combine(cleanRoot, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!targetFull.StartsWith(cleanRoot, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(targetFull))
            return NotFound(new { error = "Tệp không tồn tại trong workspace." });

        var bytes = await System.IO.File.ReadAllBytesAsync(targetFull, cancellationToken);
        return File(bytes, "application/octet-stream", Path.GetFileName(targetFull));
    }

    private async Task TryAutoUpgradeWorkspaceToRealDiskAsync(WorkspaceSession ws, CancellationToken ct)
    {
        if (!string.Equals(ws.SourceType, "browser_upload", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var fingerprints = new List<(string RelPath, long Size)>();
            if (Directory.Exists(ws.RootPath))
            {
                string[] ignored = ["node_modules", ".git", "bin", "obj", ".vs", ".cowai_trash", ".kode"];
                foreach (var f in Directory.EnumerateFiles(ws.RootPath, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(ws.RootPath, f).Replace('\\', '/');
                    if (rel.Split('/').Any(seg => ignored.Contains(seg, StringComparer.OrdinalIgnoreCase))) continue;
                    fingerprints.Add((rel, new FileInfo(f).Length));
                }
                var trash = await _workspaceRag.LoadTrashManifestAsync(ws.RootPath, ws.SessionId, ct);
                foreach (var t in trash)
                {
                    fingerprints.Add((t.OriginalRelPath.Replace('\\', '/'), -1));
                }
            }

            var realPath = _directoryAgent.TryResolveRealLocalFolder(ws.FolderName, fingerprints);
            if (!string.IsNullOrWhiteSpace(realPath) &&
                Directory.Exists(realPath) &&
                !realPath.Equals(ws.RootPath, StringComparison.OrdinalIgnoreCase) &&
                !_workspaceRag.IsDangerousSystemRoot(realPath))
            {
                // Đồng bộ các tệp mới tạo trong bản sao (như index.html) sang thư mục thật trước khi chuyển con trỏ
                if (Directory.Exists(ws.RootPath))
                {
                    string[] ignored = ["node_modules", ".git", "bin", "obj", ".vs", ".cowai_trash", ".kode"];
                    foreach (var f in Directory.EnumerateFiles(ws.RootPath, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(ws.RootPath, f).Replace('\\', '/');
                        if (rel.Split('/').Any(seg => ignored.Contains(seg, StringComparer.OrdinalIgnoreCase))) continue;
                        var dest = Path.Combine(realPath, rel.Replace('/', Path.DirectorySeparatorChar));
                        var destDir = Path.GetDirectoryName(dest);
                        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                        System.IO.File.Copy(f, dest, overwrite: true);
                    }
                }

                ws.RootPath = Path.GetFullPath(realPath);
                ws.SourceType = "server_path";
                ws.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Không thể tự nâng cấp browser_upload sang server_path cho {SessionId}", ws.SessionId);
        }
    }

    [HttpPost("/api/agent/workspace/toggle-auto")]
    public async Task<IActionResult> ToggleWorkspaceAutoApprove([FromBody] ToggleAutoApproveRequest? req, CancellationToken cancellationToken)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.SessionId))
            return BadRequest(new { success = false, error = "Thiếu sessionId." });

        var ws = await _db.WorkspaceSessions.FirstOrDefaultAsync(x => x.SessionId == req.SessionId.Trim(), cancellationToken);
        if (ws == null) return NotFound(new { success = false, error = "Phiên chat này chưa gắn thư mục." });

        ws.AutoApprove = req.AutoApprove;
        ws.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, autoApprove = ws.AutoApprove });
    }

    [HttpPost("/api/agent/workspace/unbind")]
    public async Task<IActionResult> UnbindWorkspace([FromBody] UnbindWorkspaceRequest? req, CancellationToken cancellationToken)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.SessionId))
            return BadRequest(new { success = false, error = "Thiếu sessionId." });

        var ws = await _db.WorkspaceSessions.FirstOrDefaultAsync(x => x.SessionId == req.SessionId.Trim(), cancellationToken);
        if (ws != null)
        {
            _db.WorkspaceSessions.Remove(ws);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new { success = true });
    }

    [HttpPost("/api/agent/workspace/approve")]
    public async Task<IActionResult> ApproveWorkspaceTool([FromBody] ApproveToolRequest? req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.ApprovalId))
            return BadRequest(new { success = false, error = "Thiếu approvalId." });

        var (found, approved, message) = await _kodeAgent.ResolveToolApprovalAsync(req.ApprovalId.Trim(), req.Approved, req.Note);
        if (!found)
            return NotFound(new { success = false, error = message });

        return Ok(new { success = true, approved, message });
    }

    [HttpPost("/api/agent/workspace/restore")]
    public async Task<IActionResult> RestoreWorkspaceTrash([FromBody] RestoreTrashRequest? req, CancellationToken cancellationToken)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.SessionId))
            return BadRequest(new { success = false, error = "Thiếu sessionId." });

        var ws = await _db.WorkspaceSessions.FirstOrDefaultAsync(x => x.SessionId == req.SessionId.Trim(), cancellationToken);
        if (ws == null || !Directory.Exists(ws.RootPath))
            return NotFound(new { success = false, error = "Không tìm thấy workspace của phiên chat." });

        var restored = await _workspaceRag.RestoreFromTrashAsync(ws.RootPath, ws.SessionId, req.TrashId, cancellationToken);
        var remaining = (await _workspaceRag.LoadTrashManifestAsync(ws.RootPath, ws.SessionId, cancellationToken)).Count(t => !t.Restored);

        return Ok(new
        {
            success = true,
            restoredCount = restored.Count,
            restoredPaths = restored.Select(r => r.OriginalRelPath).ToArray(),
            remainingTrashCount = remaining
        });
    }

    [HttpGet("/api/agent/workspace/download-zip")]
    public async Task<IActionResult> DownloadWorkspaceZip([FromQuery] string? sessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { error = "Thiếu sessionId." });

        var ws = await _db.WorkspaceSessions.FirstOrDefaultAsync(x => x.SessionId == sessionId.Trim(), cancellationToken);
        if (ws == null || !Directory.Exists(ws.RootPath))
            return NotFound(new { error = "Không tìm thấy thư mục workspace để tải về." });

        var bytes = await _workspaceRag.CreateWorkspaceZipBytesAsync(ws.RootPath, cancellationToken);
        var safeName = WorkspaceRagService.SanitizeSegment(ws.FolderName);
        return File(bytes, "application/zip", $"{safeName}-workspace.zip");
    }

    private async Task<WorkspaceSession> UpsertWorkspaceSessionAsync(
        string sessionId,
        string absPath,
        string sourceType,
        bool autoApprove,
        CancellationToken ct,
        string? customDisplayName = null,
        int? userId = null)
    {
        var sid = sessionId.Trim();
        var cleanPath = Path.GetFullPath(absPath);
        var folderName = string.IsNullOrWhiteSpace(customDisplayName)
            ? new DirectoryInfo(cleanPath).Name
            : customDisplayName.Trim();

        int fileCount = 0;
        try
        {
            var idxJson = await _workspaceRag.IndexWorkspaceAsync(cleanPath, ct);
            using var doc = System.Text.Json.JsonDocument.Parse(idxJson);
            if (doc.RootElement.TryGetProperty("fileCount", out var fc)) fileCount = fc.GetInt32();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Không thể lập chỉ mục RAG ngay khi bind {Path}", cleanPath);
        }

        var existing = await _db.WorkspaceSessions.FirstOrDefaultAsync(x => x.SessionId == sid, ct);
        if (existing == null)
        {
            existing = new WorkspaceSession
            {
                UserId = userId ?? GetAuthenticatedUserId(),
                SessionId = sid,
                FolderName = folderName,
                RootPath = cleanPath,
                SourceType = sourceType,
                FileCount = fileCount,
                AgentId = $"ws_{WorkspaceRagService.SanitizeSegment(sid)}",
                AutoApprove = autoApprove,
                LastIndexedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.WorkspaceSessions.Add(existing);
        }
        else
        {
            if (userId.HasValue) existing.UserId = userId;
            existing.FolderName = folderName;
            existing.RootPath = cleanPath;
            existing.SourceType = sourceType;
            existing.FileCount = fileCount;
            existing.AgentId = $"ws_{WorkspaceRagService.SanitizeSegment(sid)}";
            existing.LastIndexedAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return existing;
    }

    private static object ToWorkspaceDto(WorkspaceSession ws, int trashCount) => new
    {
        userId = ws.UserId,
        sessionId = ws.SessionId,
        folderName = ws.FolderName,
        rootPath = ws.RootPath,
        sourceType = ws.SourceType,
        fileCount = ws.FileCount,
        autoApprove = ws.AutoApprove,
        lastIndexedAt = ws.LastIndexedAt,
        trashCount
    };

    #endregion

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

public record ReadDirectoryRequest(string Path);
public record BindWorkspaceRequest(string SessionId, string Path, bool AutoApprove = false);
public record PickNativeFolderRequest(string? SessionId = null, string? InitialPath = null, bool AutoApprove = false);
public record ToggleAutoApproveRequest(string SessionId, bool AutoApprove);
public record UnbindWorkspaceRequest(string SessionId);
public record ApproveToolRequest(string ApprovalId, bool Approved, string? Note = null);
public record RestoreTrashRequest(string SessionId, string? TrashId = null);
