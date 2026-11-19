using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace COW_AI.Services;

/// <summary>
/// Dịch vụ tạo ảnh cục bộ trên GPU (RTX 4060) thông qua stable-diffusion.cpp (sd-server).
/// Tự động giải phóng VRAM của Ollama trước khi sinh ảnh và lưu ảnh tạm trong RAM (Privacy-by-Design).
/// </summary>
public class LocalImageService : IDisposable
{
    private const int MaxCachedImages = 24;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OllamaService _ollama;
    private readonly IConfiguration _config;
    private readonly ILogger<LocalImageService>? _logger;

    private readonly SemaphoreSlim _generationLock = new(1, 1);
    private readonly SemaphoreSlim _startupLock = new(1, 1);
    private readonly ConcurrentDictionary<string, CachedImageEntry> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _cacheOrder = new();

    private Process? _managedServerProcess;

    public string BaseUrl => (_config["LocalImage:BaseUrl"] ?? "http://127.0.0.1:7860").TrimEnd('/');
    public string ExecutablePath => _config["LocalImage:ExecutablePath"] ?? @"D:\LocalImageAI\bin\sd-server.exe";
    public string ModelPath => _config["LocalImage:ModelPath"] ?? @"D:\LocalImageAI\models\sd_xl_turbo_1.0_fp16.safetensors";
    public string VaePath => _config["LocalImage:VaePath"] ?? @"D:\LocalImageAI\models\sdxl_vae.safetensors";
    public bool AutoStart => !string.Equals(_config["LocalImage:AutoStart"], "false", StringComparison.OrdinalIgnoreCase);
    public int DefaultSteps => int.TryParse(_config["LocalImage:Steps"], out var s) && s > 0 ? s : 4;
    public float DefaultCfgScale => float.TryParse(_config["LocalImage:CfgScale"], System.Globalization.CultureInfo.InvariantCulture, out var c) && c >= 0 ? c : 1.0f;
    public string SamplingMethod => _config["LocalImage:SamplingMethod"] ?? "euler_a";

    public LocalImageService(
        IHttpClientFactory httpClientFactory,
        OllamaService ollama,
        IConfiguration config,
        ILogger<LocalImageService>? logger = null)
    {
        _httpClientFactory = httpClientFactory;
        _ollama = ollama;
        _config = config;
        _logger = logger;
    }

    public sealed record CachedImageEntry(byte[] Bytes, string ContentType, DateTimeOffset CreatedAt);

    public CachedImageEntry? GetCachedImage(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return _imageCache.TryGetValue(id.Trim(), out var entry) ? entry : null;
    }

    /// <summary>
    /// Chuẩn hóa kích thước ảnh tối ưu cho SDXL Turbo trên GPU 8 GB VRAM (bội số của 64).
    /// </summary>
    public static (int Width, int Height) NormalizeTurboDimensions(int requestedWidth, int requestedHeight)
    {
        if (requestedWidth > requestedHeight)
        {
            return (768, 512); // Khổ ngang 16:9 / widescreen
        }
        if (requestedHeight > requestedWidth)
        {
            return (512, 768); // Khổ dọc 9:16 / portrait
        }
        return (512, 512); // Khổ vuông gốc của SDXL Turbo (nhanh & ổn định nhất)
    }

    /// <summary>
    /// Sinh ảnh cục bộ bằng SDXL Turbo qua sd-server và trả về đường dẫn tương đối /Image/LocalOutput/{id}.
    /// </summary>
    public async Task<(string ImageUrl, int Width, int Height)> GenerateImageAsync(
        string englishPrompt,
        int requestedWidth = 512,
        int requestedHeight = 512,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(englishPrompt))
        {
            throw new ArgumentException("Prompt tạo ảnh không được để trống.", nameof(englishPrompt));
        }

        var (width, height) = NormalizeTurboDimensions(requestedWidth, requestedHeight);

        await _generationLock.WaitAsync(cancellationToken);
        try
        {
            // 1. Giải phóng VRAM của Ollama (cow-ai:latest) trước khi sinh ảnh để tránh tràn 8 GB VRAM
            await _ollama.UnloadActiveModelsAsync(cancellationToken);

            // 2. Đảm bảo sd-server đang chạy
            await EnsureServerRunningAsync(cancellationToken);

            // 3. Gửi yêu cầu sinh ảnh chuẩn OpenAI tới sd-server
            var client = _httpClientFactory.CreateClient("LocalImage");
            var endpoint = $"{BaseUrl}/v1/images/generations";

            var payload = new Dictionary<string, object>
            {
                ["prompt"] = englishPrompt,
                ["n"] = 1,
                ["size"] = $"{width}x{height}",
                ["response_format"] = "b64_json",
                ["steps"] = DefaultSteps,
                ["cfg_scale"] = DefaultCfgScale,
                ["sampling_method"] = SamplingMethod
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(payload)
            };

            _logger?.LogInformation("Gửi yêu cầu tạo ảnh Local SDXL Turbo ({Width}x{Height}, {Steps} steps): {Prompt}", width, height, DefaultSteps, englishPrompt);

            using var response = await client.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogError("Local sd-server trả về HTTP {StatusCode}: {Body}", (int)response.StatusCode, responseBody);
                throw new InvalidOperationException($"Dịch vụ tạo ảnh Local trả về lỗi HTTP {(int)response.StatusCode}.");
            }

            var imageBytes = ExtractImageBytes(responseBody);
            if (imageBytes == null || imageBytes.Length == 0)
            {
                _logger?.LogError("Local sd-server không trả về dữ liệu ảnh hợp lệ: {Body}", responseBody.Length > 300 ? responseBody[..300] : responseBody);
                throw new InvalidOperationException("Dịch vụ tạo ảnh Local không trả về dữ liệu hình ảnh.");
            }

            var id = StoreEphemeralImage(imageBytes, "image/png");
            return ($"/Image/LocalOutput/{id}", width, height);
        }
        finally
        {
            _generationLock.Release();
        }
    }

    public async Task<bool> IsServerHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("LocalImageHealth");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            using var resp = await client.GetAsync($"{BaseUrl}/v1/models", cts.Token);
            return resp.IsSuccessStatusCode || (int)resp.StatusCode < 500;
        }
        catch
        {
            return false;
        }
    }

    private async Task EnsureServerRunningAsync(CancellationToken cancellationToken)
    {
        if (await IsServerHealthyAsync(cancellationToken))
        {
            return;
        }

        if (!AutoStart)
        {
            throw new InvalidOperationException($"Dịch vụ tạo ảnh Local ({BaseUrl}) chưa được khởi động. Hãy chạy D:\\LocalImageAI\\start-sd-server.cmd trước.");
        }

        await _startupLock.WaitAsync(cancellationToken);
        try
        {
            if (await IsServerHealthyAsync(cancellationToken))
            {
                return;
            }

            if (!File.Exists(ExecutablePath))
            {
                throw new InvalidOperationException($"Không tìm thấy sd-server tại '{ExecutablePath}'. Vui lòng kiểm tra thư mục cài đặt D:\\LocalImageAI.");
            }
            if (!File.Exists(ModelPath))
            {
                throw new InvalidOperationException($"Không tìm thấy model SDXL Turbo tại '{ModelPath}'.");
            }

            var uri = new Uri(BaseUrl);
            var argsList = new List<string>
            {
                $"--listen-ip {uri.Host}",
                $"--listen-port {uri.Port}",
                $"-m \"{ModelPath}\"",
                $"--steps {DefaultSteps}",
                $"--cfg-scale {DefaultCfgScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"--sampling-method {SamplingMethod}",
                "--vae-tiling",
                "--backend te=cpu"
            };

            if (!string.IsNullOrWhiteSpace(VaePath) && File.Exists(VaePath))
            {
                argsList.Add($"--vae \"{VaePath}\"");
            }

            var extraArgs = _config["LocalImage:ExtraArgs"];
            if (!string.IsNullOrWhiteSpace(extraArgs))
            {
                argsList.Add(extraArgs.Trim());
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ExecutablePath,
                Arguments = string.Join(" ", argsList),
                WorkingDirectory = Path.GetDirectoryName(ExecutablePath) ?? @"D:\LocalImageAI\bin",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            _logger?.LogInformation("Đang tự động khởi động sd-server CUDA: {Exe} {Args}", startInfo.FileName, startInfo.Arguments);
            _managedServerProcess = Process.Start(startInfo);

            // Chờ sd-server nạp model SDXL Turbo lên RTX 4060 và mở cổng (tối đa 90 giây)
            var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_managedServerProcess != null && _managedServerProcess.HasExited)
                {
                    throw new InvalidOperationException($"sd-server đã thoát đột ngột (ExitCode={_managedServerProcess.ExitCode}). Kiểm tra lại VRAM hoặc chạy thử D:\\LocalImageAI\\start-sd-server.cmd.");
                }

                if (await IsServerHealthyAsync(cancellationToken))
                {
                    _logger?.LogInformation("sd-server CUDA đã sẵn sàng tại {BaseUrl}", BaseUrl);
                    return;
                }

                await Task.Delay(500, cancellationToken);
            }

            throw new InvalidOperationException("Hết thời gian chờ khởi động dịch vụ tạo ảnh Local (sd-server). Vui lòng thử lại.");
        }
        finally
        {
            _startupLock.Release();
        }
    }

    private static byte[]? ExtractImageBytes(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody)) return null;

        using var doc = JsonDocument.Parse(responseBody);
        if (doc.RootElement.TryGetProperty("data", out var dataElem)
            && dataElem.ValueKind == JsonValueKind.Array
            && dataElem.GetArrayLength() > 0)
        {
            var first = dataElem[0];
            if (first.TryGetProperty("b64_json", out var b64Elem))
            {
                var b64 = b64Elem.GetString();
                if (!string.IsNullOrWhiteSpace(b64))
                {
                    var commaIndex = b64.IndexOf(',');
                    if (b64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIndex >= 0)
                    {
                        b64 = b64[(commaIndex + 1)..];
                    }
                    return Convert.FromBase64String(b64);
                }
            }
        }

        return null;
    }

    private string StoreEphemeralImage(byte[] bytes, string contentType)
    {
        var id = Guid.NewGuid().ToString("N");
        _imageCache[id] = new CachedImageEntry(bytes, contentType, DateTimeOffset.UtcNow);
        _cacheOrder.Enqueue(id);

        while (_cacheOrder.Count > MaxCachedImages && _cacheOrder.TryDequeue(out var oldestId))
        {
            _imageCache.TryRemove(oldestId, out _);
        }

        return id;
    }

    public void Dispose()
    {
        try
        {
            if (_managedServerProcess != null && !_managedServerProcess.HasExited)
            {
                _managedServerProcess.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Bỏ qua lỗi khi dọn dẹp tiến trình con
        }
        _managedServerProcess?.Dispose();
        _generationLock.Dispose();
        _startupLock.Dispose();
    }
}

