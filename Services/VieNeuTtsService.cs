using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace COW_AI.Services;

/// <summary>
/// Dịch vụ tổng hợp giọng nói tiếng Việt bắt buộc sử dụng VieNeu-TTS v3 Turbo (48kHz).
/// Duy trì Persistent Python Worker Daemon để giữ model đã nạp sẵn trong RAM/VRAM,
/// đồng thời cung cấp trạng thái tải Server thời gian thực cho UI thông báo đợi server load.
/// Không fallback sang Edge-TTS.
/// </summary>
public class VieNeuTtsService : IVieNeuTtsService, IDisposable
{
    private const string WorkerProtocolPrefix = "@@VIENEU_JSON@@";
    private static readonly TimeSpan ModelLoadTimeout = TimeSpan.FromSeconds(180);
    private static readonly TimeSpan SynthesisTimeout = TimeSpan.FromSeconds(90);

    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<VieNeuTtsService> _logger;

    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _inferLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<(bool Success, string? Error)>> _pendingRequests = new();

    private Process? _workerProcess;
    private TaskCompletionSource<bool>? _readyTcs;
    private string _state = "unloaded"; // unloaded | loading | ready | error
    private string _statusMessage = "⏳ Server âm thanh VieNeu-TTS 48kHz đang chờ khởi động...";
    private string _engineLabel = "VieNeu-TTS v3 Turbo 48kHz";
    private int _sampleRate = 48000;
    private bool _disposed;

    private static readonly Dictionary<string, string> VoiceMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Hải Đăng"] = "Hải Đăng",
        ["Hai Dang"] = "Hải Đăng",
        ["Hoài My"] = "Ngọc Huyền",
        ["Hoai My"] = "Ngọc Huyền",
        ["vi-VN-HoaiMyNeural"] = "Ngọc Huyền",
        ["Lan Nhi"] = "Thùy Dung",
        ["Nam Minh"] = "Thái Sơn",
        ["vi-VN-NamMinhNeural"] = "Thái Sơn",
        ["Bình"] = "Quang Sơn",
        ["Binh"] = "Quang Sơn",
        ["Ngọc Huyền"] = "Ngọc Huyền",
        ["Thùy Dung"] = "Thùy Dung",
        ["Thái Sơn"] = "Thái Sơn",
        ["Quang Sơn"] = "Quang Sơn",
        ["Trúc Ly"] = "Trúc Ly",
        ["Thiện Minh"] = "Thiện Minh",
        ["Mai Anh"] = "Mai Anh",
        ["Minh Đức"] = "Minh Đức",
        ["Xuân Vĩnh"] = "Xuân Vĩnh"
    };

    public VieNeuTtsService(
        IConfiguration config,
        IWebHostEnvironment env,
        ILogger<VieNeuTtsService> logger)
    {
        _config = config;
        _env = env;
        _logger = logger;
    }

    public List<VieNeuVoiceOption> GetAvailableVoices()
    {
        return new List<VieNeuVoiceOption>
        {
            new("Hải Đăng", "Hải Đăng - Nam Miền Bắc (VieNeu 48kHz)", "Male", "VieNeu-TTS v3 Turbo"),
            new("Hoài My", "Ngọc Huyền (Hoài My) - Nữ Miền Bắc (VieNeu 48kHz)", "Female", "VieNeu-TTS v3 Turbo"),
            new("Lan Nhi", "Thùy Dung (Lan Nhi) - Nữ Miền Nam (VieNeu 48kHz)", "Female", "VieNeu-TTS v3 Turbo"),
            new("Nam Minh", "Thái Sơn (Nam Minh) - Nam Miền Nam (VieNeu 48kHz)", "Male", "VieNeu-TTS v3 Turbo"),
            new("Bình", "Quang Sơn (Bình) - Nam Miền Trung (VieNeu 48kHz)", "Male", "VieNeu-TTS v3 Turbo")
        };
    }

    public VieNeuServerStatus GetServerStatus()
    {
        var procAlive = _workerProcess != null && !_workerProcess.HasExited;
        var state = _state;
        if (state == "ready" && !procAlive)
        {
            state = "unloaded";
        }

        return new VieNeuServerStatus(
            State: state,
            Message: _statusMessage,
            IsReady: state == "ready" && procAlive,
            IsLoading: state == "loading",
            Engine: _engineLabel,
            SampleRate: _sampleRate
        );
    }

    public void TriggerWarmUp()
    {
        if (_disposed) return;
        var status = GetServerStatus();
        if (status.IsReady || status.IsLoading) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await EnsureServerLoadedAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WarmUp VieNeu-TTS worker không hoàn tất.");
            }
        });
    }

    public async Task<VieNeuServerStatus> EnsureServerLoadedAsync(CancellationToken ct = default)
    {
        if (_workerProcess != null && !_workerProcess.HasExited && _state == "ready")
        {
            return GetServerStatus();
        }

        TaskCompletionSource<bool> tcsToWait;

        await _lifecycleLock.WaitAsync(ct);
        try
        {
            if (_workerProcess != null && !_workerProcess.HasExited && _state == "ready")
            {
                return GetServerStatus();
            }

            if (_workerProcess != null && !_workerProcess.HasExited && _state == "loading" && _readyTcs != null)
            {
                tcsToWait = _readyTcs;
            }
            else
            {
                StartWorkerProcessLocked();
                tcsToWait = _readyTcs ?? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ModelLoadTimeout);

        try
        {
            await tcsToWait.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _state = "error";
            _statusMessage = "Hết thời gian chờ (180s) khi nạp model âm thanh VieNeu-TTS 48kHz.";
        }

        return GetServerStatus();
    }

    private void StartWorkerProcessLocked()
    {
        CleanupWorkerLocked();

        var repoPath = _config["VieNeu:RepoPath"] ?? @"G:\Video\auto-compare-video\VieNeu-TTS";
        var pythonPath = _config["VieNeu:PythonPath"] ?? Path.Combine(repoPath, ".venv", "Scripts", "python.exe");
        var srcDir = Path.Combine(repoPath, "src");
        var workerScript = Path.Combine(_env.ContentRootPath, "Tools", "vieneu_worker.py");

        if (!File.Exists(pythonPath))
        {
            _state = "error";
            _statusMessage = $"Không tìm thấy Python của VieNeu-TTS tại '{pythonPath}'.";
            _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _readyTcs.TrySetResult(false);
            return;
        }

        if (!Directory.Exists(srcDir) || !File.Exists(workerScript))
        {
            _state = "error";
            _statusMessage = $"Thiếu mã nguồn VieNeu-TTS ('{srcDir}') hoặc worker script ('{workerScript}').";
            _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _readyTcs.TrySetResult(false);
            return;
        }

        _state = "loading";
        _statusMessage = "⏳ Đang đợi Server nạp model âm thanh VieNeu-TTS 48kHz...";
        var readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _readyTcs = readyTcs;

        var psi = new ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = $"-u \"{workerScript}\" \"{srcDir}\"",
            WorkingDirectory = repoPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var proc = Process.Start(psi);
        if (proc == null)
        {
            _state = "error";
            _statusMessage = "Không thể khởi chạy tiến trình server VieNeu-TTS.";
            readyTcs.TrySetResult(false);
            return;
        }

        _workerProcess = proc;
        _logger.LogInformation("Đã khởi động VieNeu-TTS Persistent Worker (PID={Pid})", proc.Id);

        _ = Task.Run(async () => await ReadWorkerStdoutLoopAsync(proc, readyTcs));
        _ = Task.Run(async () => await DrainWorkerStderrLoopAsync(proc));
    }

    private async Task ReadWorkerStdoutLoopAsync(Process proc, TaskCompletionSource<bool> readyTcs)
    {
        try
        {
            while (!proc.HasExited)
            {
                var line = await proc.StandardOutput.ReadLineAsync();
                if (line == null) break;
                if (!line.StartsWith(WorkerProtocolPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var jsonText = line.Substring(WorkerProtocolPrefix.Length);
                try
                {
                    using var doc = JsonDocument.Parse(jsonText);
                    var root = doc.RootElement;
                    var ev = root.TryGetProperty("event", out var evEl) ? evEl.GetString() : string.Empty;

                    if (ev == "loading")
                    {
                        _state = "loading";
                        _statusMessage = root.TryGetProperty("message", out var msgEl) && !string.IsNullOrWhiteSpace(msgEl.GetString())
                            ? msgEl.GetString()!
                            : "⏳ Đang đợi Server nạp model âm thanh VieNeu-TTS 48kHz...";
                    }
                    else if (ev == "ready")
                    {
                        var backend = root.TryGetProperty("backend", out var bEl) ? bEl.GetString() : "v3turbo";
                        _sampleRate = root.TryGetProperty("sampleRate", out var srEl) && srEl.TryGetInt32(out var sr) ? sr : 48000;
                        _engineLabel = $"VieNeu-TTS v3 Turbo ({backend}, {_sampleRate / 1000}kHz)";
                        _state = "ready";
                        _statusMessage = "✅ Server âm thanh VieNeu-TTS 48kHz đã sẵn sàng.";
                        readyTcs.TrySetResult(true);
                        _logger.LogInformation("VieNeu-TTS Worker sẵn sàng: {Engine}", _engineLabel);
                    }
                    else if (ev == "error")
                    {
                        var err = root.TryGetProperty("error", out var errEl) ? errEl.GetString() : "Lỗi không xác định khi nạp VieNeu-TTS.";
                        _state = "error";
                        _statusMessage = err!;
                        readyTcs.TrySetResult(false);
                        _logger.LogError("VieNeu-TTS Worker báo lỗi: {Error}", err);
                    }
                    else if (ev == "result")
                    {
                        var reqId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                        var success = root.TryGetProperty("success", out var okEl) && okEl.ValueKind == JsonValueKind.True;
                        var err = root.TryGetProperty("error", out var eEl) ? eEl.GetString() : null;
                        if (_pendingRequests.TryRemove(reqId, out var pending))
                        {
                            pending.TrySetResult((success, err));
                        }
                    }
                }
                catch (Exception parseEx)
                {
                    _logger.LogDebug(parseEx, "Bỏ qua dòng log không phải JSON protocol từ VieNeu worker.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Luồng đọc stdout của VieNeu-TTS Worker đã dừng.");
        }
        finally
        {
            if (_state != "error")
            {
                _state = "unloaded";
                _statusMessage = "Tiến trình VieNeu-TTS Worker đã đóng.";
            }
            readyTcs.TrySetResult(false);

            foreach (var kv in _pendingRequests)
            {
                if (_pendingRequests.TryRemove(kv.Key, out var pending))
                {
                    pending.TrySetResult((false, "Server VieNeu-TTS đã ngắt kết nối giữa chừng."));
                }
            }
        }
    }

    private async Task DrainWorkerStderrLoopAsync(Process proc)
    {
        try
        {
            while (!proc.HasExited)
            {
                var line = await proc.StandardError.ReadLineAsync();
                if (line == null) break;
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _logger.LogDebug("[VieNeu-TTS stderr] {Line}", line);
                }
            }
        }
        catch { }
    }

    private void CleanupWorkerLocked()
    {
        try
        {
            if (_workerProcess != null)
            {
                if (!_workerProcess.HasExited)
                {
                    _workerProcess.Kill(entireProcessTree: true);
                }
                _workerProcess.Dispose();
                _workerProcess = null;
            }
        }
        catch { }
    }

    public static string ResolveVieNeuPresetVoice(string? requestedVoice)
    {
        if (string.IsNullOrWhiteSpace(requestedVoice))
        {
            return "Hải Đăng";
        }

        var trimmed = requestedVoice.Trim();
        if (VoiceMap.TryGetValue(trimmed, out var mapped))
        {
            return mapped;
        }

        return trimmed;
    }

    public async Task<(bool Success, string? Error, string AudioUrl, double DurationSeconds)> GenerateAudioAsync(
        string text,
        string voiceName,
        string relativeSubDir,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (false, "Văn bản thuyết minh không được để trống.", string.Empty, 0);
        }

        var presetVoice = ResolveVieNeuPresetVoice(voiceName);
        var cleanVoiceSafe = Regex.Replace(presetVoice, @"[^\w\-]", "");
        if (string.IsNullOrWhiteSpace(cleanVoiceSafe)) cleanVoiceSafe = "vieneu";

        var cleanSubDir = (relativeSubDir ?? string.Empty).Replace('\\', '/').Trim('/');
        if (cleanSubDir.Equals("studio/audio", StringComparison.OrdinalIgnoreCase))
        {
            cleanSubDir = string.Empty;
        }

        var baseMediaDir = Path.Combine(_env.WebRootPath, "media", "studio", "audio");
        var targetDir = string.IsNullOrWhiteSpace(cleanSubDir)
            ? baseMediaDir
            : Path.Combine(baseMediaDir, cleanSubDir.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(targetDir);

        var fileName = $"vo_{cleanVoiceSafe}_{Guid.NewGuid():N}.wav";
        var localFilePath = Path.Combine(targetDir, fileName);
        var webUrl = string.IsNullOrWhiteSpace(cleanSubDir)
            ? $"/media/studio/audio/{fileName}"
            : $"/media/studio/audio/{cleanSubDir}/{fileName}";

        var (ok, err) = await SynthesizeToFileAsync(text, presetVoice, localFilePath, ct);
        if (!ok || !File.Exists(localFilePath) || new FileInfo(localFilePath).Length < 100)
        {
            return (false, err ?? "Server VieNeu-TTS không thể tạo tệp âm thanh 48kHz.", string.Empty, 0);
        }

        var duration = await MeasureAudioDurationAsync(localFilePath, ct);
        _logger.LogInformation("Đã sinh giọng VieNeu-TTS 48kHz ({Voice}): {Url} ({Duration}s)", presetVoice, webUrl, duration);
        return (true, null, webUrl, duration);
    }

    public async Task<(bool Success, string? Error, byte[] AudioBytes, string ContentType)> SynthesizeWavBytesAsync(
        string text,
        string voiceName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (false, "Văn bản không được để trống.", Array.Empty<byte>(), "audio/wav");
        }

        var presetVoice = ResolveVieNeuPresetVoice(voiceName);
        var tempPath = Path.Combine(Path.GetTempPath(), $"vieneu_speak_{Guid.NewGuid():N}.wav");
        try
        {
            var (ok, err) = await SynthesizeToFileAsync(text, presetVoice, tempPath, ct);
            if (!ok || !File.Exists(tempPath))
            {
                return (false, err ?? "Server VieNeu-TTS không thể tổng hợp giọng nói.", Array.Empty<byte>(), "audio/wav");
            }

            var bytes = await File.ReadAllBytesAsync(tempPath, ct);
            return (true, null, bytes, "audio/wav");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private async Task<(bool Success, string? Error)> SynthesizeToFileAsync(
        string text,
        string presetVoice,
        string outputPath,
        CancellationToken ct)
    {
        var serverStatus = await EnsureServerLoadedAsync(ct);
        if (!serverStatus.IsReady)
        {
            return (false, serverStatus.Message);
        }

        await _inferLock.WaitAsync(ct);
        try
        {
            if (_workerProcess == null || _workerProcess.HasExited || _state != "ready")
            {
                serverStatus = await EnsureServerLoadedAsync(ct);
                if (!serverStatus.IsReady || _workerProcess == null)
                {
                    return (false, serverStatus.Message);
                }
            }

            var reqId = Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<(bool Success, string? Error)>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[reqId] = tcs;

            try
            {
                var payloadJson = JsonSerializer.Serialize(new
                {
                    id = reqId,
                    text = text.Trim(),
                    voice = presetVoice,
                    outputPath
                });

                await _workerProcess.StandardInput.WriteLineAsync(payloadJson.AsMemory(), ct);
                await _workerProcess.StandardInput.FlushAsync(ct);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(SynthesisTimeout);

                return await tcs.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return (false, "Hết thời gian chờ Server VieNeu-TTS tổng hợp giọng nói.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi gửi lệnh tổng hợp tới VieNeu-TTS Worker");
                return (false, "Lỗi kết nối với Server âm thanh VieNeu-TTS: " + ex.Message);
            }
            finally
            {
                _pendingRequests.TryRemove(reqId, out _);
            }
        }
        finally
        {
            _inferLock.Release();
        }
    }

    private string ResolveFfprobePath()
    {
        var candidates = new[]
        {
            _config["AutoVideo:FfprobePath"],
            Path.Combine(_env.ContentRootPath, "Tools", "ffmpeg", "ffprobe.exe"),
            @"G:\Video\ffmpeg\bin\ffprobe.exe"
        };
        foreach (var path in candidates)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
        }
        return "ffprobe";
    }

    public async Task<double> MeasureAudioDurationAsync(string localAudioPath, CancellationToken ct = default)
    {
        if (!File.Exists(localAudioPath))
        {
            return 3.5;
        }

        // 1. Đọc WAV header trực tiếp (nhanh & chính xác cho VieNeu-TTS 48kHz PCM WAV)
        if (localAudioPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await using var stream = File.OpenRead(localAudioPath);
                using var reader = new BinaryReader(stream);
                if (stream.Length > 44)
                {
                    reader.BaseStream.Seek(28, SeekOrigin.Begin);
                    int byteRate = reader.ReadInt32();
                    if (byteRate > 0)
                    {
                        var dataLength = stream.Length - 44;
                        var duration = (double)dataLength / byteRate;
                        if (duration > 0.2)
                        {
                            return Math.Round(Math.Clamp(duration, 1.0, 180.0), 2);
                        }
                    }
                }
            }
            catch { }
        }

        // 2. Đo bằng ffprobe
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ResolveFfprobePath(),
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{localAudioPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
                await proc.WaitForExitAsync(ct);
                if (proc.ExitCode == 0 && double.TryParse(stdout.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var exactSec) && exactSec > 0.2)
                {
                    return Math.Round(Math.Clamp(exactSec, 1.0, 180.0), 2);
                }
            }
        }
        catch { }

        var info = new FileInfo(localAudioPath);
        return Math.Round(Math.Clamp(info.Length / 96000.0, 1.5, 60.0), 1);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CleanupWorkerLocked();
        _lifecycleLock.Dispose();
        _inferLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
