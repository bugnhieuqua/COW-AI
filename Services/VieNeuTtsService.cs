using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace COW_AI.Services;

public class VieNeuTtsService : IVieNeuTtsService
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly EdgeTtsClient _edgeTtsClient;
    private readonly ILogger<VieNeuTtsService> _logger;

    public VieNeuTtsService(
        IConfiguration config,
        IWebHostEnvironment env,
        EdgeTtsClient edgeTtsClient,
        ILogger<VieNeuTtsService> logger)
    {
        _config = config;
        _env = env;
        _edgeTtsClient = edgeTtsClient;
        _logger = logger;
    }

    public List<VieNeuVoiceOption> GetAvailableVoices()
    {
        return new List<VieNeuVoiceOption>
        {
            new("Hải Đăng", "Hải Đăng - Nam Miền Bắc (Chuẩn học thuật, truyền cảm - 48kHz)", "Male", "VieNeu-TTS"),
            new("Hoài My", "Hoài My - Nữ Miền Bắc (Ấm áp, phát thanh viên)", "Female", "Edge-TTS"),
            new("Lan Nhi", "Lan Nhi - Nữ Miền Nam (Tươi sáng, tự nhiên - 48kHz)", "Female", "VieNeu-TTS"),
            new("Nam Minh", "Nam Minh - Nam Miền Nam (Trầm ấm, lịch lãm)", "Male", "Edge-TTS"),
            new("Bình", "Bình - Nam Miền Trung (Nội lực, khúc chiết - 48kHz)", "Male", "VieNeu-TTS")
        };
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

        var selectedVoice = string.IsNullOrWhiteSpace(voiceName) ? "Hải Đăng" : voiceName.Trim();
        var cleanVoiceSafe = Regex.Replace(selectedVoice, @"[^\w\-]", "");

        var baseMediaDir = Path.Combine(_env.WebRootPath, "media", "studio", "audio");
        var targetDir = string.IsNullOrWhiteSpace(relativeSubDir) 
            ? baseMediaDir 
            : Path.Combine(baseMediaDir, relativeSubDir);

        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var fileName = $"vo_{cleanVoiceSafe}_{Guid.NewGuid():N}.mp3";
        var localFilePath = Path.Combine(targetDir, fileName);
        var webUrl = string.IsNullOrWhiteSpace(relativeSubDir)
            ? $"/media/studio/audio/{fileName}"
            : $"/media/studio/audio/{relativeSubDir.Replace('\\', '/')}/{fileName}";

        // 1. Thử sinh bằng VieNeu-TTS cục bộ (nếu là giọng VieNeu)
        if (IsVieNeuVoice(selectedVoice))
        {
            var vieneuSuccess = await TryGenerateWithVieNeuAsync(text, selectedVoice, localFilePath, ct);
            if (vieneuSuccess && File.Exists(localFilePath) && new FileInfo(localFilePath).Length > 100)
            {
                var duration = await MeasureAudioDurationAsync(localFilePath, ct);
                _logger.LogInformation("Đã sinh giọng VieNeu-TTS thành công: {Url} ({Duration}s)", webUrl, duration);
                return (true, null, webUrl, duration);
            }
            _logger.LogWarning("VieNeu-TTS cục bộ không sẵn sàng hoặc gặp lỗi. Tự động chuyển fallback sang Edge Neural.");
        }

        // 2. Fallback dự phòng mượt mà qua Edge Neural TTS
        try
        {
            var fallbackVoice = selectedVoice.StartsWith("vi-VN-", StringComparison.OrdinalIgnoreCase)
                ? selectedVoice
                : (selectedVoice.Contains("Hoài My", StringComparison.OrdinalIgnoreCase) || selectedVoice.Contains("HoaiMy", StringComparison.OrdinalIgnoreCase) || selectedVoice.Contains("Nhi", StringComparison.OrdinalIgnoreCase)
                    ? "vi-VN-HoaiMyNeural"
                    : "vi-VN-NamMinhNeural");

            await using (var fileStream = new FileStream(localFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await _edgeTtsClient.StreamAsync(text, fileStream, fallbackVoice, ct);
            }

            if (File.Exists(localFilePath) && new FileInfo(localFilePath).Length > 0)
            {
                var duration = await MeasureAudioDurationAsync(localFilePath, ct);
                _logger.LogInformation("Đã sinh giọng Edge-TTS (Fallback) thành công: {Url} ({Duration}s)", webUrl, duration);
                return (true, null, webUrl, duration);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi tổng hợp âm thanh giọng đọc {Voice}", selectedVoice);
            return (false, "Lỗi tổng hợp giọng nói: " + ex.Message, string.Empty, 0);
        }

        return (false, "Không tạo được file âm thanh.", string.Empty, 0);
    }

    private bool IsVieNeuVoice(string voiceName)
    {
        return voiceName.Equals("Hải Đăng", StringComparison.OrdinalIgnoreCase) ||
               voiceName.Equals("Lan Nhi", StringComparison.OrdinalIgnoreCase) ||
               voiceName.Equals("Binh", StringComparison.OrdinalIgnoreCase) ||
               voiceName.Equals("Bình", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> TryGenerateWithVieNeuAsync(
        string text, 
        string voiceName, 
        string outputPath, 
        CancellationToken ct)
    {
        try
        {
            var repoPath = _config["VieNeu:RepoPath"] ?? @"G:\Video\auto-compare-video\VieNeu-TTS";
            var pythonPath = _config["VieNeu:PythonPath"] ?? Path.Combine(repoPath, ".venv", "Scripts", "python.exe");

            if (!File.Exists(pythonPath))
            {
                // Thử tìm python mặc định trên hệ thống
                pythonPath = "python";
            }

            var srcDir = Path.Combine(repoPath, "src");
            if (!Directory.Exists(srcDir))
            {
                return false;
            }

            // Tạo script python sinh voice ngắn hạn
            var tempScriptPath = Path.Combine(Path.GetTempPath(), $"vieneu_gen_{Guid.NewGuid():N}.py");
            var cleanText = text.Replace("\"", "\\\"").Replace("\n", " ");

            var scriptContent = $@"
import sys
import os

os.environ['HF_HUB_OFFLINE'] = '1'
os.environ['TRANSFORMERS_OFFLINE'] = '1'

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

sys.path.insert(0, r'{srcDir}')
try:
    from vieneu import Vieneu
    tts = Vieneu()
    target_voice = '{voiceName}'
    try:
        voice_data = tts.get_preset_voice(target_voice)
    except Exception:
        voice_data = tts.get_preset_voice('Hải Đăng')
    audio = tts.infer(text='{cleanText}', voice=voice_data)
    tts.save(audio, r'{outputPath}')
    print('SUCCESS')
except Exception as e:
    print(f'ERROR: {{e}}')
    sys.exit(1)
";
            await File.WriteAllTextAsync(tempScriptPath, scriptContent, Encoding.UTF8, ct);

            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"\"{tempScriptPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return false;

            var outputTask = proc.StandardOutput.ReadToEndAsync(ct);
            var errorTask = proc.StandardError.ReadToEndAsync(ct);

            var completed = await Task.Run(() => proc.WaitForExit(12000), ct);
            try { if (File.Exists(tempScriptPath)) File.Delete(tempScriptPath); } catch { }

            if (!completed)
            {
                try { proc.Kill(); } catch { }
                return false;
            }

            var output = await outputTask;
            return output.Contains("SUCCESS");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Không thể gọi trực tiếp script VieNeu-TTS python. Tiếp tục với fallback.");
            return false;
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

        // 1. Đo chính xác bằng ffprobe (chuẩn auto-compare-video)
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
                if (proc.ExitCode == 0 && double.TryParse(stdout.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var exactSec) && exactSec > 0.2)
                {
                    return Math.Round(Math.Clamp(exactSec, 1.0, 120.0), 2);
                }
            }
        }
        catch { }

        // 2. Fallback đọc WAV header hoặc ước lượng MP3 48kbps
        try
        {
            var info = new FileInfo(localAudioPath);
            var bytes = info.Length;

            if (localAudioPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
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
                        return Math.Round(Math.Max(1.0, duration), 2);
                    }
                }
            }

            var estimatedSeconds = bytes / 6000.0;
            return Math.Round(Math.Clamp(estimatedSeconds, 1.5, 60.0), 1);
        }
        catch
        {
            return 4.0;
        }
    }
}
