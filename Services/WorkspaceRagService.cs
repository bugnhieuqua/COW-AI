using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace COW_AI.Services;

public sealed record TrashManifestEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..10];
    public string OriginalRelPath { get; init; } = string.Empty;
    public string BackupRelPath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTime DeletedAtUtc { get; init; } = DateTime.UtcNow;
    public bool Restored { get; set; }
}

/// <summary>
/// Quản lý Workspace Jail, RAG Hybrid (MiniSearch + Trigram), phát hiện trùng lặp (SHA-256 + jscpd)
/// và Thùng rác an toàn (.cowai_trash) cho phép khôi phục.
/// </summary>
public sealed class WorkspaceRagService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<WorkspaceRagService> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly string[] ZipIgnoredSegments =
    [
        "node_modules", ".git", ".vs", ".vscode", "bin", "obj", "dist", "build",
        "__pycache__", ".venv", "venv", ".cowai_trash", ".cowai_index", ".kode"
    ];

    public WorkspaceRagService(IWebHostEnvironment env, ILogger<WorkspaceRagService> logger)
    {
        _env = env;
        _logger = logger;
    }

    public string GetUploadWorkspacePath(string sessionId)
    {
        var safeSession = SanitizeSegment(sessionId);
        return Path.GetFullPath(Path.Combine(_env.ContentRootPath, "App_Data", "Workspaces", safeSession));
    }

    public bool IsDangerousSystemRoot(string fullPath)
    {
        var norm = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(norm)) return true;

        var driveRoot = Path.GetPathRoot(norm)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(norm, driveRoot, StringComparison.OrdinalIgnoreCase)) return true;

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        string[] blocked = [windowsDir, progFiles, progFilesX86];
        foreach (var b in blocked)
        {
            if (string.IsNullOrWhiteSpace(b)) continue;
            var cleanB = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(norm, cleanB, StringComparison.OrdinalIgnoreCase) ||
                norm.StartsWith(cleanB + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Ép mọi thao tác đọc/ghi/xóa phải nằm gọn bên trong thư mục Workspace đã khóa (Workspace Jail).
    /// </summary>
    public string ResolveSafePath(string workspaceRoot, string? requestedPath, bool allowRoot = false)
    {
        var absRoot = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(absRoot))
            throw new DirectoryNotFoundException($"Thư mục workspace không tồn tại: {absRoot}");

        if (string.IsNullOrWhiteSpace(requestedPath) || requestedPath is "." or "./" or ".\\")
        {
            if (!allowRoot) throw new UnauthorizedAccessException("Đường dẫn không được trỏ tới thư mục gốc.");
            return absRoot;
        }

        var normalized = requestedPath.Trim().TrimStart('/', '\\');
        var candidate = Path.IsPathRooted(requestedPath)
            ? Path.GetFullPath(requestedPath)
            : Path.GetFullPath(Path.Combine(absRoot, normalized));

        var rel = Path.GetRelativePath(absRoot, candidate);
        if (string.Equals(rel, ".", StringComparison.Ordinal))
        {
            if (!allowRoot) throw new UnauthorizedAccessException("Không được thao tác phá hủy lên chính thư mục gốc.");
            return absRoot;
        }

        if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
        {
            throw new UnauthorizedAccessException($"Từ chối truy cập ngoài phạm vi Workspace Jail: {requestedPath}");
        }

        // Chặn reparse point / symlink trỏ ra ngoài
        var cur = candidate;
        while (!string.IsNullOrWhiteSpace(cur) &&
               cur.Length >= absRoot.Length &&
               !string.Equals(cur.TrimEnd('\\', '/'), absRoot.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(cur) || Directory.Exists(cur))
            {
                var attrs = File.GetAttributes(cur);
                if (attrs.HasFlag(FileAttributes.ReparsePoint))
                    throw new UnauthorizedAccessException($"Từ chối symlink/junction ngoài Workspace: {rel}");
            }
            cur = Path.GetDirectoryName(cur);
        }

        return candidate;
    }

    public string GetIndexCachePath(string workspaceRoot)
    {
        var absRoot = Path.GetFullPath(workspaceRoot);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(absRoot.ToLowerInvariant())))[..16];
        var cacheDir = Path.Combine(_env.ContentRootPath, "App_Data", "RagCache");
        Directory.CreateDirectory(cacheDir);
        return Path.Combine(cacheDir, $"{hash}.json");
    }

    public async Task<string> IndexWorkspaceAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var absRoot = Path.GetFullPath(workspaceRoot);
        var indexFile = GetIndexCachePath(absRoot);
        return await RunNodeWorkerAsync(["index", absRoot, indexFile], ct);
    }

    public async Task<string> SearchWorkspaceAsync(string workspaceRoot, string query, int topK = 8, CancellationToken ct = default)
    {
        var absRoot = Path.GetFullPath(workspaceRoot);
        var indexFile = GetIndexCachePath(absRoot);

        bool needReindex = !File.Exists(indexFile) ||
                           (DateTime.UtcNow - File.GetLastWriteTimeUtc(indexFile)) > TimeSpan.FromMinutes(10);
        if (needReindex)
        {
            await IndexWorkspaceAsync(absRoot, ct);
        }

        return await RunNodeWorkerAsync(["search", indexFile, query, topK.ToString()], ct);
    }

    public async Task<string> FindDuplicatesAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var absRoot = Path.GetFullPath(workspaceRoot);
        return await RunNodeWorkerAsync(["duplicates", absRoot], ct);
    }

    public async Task<(TrashManifestEntry Entry, List<TrashManifestEntry> AllEntries)> SoftDeleteToTrashAsync(
        string workspaceRoot,
        string sessionId,
        string relativePath,
        string reason,
        CancellationToken ct = default)
    {
        var absRoot = Path.GetFullPath(workspaceRoot);
        var fullTarget = ResolveSafePath(absRoot, relativePath, allowRoot: false);

        if (fullTarget.Contains($"{Path.DirectorySeparatorChar}.cowai_trash", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Không thể xóa tệp đã nằm trong .cowai_trash.");

        if (!File.Exists(fullTarget) && !Directory.Exists(fullTarget))
            throw new FileNotFoundException($"Không tìm thấy tệp/thư mục để xóa: {relativePath}");

        var relOriginal = Path.GetRelativePath(absRoot, fullTarget).Replace('\\', '/');
        var safeSession = SanitizeSegment(string.IsNullOrWhiteSpace(sessionId) ? "default" : sessionId);
        var trashDir = Path.Combine(absRoot, ".cowai_trash", safeSession);
        Directory.CreateDirectory(trashDir);

        var entryId = Guid.NewGuid().ToString("N")[..10];
        var backupFileName = $"{entryId}_{Path.GetFileName(fullTarget)}";
        var backupFullPath = Path.Combine(trashDir, backupFileName);
        var backupRelPath = Path.GetRelativePath(absRoot, backupFullPath).Replace('\\', '/');

        long sizeBytes = 0;
        if (File.Exists(fullTarget))
        {
            sizeBytes = new FileInfo(fullTarget).Length;
            File.Move(fullTarget, backupFullPath, overwrite: true);
        }
        else if (Directory.Exists(fullTarget))
        {
            sizeBytes = Directory.GetFiles(fullTarget, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
            Directory.Move(fullTarget, backupFullPath);
        }

        var entries = await LoadTrashManifestAsync(absRoot, safeSession, ct);
        var entry = new TrashManifestEntry
        {
            Id = entryId,
            OriginalRelPath = relOriginal,
            BackupRelPath = backupRelPath,
            SizeBytes = sizeBytes,
            Reason = string.IsNullOrWhiteSpace(reason) ? "AI Agent cleanup" : reason.Trim(),
            DeletedAtUtc = DateTime.UtcNow,
            Restored = false
        };
        entries.Add(entry);
        await SaveTrashManifestAsync(absRoot, safeSession, entries, ct);

        // Làm mới lại RAG index sau khi dọn tệp
        try { await IndexWorkspaceAsync(absRoot, ct); } catch { /* ignore */ }

        return (entry, entries);
    }

    public async Task<List<TrashManifestEntry>> RestoreFromTrashAsync(
        string workspaceRoot,
        string sessionId,
        string? trashId = null,
        CancellationToken ct = default)
    {
        var absRoot = Path.GetFullPath(workspaceRoot);
        var safeSession = SanitizeSegment(string.IsNullOrWhiteSpace(sessionId) ? "default" : sessionId);
        var entries = await LoadTrashManifestAsync(absRoot, safeSession, ct);
        var restoredNow = new List<TrashManifestEntry>();

        foreach (var item in entries.Where(x => !x.Restored))
        {
            if (!string.IsNullOrWhiteSpace(trashId) &&
                !string.Equals(item.Id, trashId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var backupFull = ResolveSafePath(absRoot, item.BackupRelPath, allowRoot: false);
            var destFull = ResolveSafePath(absRoot, item.OriginalRelPath, allowRoot: false);

            var destDir = Path.GetDirectoryName(destFull);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

            if (File.Exists(backupFull))
            {
                if (File.Exists(destFull))
                {
                    var ext = Path.GetExtension(destFull);
                    var stem = Path.GetFileNameWithoutExtension(destFull);
                    destFull = Path.Combine(destDir ?? absRoot, $"{stem}.restored_{item.Id}{ext}");
                }
                File.Move(backupFull, destFull);
                item.Restored = true;
                restoredNow.Add(item);
            }
            else if (Directory.Exists(backupFull))
            {
                if (!Directory.Exists(destFull))
                {
                    Directory.Move(backupFull, destFull);
                    item.Restored = true;
                    restoredNow.Add(item);
                }
            }
        }

        await SaveTrashManifestAsync(absRoot, safeSession, entries, ct);
        if (restoredNow.Count > 0)
        {
            try { await IndexWorkspaceAsync(absRoot, ct); } catch { /* ignore */ }
        }
        return restoredNow;
    }

    public async Task<List<TrashManifestEntry>> LoadTrashManifestAsync(string workspaceRoot, string sessionId, CancellationToken ct = default)
    {
        var safeSession = SanitizeSegment(string.IsNullOrWhiteSpace(sessionId) ? "default" : sessionId);
        var manifestPath = Path.Combine(Path.GetFullPath(workspaceRoot), ".cowai_trash", safeSession, "manifest.json");
        if (!File.Exists(manifestPath)) return [];
        try
        {
            var json = await File.ReadAllTextAsync(manifestPath, ct);
            return JsonSerializer.Deserialize<List<TrashManifestEntry>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task SaveTrashManifestAsync(string workspaceRoot, string sessionId, List<TrashManifestEntry> entries, CancellationToken ct)
    {
        var safeSession = SanitizeSegment(string.IsNullOrWhiteSpace(sessionId) ? "default" : sessionId);
        var trashDir = Path.Combine(Path.GetFullPath(workspaceRoot), ".cowai_trash", safeSession);
        Directory.CreateDirectory(trashDir);
        var manifestPath = Path.Combine(trashDir, "manifest.json");
        var json = JsonSerializer.Serialize(entries, JsonOpts);
        await File.WriteAllTextAsync(manifestPath, json, ct);
    }

    public async Task<byte[]> CreateWorkspaceZipBytesAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var absRoot = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(absRoot))
            throw new DirectoryNotFoundException("Không tìm thấy thư mục để nén tải về.");

        return await Task.Run(() =>
        {
            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in Directory.EnumerateFiles(absRoot, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    var rel = Path.GetRelativePath(absRoot, file).Replace('\\', '/');
                    var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Any(p => ZipIgnoredSegments.Contains(p, StringComparer.OrdinalIgnoreCase)))
                        continue;

                    var entry = archive.CreateEntry(rel, CompressionLevel.Fastest);
                    using var entryStream = entry.Open();
                    using var fs = File.OpenRead(file);
                    fs.CopyTo(entryStream);
                }
            }
            return ms.ToArray();
        }, ct);
    }

    private async Task<string> RunNodeWorkerAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var workerScript = Path.Combine(_env.ContentRootPath, "Tools", "agent-mcp", "rag-agent-worker.mjs");
        if (!File.Exists(workerScript))
            throw new FileNotFoundException($"Thiếu worker RAG Node.js tại {workerScript}");

        var psi = new ProcessStartInfo
        {
            FileName = "node",
            WorkingDirectory = Path.Combine(_env.ContentRootPath, "Tools", "agent-mcp"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(workerScript);
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Không khởi động được Node RAG worker.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(45));

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);
        await proc.WaitForExitAsync(cts.Token);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (proc.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
        {
            _logger.LogWarning("RAG worker exited {Code}: {Stderr}", proc.ExitCode, stderr);
            throw new InvalidOperationException($"RAG worker thất bại (code {proc.ExitCode}): {stderr}");
        }
        return stdout.Trim();
    }

    public static string SanitizeSegment(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_');
        }
        var res = sb.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(res) ? "default" : res[..Math.Min(64, res.Length)];
    }
}
