using System.Text.Json;
using System.Text.Json.Serialization;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Tools;

namespace COW_AI.Services.AgentTools;

public sealed class RagSearchArgs
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("topK")]
    public int TopK { get; set; } = 8;
}

/// <summary>
/// Tìm kiếm ngữ nghĩa + từ khóa (MiniSearch BM25 + Trigram Cosine + bỏ dấu Tiếng Việt) trên toàn bộ Workspace.
/// </summary>
public sealed class RagSearchTool : ToolBase<RagSearchArgs>
{
    private readonly WorkspaceRagService _ragService;

    public RagSearchTool(WorkspaceRagService ragService)
    {
        _ragService = ragService;
    }

    public override string Name => "rag_search";

    public override string Description =>
        "Tìm kiếm thông minh kết hợp BM25 + ngữ nghĩa trên toàn bộ mã nguồn và tài liệu trong thư mục workspace đã gắn.";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "Từ khóa, tên hàm/class hoặc mô tả ngữ nghĩa cần tìm." },
            topK = new { type = "integer", description = "Số đoạn khớp tối đa trả về (mặc định 8)." }
        },
        required = new[] { "query" }
    };

    public override ToolAttributes Attributes => new() { ReadOnly = true, NoEffect = true, RequiresApproval = false };

    protected override async Task<ToolResult> ExecuteAsync(
        RagSearchArgs arguments,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(arguments.Query))
            return ToolResult.Fail("Thiếu tham số query.");

        var root = context.Sandbox.WorkingDirectory;
        var json = await _ragService.SearchWorkspaceAsync(
            root,
            arguments.Query,
            Math.Clamp(arguments.TopK <= 0 ? 8 : arguments.TopK, 1, 20),
            cancellationToken);

        return ToolResult.Ok(json);
    }
}

public sealed class FindDuplicatesArgs
{
    [JsonPropertyName("includeCodeClones")]
    public bool IncludeCodeClones { get; set; } = true;
}

/// <summary>
/// Phát hiện tệp trùng lặp 100% (SHA-256) và đoạn code sao chép (jscpd).
/// </summary>
public sealed class FindDuplicatesTool : ToolBase<FindDuplicatesArgs>
{
    private readonly WorkspaceRagService _ragService;

    public FindDuplicatesTool(WorkspaceRagService ragService)
    {
        _ragService = ragService;
    }

    public override string Name => "find_duplicates";

    public override string Description =>
        "Quét toàn bộ workspace để phát hiện tệp trùng lặp 100% nội dung (SHA-256) và đoạn code trùng lặp cấu trúc (jscpd), đồng thời đề xuất giữ 1 bản gốc (keepPath) và danh sách bản thừa (duplicatePaths).";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            includeCodeClones = new { type = "boolean", description = "Có quét cả clone cấu trúc code bằng jscpd không (mặc định true)." }
        }
    };

    public override ToolAttributes Attributes => new() { ReadOnly = true, NoEffect = true, RequiresApproval = false };

    protected override async Task<ToolResult> ExecuteAsync(
        FindDuplicatesArgs arguments,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        var root = context.Sandbox.WorkingDirectory;
        var json = await _ragService.FindDuplicatesAsync(root, cancellationToken);
        return ToolResult.Ok(json);
    }
}

public sealed class SafeDeleteToTrashArgs
{
    [JsonPropertyName("paths")]
    public List<string>? Paths { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "Dọn tệp trùng lặp / dư thừa";
}

/// <summary>
/// Xóa mềm tệp/thư mục vào vùng cách ly .cowai_trash (có thể hoàn tác 1-click). Cần người dùng xác nhận khi Auto-Approve đang tắt.
/// </summary>
public sealed class SafeDeleteToTrashTool : ToolBase<SafeDeleteToTrashArgs>
{
    private readonly WorkspaceRagService _ragService;
    private readonly Func<string, string?>? _sessionResolver;

    public SafeDeleteToTrashTool(WorkspaceRagService ragService, Func<string, string?>? sessionResolver = null)
    {
        _ragService = ragService;
        _sessionResolver = sessionResolver;
    }

    public override string Name => "safe_delete_to_trash";

    public override string Description =>
        "Di chuyển an toàn một hoặc nhiều tệp trùng lặp/dư thừa vào thùng rác .cowai_trash/{sessionId} bên trong workspace để có thể hoàn tác, thay vì xóa vĩnh viễn.";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            paths = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Danh sách đường dẫn tương đối trong workspace cần xóa mềm."
            },
            path = new
            {
                type = "string",
                description = "Hoặc một đường dẫn tương đối duy nhất cần xóa mềm."
            },
            reason = new { type = "string", description = "Lý do xóa (ví dụ: Bản sao SHA-256 của MathHelper.cs)." }
        }
    };

    public override ToolAttributes Attributes => new()
    {
        ReadOnly = false,
        NoEffect = false,
        RequiresApproval = true,
        AllowParallel = false,
        PermissionCategory = "delete"
    };

    protected override async Task<ToolResult> ExecuteAsync(
        SafeDeleteToTrashArgs arguments,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        var targets = new List<string>();
        if (arguments.Paths != null) targets.AddRange(arguments.Paths);
        if (!string.IsNullOrWhiteSpace(arguments.Path)) targets.Add(arguments.Path);

        var root = context.Sandbox.WorkingDirectory;
        var expanded = new List<string>();
        string[] ignoredDirs = ["node_modules", ".git", "bin", "obj", ".vs", ".cowai_trash", ".kode"];
        foreach (var raw in targets)
        {
            var trimmed = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            if (trimmed is "*" or "*.*" or "." or "./" or ".\\")
            {
                foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                    if (rel.Split('/').Any(seg => ignoredDirs.Contains(seg, StringComparer.OrdinalIgnoreCase))) continue;
                    expanded.Add(rel);
                }
            }
            else
            {
                expanded.Add(trimmed);
            }
        }

        targets = expanded.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (targets.Count == 0)
            return ToolResult.Fail("Vui lòng truyền danh sách `paths` hoặc `path` của tệp cần xóa mềm (hiện không có tệp hợp lệ).");

        var sessionId = _sessionResolver?.Invoke(context.AgentId) ?? context.AgentId;
        var moved = new List<object>();

        foreach (var relPath in targets)
        {
            var (entry, _) = await _ragService.SoftDeleteToTrashAsync(
                root,
                sessionId,
                relPath,
                arguments.Reason,
                cancellationToken);

            moved.Add(new
            {
                id = entry.Id,
                originalPath = entry.OriginalRelPath,
                backupPath = entry.BackupRelPath,
                sizeBytes = entry.SizeBytes
            });
        }

        return ToolResult.Ok(JsonSerializer.Serialize(new
        {
            success = true,
            sessionId,
            movedCount = moved.Count,
            moved
        }));
    }
}

public sealed class RestoreFromTrashArgs
{
    [JsonPropertyName("trashId")]
    public string? TrashId { get; set; }
}

/// <summary>
/// Khôi phục tệp đã xóa mềm từ .cowai_trash về vị trí cũ.
/// </summary>
public sealed class RestoreFromTrashTool : ToolBase<RestoreFromTrashArgs>
{
    private readonly WorkspaceRagService _ragService;
    private readonly Func<string, string?>? _sessionResolver;

    public RestoreFromTrashTool(WorkspaceRagService ragService, Func<string, string?>? sessionResolver = null)
    {
        _ragService = ragService;
        _sessionResolver = sessionResolver;
    }

    public override string Name => "restore_from_trash";

    public override string Description =>
        "Khôi phục lại các tệp đã di chuyển vào .cowai_trash của phiên hiện tại (truyền trashId để khôi phục 1 tệp hoặc bỏ trống để khôi phục toàn bộ).";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            trashId = new { type = "string", description = "ID bản sao trong thùng rác (bỏ trống để khôi phục tất cả)." }
        }
    };

    public override ToolAttributes Attributes => new()
    {
        ReadOnly = false,
        NoEffect = false,
        RequiresApproval = false
    };

    protected override async Task<ToolResult> ExecuteAsync(
        RestoreFromTrashArgs arguments,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        var root = context.Sandbox.WorkingDirectory;
        var sessionId = _sessionResolver?.Invoke(context.AgentId) ?? context.AgentId;
        var restored = await _ragService.RestoreFromTrashAsync(root, sessionId, arguments.TrashId, cancellationToken);
        return ToolResult.Ok(JsonSerializer.Serialize(new
        {
            success = true,
            restoredCount = restored.Count,
            restoredPaths = restored.Select(x => x.OriginalRelPath).ToArray()
        }));
    }
}
