using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace COW_AI.Services;

public record DirectoryEntryInfo(string Name, string FullPath, bool HasChildren);

public record DirectoryBrowseResult(
    bool Success,
    string? Error,
    string CurrentPath,
    string? ParentPath,
    List<DirectoryEntryInfo> Directories
);

public record DirectoryReadResult(
    bool Success,
    string? Error,
    string FolderName,
    string FullPath,
    string Engine,
    string Tree,
    int FileCount,
    int TotalChars,
    bool Truncated,
    string PackedContent,
    List<string> Files
);

/// <summary>
/// Dịch vụ cho phép AI Agents truy cập và đọc cấu trúc + nội dung thư mục.
/// Tích hợp trực tiếp 2 repo mã nguồn mở:
/// 1) yamadashy/repomix (đóng gói ngữ cảnh thư mục tối ưu LLM, tôn trọng .gitignore, quét secret)
/// 2) @modelcontextprotocol/server-filesystem (giao thức MCP JSON-RPC stdio chuẩn Anthropic)
/// </summary>
public class DirectoryAgentService
{
    private const int MaxPackedChars = 65_000;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<DirectoryAgentService> _logger;

    private static readonly HashSet<string> IgnoredDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".vs", ".vscode", ".idea", "bin", "obj", "dist", "build",
        "__pycache__", ".venv", "venv", "$RECYCLE.BIN", "System Volume Information", "Recovery",
        "Windows", "Program Files", "Program Files (x86)", "ProgramData", "AppData", "App_Data", ".cowai_trash", ".kode"
    };

    public DirectoryAgentService(IWebHostEnvironment env, ILogger<DirectoryAgentService> logger)
    {
        _env = env;
        _logger = logger;
    }

    public DirectoryBrowseResult BrowseDirectories(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                var entries = new List<DirectoryEntryInfo>();

                void TryAddSpecial(string label, Environment.SpecialFolder folder)
                {
                    try
                    {
                        var p = Environment.GetFolderPath(folder);
                        if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                        {
                            entries.Add(new DirectoryEntryInfo(label, p, true));
                        }
                    }
                    catch { }
                }

                TryAddSpecial("📌 Desktop (Màn hình chính)", Environment.SpecialFolder.DesktopDirectory);
                TryAddSpecial("📌 Documents (Tài liệu)", Environment.SpecialFolder.MyDocuments);
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrWhiteSpace(userProfile))
                {
                    var downloads = Path.Combine(userProfile, "Downloads");
                    if (Directory.Exists(downloads))
                    {
                        entries.Add(new DirectoryEntryInfo("📌 Downloads (Tải xuống)", downloads, true));
                    }
                }

                var workspacePath = _env.ContentRootPath;
                if (Directory.Exists(workspacePath))
                {
                    entries.Add(new DirectoryEntryInfo($"[Dự án hiện tại] {Path.GetFileName(workspacePath)}", workspacePath, true));
                }

                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                        ? $"Ổ đĩa ({drive.Name.TrimEnd('\\')})"
                        : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
                    entries.Add(new DirectoryEntryInfo(label, drive.RootDirectory.FullName, true));
                }

                return new DirectoryBrowseResult(true, null, string.Empty, null, entries);
            }

            var fullPath = Path.GetFullPath(path.Trim().Trim('"', '\''));
            if (!Directory.Exists(fullPath))
            {
                return new DirectoryBrowseResult(false, $"Không tìm thấy thư mục: {fullPath}", fullPath, null, new List<DirectoryEntryInfo>());
            }

            var parentDir = Directory.GetParent(fullPath)?.FullName ?? string.Empty;
            var dirs = new List<DirectoryEntryInfo>();
            foreach (var dir in Directory.EnumerateDirectories(fullPath).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(name) || IgnoredDirNames.Contains(name)) continue;
                try
                {
                    var attrs = File.GetAttributes(dir);
                    if ((attrs & FileAttributes.System) != 0) continue;
                    bool hasSub = false;
                    try { hasSub = Directory.EnumerateDirectories(dir).Any(); } catch { }
                    dirs.Add(new DirectoryEntryInfo(name, dir, hasSub));
                }
                catch { }
            }

            return new DirectoryBrowseResult(true, null, fullPath, parentDir, dirs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi duyệt thư mục {Path}", path);
            return new DirectoryBrowseResult(false, $"Không thể truy cập thư mục: {ex.Message}", path ?? string.Empty, null, new List<DirectoryEntryInfo>());
        }
    }

    public async Task<DirectoryReadResult> ReadDirectoryAsync(string directoryPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return new DirectoryReadResult(false, "Đường dẫn thư mục không được để trống.", string.Empty, string.Empty, string.Empty, string.Empty, 0, 0, false, string.Empty, new List<string>());
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(directoryPath.Trim().Trim('"', '\''));
        }
        catch (Exception ex)
        {
            return new DirectoryReadResult(false, $"Đường dẫn không hợp lệ: {ex.Message}", string.Empty, directoryPath, string.Empty, string.Empty, 0, 0, false, string.Empty, new List<string>());
        }

        if (!Directory.Exists(fullPath))
        {
            return new DirectoryReadResult(false, $"Thư mục không tồn tại: {fullPath}", Path.GetFileName(fullPath), fullPath, string.Empty, string.Empty, 0, 0, false, string.Empty, new List<string>());
        }

        var folderName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(folderName)) folderName = fullPath;

        // 1. Lấy cây thư mục qua @modelcontextprotocol/server-filesystem (MCP JSON-RPC stdio)
        var mcpFiles = new List<string>();
        var mcpTree = await TryGetMcpDirectoryTreeAsync(fullPath, mcpFiles, ct);

        // 2. Đóng gói nội dung thư mục qua yamadashy/repomix
        var repomixMarkdown = await TryPackWithRepomixAsync(fullPath, ct);

        var usedEngines = new List<string>();
        if (!string.IsNullOrWhiteSpace(repomixMarkdown)) usedEngines.Add("Repomix");
        if (!string.IsNullOrWhiteSpace(mcpTree)) usedEngines.Add("MCP Filesystem");
        var engineLabel = usedEngines.Count > 0 ? string.Join(" + ", usedEngines) : "Filesystem Fallback";

        if (string.IsNullOrWhiteSpace(repomixMarkdown))
        {
            // Fallback an toàn nếu repomix không đọc được (ví dụ thư mục chỉ toàn tệp nhỏ hoặc ngoài git)
            repomixMarkdown = BuildLightweightFallbackMarkdown(fullPath, mcpTree, mcpFiles);
        }

        if (mcpFiles.Count == 0)
        {
            CollectFilesFallback(fullPath, fullPath, mcpFiles, 0);
        }

        bool truncated = false;
        var finalPacked = repomixMarkdown;
        if (finalPacked.Length > MaxPackedChars)
        {
            truncated = true;
            var headerNote = $"[LƯU Ý: Nội dung thư mục được rút gọn xuống {MaxPackedChars:N0} / {finalPacked.Length:N0} ký tự để tối ưu ngữ cảnh AI.]\n\n";
            var treePrefix = !string.IsNullOrWhiteSpace(mcpTree)
                ? $"## Cây thư mục (MCP Filesystem)\n```text\n{mcpTree}\n```\n\n"
                : string.Empty;
            var budget = Math.Max(4000, MaxPackedChars - headerNote.Length - treePrefix.Length);
            finalPacked = headerNote + treePrefix + finalPacked[..Math.Min(budget, finalPacked.Length)];
        }
        else if (!string.IsNullOrWhiteSpace(mcpTree) && !finalPacked.Contains("Directory Structure", StringComparison.OrdinalIgnoreCase))
        {
            finalPacked = $"## Cây thư mục (MCP Filesystem)\n```text\n{mcpTree}\n```\n\n{finalPacked}";
        }

        _logger.LogInformation("DirectoryAgentService đã đọc thư mục '{Path}' bằng [{Engine}] ({Files} files, {Chars} chars)",
            fullPath, engineLabel, mcpFiles.Count, finalPacked.Length);

        return new DirectoryReadResult(
            true,
            null,
            folderName,
            fullPath,
            engineLabel,
            mcpTree,
            mcpFiles.Count,
            finalPacked.Length,
            truncated,
            finalPacked,
            mcpFiles.Take(200).ToList()
        );
    }

    public static string BuildAgentFolderPrompt(string userQuestion, DirectoryReadResult dir)
    {
        var question = string.IsNullOrWhiteSpace(userQuestion)
            ? "Hãy phân tích cấu trúc, mục đích và các thành phần chính của thư mục này."
            : userQuestion.Trim();

        return $@"Bạn là AI Agent kỹ thuật đang truy cập và đọc thư mục: `{dir.FullPath}` (công cụ tích hợp: {dir.Engine}).
Tổng số tệp phát hiện: {dir.FileCount}.

CÂU HỎI / YÊU CẦU CỦA NGƯỜI DÙNG:
{question}

--- NGỮ CẢNH THƯ MỤC ĐÃ ĐỌC ({dir.FolderName}) ---
{dir.PackedContent}
--- HẾT NGỮ CẢNH THƯ MỤC ---

Hãy trả lời chính xác dựa trên các tệp và cấu trúc thư mục thực tế ở trên (nêu rõ tên tệp khi trích dẫn).";
    }

    private string? ResolveMcpFilesystemEntrypoint()
    {
        var localPath = Path.Combine(_env.ContentRootPath, "Tools", "agent-mcp", "node_modules", "@modelcontextprotocol", "server-filesystem", "dist", "index.js");
        return File.Exists(localPath) ? localPath : null;
    }

    private string? ResolveRepomixEntrypoint()
    {
        var localPath = Path.Combine(_env.ContentRootPath, "Tools", "agent-mcp", "node_modules", "repomix", "bin", "repomix.cjs");
        return File.Exists(localPath) ? localPath : null;
    }

    private async Task<string> TryGetMcpDirectoryTreeAsync(string targetDir, List<string> collectedFiles, CancellationToken ct)
    {
        var mcpEntry = ResolveMcpFilesystemEntrypoint();
        if (string.IsNullOrWhiteSpace(mcpEntry)) return string.Empty;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = $"\"{mcpEntry}\" \"{targetDir}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var proc = Process.Start(psi);
            if (proc == null) return string.Empty;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            await proc.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "COW-AI", version = "1.0.0" }
                }
            }));
            await proc.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized"
            }));
            await proc.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new
                {
                    name = "directory_tree",
                    arguments = new
                    {
                        path = targetDir,
                        excludePatterns = new[] { "node_modules", ".git", "bin", "obj", "dist", ".vs", "__pycache__", ".venv" }
                    }
                }
            }));
            await proc.StandardInput.FlushAsync();

            string? treeJsonText = null;
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                var line = await proc.StandardOutput.ReadLineAsync(timeoutCts.Token);
                if (line == null) break;
                if (!line.TrimStart().StartsWith('{')) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number && idProp.GetInt32() == 2)
                    {
                        if (doc.RootElement.TryGetProperty("result", out var res) &&
                            res.TryGetProperty("content", out var contentArr) &&
                            contentArr.ValueKind == JsonValueKind.Array &&
                            contentArr.GetArrayLength() > 0)
                        {
                            treeJsonText = contentArr[0].GetProperty("text").GetString();
                        }
                        break;
                    }
                }
                catch { }
            }

            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }

            if (string.IsNullOrWhiteSpace(treeJsonText)) return string.Empty;

            using var treeDoc = JsonDocument.Parse(treeJsonText);
            var sb = new StringBuilder();
            FormatMcpTreeElement(treeDoc.RootElement, string.Empty, string.Empty, sb, collectedFiles, 0);
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MCP Filesystem directory_tree không phản hồi, chuyển sang fallback.");
            return string.Empty;
        }
    }

    private static void FormatMcpTreeElement(JsonElement elem, string indent, string relPrefix, StringBuilder sb, List<string> files, int depth)
    {
        if (depth > 8 || sb.Length > 20_000) return;
        if (elem.ValueKind != JsonValueKind.Array) return;

        var items = elem.EnumerateArray().Take(120).ToList();
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var name = item.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
            var type = item.TryGetProperty("type", out var tProp) ? tProp.GetString() ?? "file" : "file";
            if (string.IsNullOrWhiteSpace(name) || IgnoredDirNames.Contains(name)) continue;

            bool isLast = i == items.Count - 1;
            string connector = isLast ? "└── " : "├── ";
            string nextIndent = indent + (isLast ? "    " : "│   ");
            string relPath = string.IsNullOrEmpty(relPrefix) ? name : $"{relPrefix}/{name}";

            if (type == "directory")
            {
                sb.AppendLine($"{indent}{connector}{name}/");
                if (item.TryGetProperty("children", out var children))
                {
                    FormatMcpTreeElement(children, nextIndent, relPath, sb, files, depth + 1);
                }
            }
            else
            {
                sb.AppendLine($"{indent}{connector}{name}");
                files.Add(relPath);
            }
        }
    }

    private async Task<string> TryPackWithRepomixAsync(string targetDir, CancellationToken ct)
    {
        var repomixEntry = ResolveRepomixEntrypoint();
        var tempOut = Path.Combine(Path.GetTempPath(), $"cowai_repomix_{Guid.NewGuid():N}.md");

        try
        {
            var fileName = !string.IsNullOrWhiteSpace(repomixEntry) ? "node" : "npx";
            var ignorePattern = "**/node_modules/**,**/bin/**,**/obj/**,**/.git/**,**/dist/**,**/.vs/**,**/*.exe,**/*.dll,**/*.pdb,**/*.png,**/*.jpg,**/*.jpeg,**/*.webp,**/*.gif,**/*.ico,**/*.mp4,**/*.mp3,**/*.wav,**/*.zip,**/*.db,**/*.woff2,**/*.woff";
            var prefixArgs = !string.IsNullOrWhiteSpace(repomixEntry) ? $"\"{repomixEntry}\"" : "-y repomix";
            var args = $"{prefixArgs} \"{targetDir}\" --style markdown --output \"{tempOut}\" --ignore \"{ignorePattern}\" --quiet";

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                WorkingDirectory = Path.GetTempPath(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var proc = Process.Start(psi);
            if (proc == null) return string.Empty;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(25));

            var stderrTask = proc.StandardError.ReadToEndAsync(timeoutCts.Token);
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(timeoutCts.Token);

            await proc.WaitForExitAsync(timeoutCts.Token);
            if (File.Exists(tempOut))
            {
                var content = await File.ReadAllTextAsync(tempOut, Encoding.UTF8, ct);
                return content.Trim();
            }

            var err = await stderrTask;
            if (!string.IsNullOrWhiteSpace(err))
            {
                _logger.LogDebug("Repomix stderr: {Err}", err);
            }
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Repomix pack thất bại cho thư mục {Dir}", targetDir);
            return string.Empty;
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }

    private static void CollectFilesFallback(string rootDir, string currentDir, List<string> files, int depth)
    {
        if (depth > 6 || files.Count >= 300) return;
        try
        {
            foreach (var f in Directory.EnumerateFiles(currentDir))
            {
                files.Add(Path.GetRelativePath(rootDir, f).Replace('\\', '/'));
                if (files.Count >= 300) return;
            }
            foreach (var d in Directory.EnumerateDirectories(currentDir))
            {
                var name = Path.GetFileName(d);
                if (IgnoredDirNames.Contains(name)) continue;
                CollectFilesFallback(rootDir, d, files, depth + 1);
            }
        }
        catch { }
    }

    private static string BuildLightweightFallbackMarkdown(string rootDir, string tree, List<string> files)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(tree))
        {
            sb.AppendLine("## Directory Structure");
            sb.AppendLine("```text");
            sb.AppendLine(tree);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        var textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".cshtml", ".js", ".ts", ".jsx", ".tsx", ".py", ".json", ".md", ".txt", ".html", ".css", ".sql", ".xml", ".yaml", ".yml", ".csproj", ".env.example"
        };

        int chars = 0;
        foreach (var rel in files.Take(40))
        {
            if (chars > 45_000) break;
            var full = Path.Combine(rootDir, rel.Replace('/', Path.DirectorySeparatorChar));
            var ext = Path.GetExtension(full);
            if (!textExtensions.Contains(ext) || !File.Exists(full)) continue;
            try
            {
                var info = new FileInfo(full);
                if (info.Length > 200_000) continue;
                var txt = File.ReadAllText(full, Encoding.UTF8);
                sb.AppendLine($"### File: `{rel}`");
                sb.AppendLine("```" + ext.TrimStart('.'));
                sb.AppendLine(txt);
                sb.AppendLine("```");
                sb.AppendLine();
                chars += txt.Length;
            }
            catch { }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Mở hộp thoại chọn thư mục gốc của Windows (FolderBrowserDialog chạy trên STA thread, TopMost)
    /// để lấy đường dẫn ổ đĩa thật 100% khi chạy trên máy cục bộ.
    /// </summary>
    public async Task<(bool Success, string? SelectedPath, string? Error)> PickNativeFolderOnWindowsAsync(
        string? initialPath = null,
        CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return (false, null, "Hộp thoại chọn thư mục hệ điều hành chỉ hỗ trợ trên Windows.");
        }

        try
        {
            var safeInit = (initialPath ?? string.Empty).Replace("'", "''");
            var psScript = $@"
Add-Type -AssemblyName System.Windows.Forms
$topForm = New-Object System.Windows.Forms.Form
$topForm.TopMost = $true
$topForm.ShowInTaskbar = $false
$topForm.Width = 1
$topForm.Height = 1
$topForm.StartPosition = 'CenterScreen'
$dlg = New-Object System.Windows.Forms.FolderBrowserDialog
$dlg.Description = 'Chọn thư mục dự án cho COW.AI'
$dlg.ShowNewFolderButton = $true
if ('{safeInit}' -ne '' -and (Test-Path '{safeInit}')) {{ $dlg.SelectedPath = '{safeInit}' }}
$res = $dlg.ShowDialog($topForm)
$topForm.Dispose()
if ($res -eq [System.Windows.Forms.DialogResult]::OK) {{
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    Write-Output $dlg.SelectedPath
}}
";
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -STA -WindowStyle Hidden -Command \"{psScript.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return (false, null, "Không thể mở hộp thoại chọn thư mục Windows.");
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(10));
            var output = (await proc.StandardOutput.ReadToEndAsync(cts.Token)).Trim();
            await proc.WaitForExitAsync(cts.Token);

            if (string.IsNullOrWhiteSpace(output))
            {
                return (false, null, "Đã hủy chọn thư mục.");
            }

            var fullPath = Path.GetFullPath(output);
            return Directory.Exists(fullPath)
                ? (true, fullPath, null)
                : (false, null, $"Thư mục không tồn tại: {fullPath}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi mở hộp thoại chọn thư mục Windows");
            return (false, null, $"Không thể mở hộp thoại chọn thư mục: {ex.Message}");
        }
    }

    /// <summary>
    /// Tự động dò tìm thư mục thật trên các ổ đĩa cục bộ (Desktop, Documents, Downloads, D:\, G:\, C:\)
    /// dựa theo tên thư mục và danh sách tệp con tương đối khi người dùng chọn qua hộp thoại trình duyệt (webkitdirectory).
    /// </summary>
    public string? TryResolveRealLocalFolder(
        string? folderName,
        IReadOnlyList<(string RelPath, long Size)> files)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return null;
        var targetName = folderName.Trim();
        if (IgnoredDirNames.Contains(targetName)) return null;

        var uploadWorkspaceRoot = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "App_Data", "workspaces"));
        var searchRoots = new List<string>();

        void AddRootIfExists(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            try
            {
                var full = Path.GetFullPath(p);
                if (Directory.Exists(full) && !searchRoots.Contains(full, StringComparer.OrdinalIgnoreCase))
                {
                    searchRoots.Add(full);
                }
            }
            catch { }
        }

        AddRootIfExists(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddRootIfExists(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            AddRootIfExists(Path.Combine(userProfile, "Downloads"));
            AddRootIfExists(Path.Combine(userProfile, "source", "repos"));
            AddRootIfExists(userProfile);
        }
        AddRootIfExists(Directory.GetParent(_env.ContentRootPath)?.FullName);
        AddRootIfExists(_env.ContentRootPath);

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            AddRootIfExists(drive.RootDirectory.FullName);
        }

        var candidates = new List<string>();
        var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sw = Stopwatch.StartNew();

        void SearchRecursive(string currentDir, int depth, int maxDepth)
        {
            if (sw.ElapsedMilliseconds > 1800 || candidates.Count >= 25) return;
            if (currentDir.StartsWith(uploadWorkspaceRoot, StringComparison.OrdinalIgnoreCase)) return;

            try
            {
                var directCandidate = Path.Combine(currentDir, targetName);
                if (Directory.Exists(directCandidate) &&
                    !directCandidate.StartsWith(uploadWorkspaceRoot, StringComparison.OrdinalIgnoreCase) &&
                    seenDirs.Add(directCandidate))
                {
                    candidates.Add(directCandidate);
                }
            }
            catch { }

            if (depth >= maxDepth) return;

            IEnumerable<string> subDirs;
            try { subDirs = Directory.EnumerateDirectories(currentDir); }
            catch { return; }

            foreach (var sub in subDirs)
            {
                if (sw.ElapsedMilliseconds > 1800) break;
                var name = Path.GetFileName(sub);
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith('.') || IgnoredDirNames.Contains(name))
                    continue;
                SearchRecursive(sub, depth + 1, maxDepth);
            }
        }

        foreach (var root in searchRoots)
        {
            var isDriveRoot = Path.GetPathRoot(root)?.Equals(root, StringComparison.OrdinalIgnoreCase) == true;
            SearchRecursive(root, 0, isDriveRoot ? 2 : 3);
        }

        if (candidates.Count == 0) return null;
        if (files == null || files.Count == 0)
        {
            return candidates.Count == 1 ? candidates[0] : null;
        }

        string? bestCandidate = null;
        int bestScore = 0;
        var sampleFiles = files.Take(25).ToList();

        foreach (var cand in candidates)
        {
            int score = 0;
            foreach (var (rel, size) in sampleFiles)
            {
                var cleanRel = rel.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                var fullFile = Path.Combine(cand, cleanRel);
                if (File.Exists(fullFile))
                {
                    score += 3;
                    try
                    {
                        if (size >= 0 && new FileInfo(fullFile).Length == size)
                        {
                            score += 2;
                        }
                    }
                    catch { }
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestCandidate = cand;
            }
        }

        if (bestCandidate != null && bestScore >= 3)
        {
            _logger.LogInformation("TryResolveRealLocalFolder đã ánh xạ thư mục '{Folder}' -> đường dẫn thật '{RealPath}' (score={Score})",
                targetName, bestCandidate, bestScore);
            return bestCandidate;
        }

        // Trường hợp chỉ có 1 thư mục duy nhất trùng tên trên máy (ví dụ thư mục người dùng vừa tạo/xóa file)
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        return null;
    }
}
