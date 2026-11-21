using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using COW_AI.Data;
using COW_AI.Models;
using COW_AI.Services.AgentTools;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Agent;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Sdk.Infrastructure.Providers;
using Kode.Agent.Sdk.Infrastructure.Sandbox;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Store.Json;
using Kode.Agent.Tools.Builtin;
using Microsoft.EntityFrameworkCore;

namespace COW_AI.Services;

/// <summary>
/// AI Agent tự trị tích hợp Kode.Agent.Sdk (v0.1.3), bị khóa 1-1 vào Workspace của từng phiên Chat,
/// kết hợp NLP Intent Classifier + RAG Hybrid + Safe Trash.
/// </summary>
public sealed class KodeWorkspaceAgentService
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkspaceRagService _ragService;
    private readonly AgentIntentClassifier _intentClassifier;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<KodeWorkspaceAgentService> _logger;

    private readonly ConcurrentDictionary<string, string> _agentToSession = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PendingToolApproval> _pendingApprovals = new(StringComparer.OrdinalIgnoreCase);

    private sealed record PendingToolApproval(
        string ApprovalId,
        string SessionId,
        string CallId,
        string ToolName,
        Agent Agent,
        Func<string, PermissionRespondOptions?, Task>? Respond,
        TaskCompletionSource<bool> DecisionTcs);

    public KodeWorkspaceAgentService(
        IConfiguration config,
        IWebHostEnvironment env,
        IServiceScopeFactory scopeFactory,
        WorkspaceRagService ragService,
        AgentIntentClassifier intentClassifier,
        ILoggerFactory loggerFactory)
    {
        _config = config;
        _env = env;
        _scopeFactory = scopeFactory;
        _ragService = ragService;
        _intentClassifier = intentClassifier;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<KodeWorkspaceAgentService>();
    }

    public string? ResolveSessionIdByAgent(string agentId)
    {
        return _agentToSession.TryGetValue(agentId, out var sid) ? sid : null;
    }

    public async Task<(bool Found, bool Approved, string Message)> ResolveToolApprovalAsync(
        string approvalId,
        bool approved,
        string? note = null)
    {
        if (!_pendingApprovals.TryRemove(approvalId, out var pending))
        {
            return (false, false, "Yêu cầu xác nhận đã hết hạn hoặc không tồn tại.");
        }

        try
        {
            if (pending.Respond != null)
            {
                await pending.Respond(
                    approved ? "allow" : "deny",
                    string.IsNullOrWhiteSpace(note) ? null : new PermissionRespondOptions { Note = note });
            }
            else if (approved)
            {
                await pending.Agent.ApproveToolCallAsync(pending.CallId);
            }
            else
            {
                await pending.Agent.DenyToolCallAsync(pending.CallId, note ?? "Người dùng từ chối thao tác.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi phản hồi Permission cho CallId {CallId}", pending.CallId);
        }
        finally
        {
            pending.DecisionTcs.TrySetResult(approved);
        }

        return (true, approved, approved
            ? $"Đã phê duyệt tool `{pending.ToolName}`."
            : $"Đã từ chối tool `{pending.ToolName}`.");
    }

    private static string[] ResolveActiveTools(WorkspaceAgentIntent intent, bool isLocal)
    {
        if (!isLocal)
        {
            return new[]
            {
                "fs_list", "fs_glob", "fs_read", "fs_grep",
                "fs_write", "fs_edit", "fs_rm",
                "bash_run", "todo_read", "todo_write",
                "rag_search", "find_duplicates", "safe_delete_to_trash", "restore_from_trash"
            };
        }

        // Với mô hình local 7B (Ollama), thu gọn danh sách tools xuống 3-5 tools trọng tâm
        // để tránh tràn ngữ cảnh và chống ảo giác sinh text thay vì gọi tool
        return intent switch
        {
            WorkspaceAgentIntent.DuplicateAuditCleanup => new[] { "find_duplicates", "safe_delete_to_trash", "restore_from_trash", "fs_list", "fs_read" },
            WorkspaceAgentIntent.WriteCreate => new[] { "fs_write", "safe_delete_to_trash", "fs_read", "fs_list" },
            WorkspaceAgentIntent.EditRefactor => new[] { "safe_delete_to_trash", "fs_write", "fs_edit", "fs_read", "fs_list" },
            WorkspaceAgentIntent.BuildVerify => new[] { "bash_run", "fs_read", "fs_list" },
            WorkspaceAgentIntent.SearchDebug => new[] { "rag_search", "fs_grep", "fs_read", "fs_list" },
            _ => new[] { "fs_list", "fs_glob", "rag_search", "fs_read" }
        };
    }

    public async IAsyncEnumerable<string> StreamWorkspaceAgentSseAsync(
        WorkspaceSession workspace,
        string userPrompt,
        string? uiModel,
        float? temperature,
        int? maxTokens,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var intent = _intentClassifier.Analyze(userPrompt);
        var (provider, resolvedModel, providerLabel, isLocal) = ResolveProviderAndModel(uiModel, intent.Intent);

        // Đảm bảo chỉ mục RAG sẵn sàng trước khi Agent chạy
        if (!workspace.LastIndexedAt.HasValue || (DateTime.UtcNow - workspace.LastIndexedAt.Value) > TimeSpan.FromMinutes(10))
        {
            yield return FormatSseAgentStep("info", "rag_index", "", $"Đang lập chỉ mục RAG cho `{workspace.FolderName}`...", null);
            try
            {
                var idxJson = await _ragService.IndexWorkspaceAsync(workspace.RootPath, cancellationToken);
                using var doc = JsonDocument.Parse(idxJson);
                if (doc.RootElement.TryGetProperty("fileCount", out var fc))
                {
                    await UpdateWorkspaceIndexStatsAsync(workspace.SessionId, fc.GetInt32(), cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pre-index RAG thất bại cho {Root}", workspace.RootPath);
            }
        }

        yield return FormatSseAgentStep("info", "agent_init", "", $"Khởi động Agent `{providerLabel}` ({resolvedModel}) trong `{workspace.FolderName}`...", null);

        var kodeStoreRoot = Path.Combine(_env.ContentRootPath, "App_Data", "KodeAgents");
        Directory.CreateDirectory(kodeStoreRoot);

        var agentId = string.IsNullOrWhiteSpace(workspace.AgentId)
            ? $"ws_{WorkspaceRagService.SanitizeSegment(workspace.SessionId)}"
            : workspace.AgentId;
        _agentToSession[agentId] = workspace.SessionId;

        var toolRegistry = new ToolRegistry();
        toolRegistry.RegisterBuiltinTools();
        toolRegistry.Register(new RagSearchTool(_ragService));
        toolRegistry.Register(new FindDuplicatesTool(_ragService));
        toolRegistry.Register(new SafeDeleteToTrashTool(_ragService, ResolveSessionIdByAgent));
        toolRegistry.Register(new RestoreFromTrashTool(_ragService, ResolveSessionIdByAgent));

        var enabledTools = ResolveActiveTools(intent.Intent, isLocal);

        var requireApproval = workspace.AutoApprove
            ? Array.Empty<string>()
            : new[] { "fs_rm", "bash_run", "safe_delete_to_trash" };

        var permissions = new PermissionConfig
        {
            Mode = "auto",
            AllowTools = ["*"],
            RequireApprovalTools = requireApproval
        };

        var sandboxOptions = new SandboxOptions
        {
            WorkingDirectory = workspace.RootPath,
            EnforceBoundary = true,
            Timeout = TimeSpan.FromMinutes(2)
        };

        var systemPrompt = BuildWorkspaceSystemPrompt(workspace, intent);
        var deps = new AgentDependencies
        {
            Store = new JsonAgentStore(kodeStoreRoot),
            SandboxFactory = new LocalSandboxFactory(),
            ToolRegistry = toolRegistry,
            ModelProvider = provider,
            LoggerFactory = _loggerFactory
        };

        var config = new AgentConfig
        {
            Model = resolvedModel,
            SystemPrompt = systemPrompt,
            MaxIterations = 16,
            MaxTokens = maxTokens ?? 4096,
            Temperature = temperature ?? 0.2,
            Tools = enabledTools,
            Permissions = permissions,
            SandboxOptions = sandboxOptions,
            ToolTimeout = TimeSpan.FromSeconds(60)
        };

        Agent agent;
        var agentMetaFile = Path.Combine(kodeStoreRoot, agentId, "meta.json");
        if (File.Exists(agentMetaFile))
        {
            try
            {
                agent = await Agent.ResumeFromStoreAsync(
                    agentId,
                    deps,
                    options: null,
                    overrides: new AgentConfigOverrides
                    {
                        Model = resolvedModel,
                        SystemPrompt = systemPrompt,
                        MaxIterations = 16,
                        MaxTokens = maxTokens ?? 4096,
                        Temperature = temperature ?? 0.2,
                        Tools = enabledTools,
                        Permissions = permissions,
                        SandboxOptions = sandboxOptions,
                        ToolTimeout = TimeSpan.FromSeconds(60)
                    },
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không resume được agent {AgentId}, tạo mới.", agentId);
                agent = await Agent.CreateAsync(agentId, config, deps, cancellationToken);
            }
        }
        else
        {
            agent = await Agent.CreateAsync(agentId, config, deps, cancellationToken);
        }

        var sseChannel = Channel.CreateUnbounded<string>();
        var pendingForRun = new ConcurrentBag<TaskCompletionSource<bool>>();

        using var controlSub = agent.EventBus.OnControl<PermissionRequiredEvent>(evt =>
        {
            if (workspace.AutoApprove)
            {
                _ = evt.Respond?.Invoke("allow", new PermissionRespondOptions { Note = "AutoApprove bật" });
                sseChannel.Writer.TryWrite(FormatSseAgentStep("info", evt.Call.Name, evt.Call.Id, $"Tự động cho phép `{evt.Call.Name}`", null));
                return;
            }

            var approvalId = Guid.NewGuid().ToString("N")[..12];
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entry = new PendingToolApproval(approvalId, workspace.SessionId, evt.Call.Id, evt.Call.Name, agent, evt.Respond, tcs);
            _pendingApprovals[approvalId] = entry;
            pendingForRun.Add(tcs);

            var preview = ExtractSnapshotJson(evt.Call);
            sseChannel.Writer.TryWrite(FormatSsePermissionRequired(approvalId, evt.Call.Id, evt.Call.Name, preview));
        });

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runCts.CancelAfter(TimeSpan.FromMinutes(3));

        using var errorSub = agent.EventBus.OnMonitor<ErrorEvent>(evt =>
        {
            sseChannel.Writer.TryWrite(FormatSseAgentStep("error", evt.Phase, "", evt.Message, null));
            if (string.Equals(evt.Severity, "error", StringComparison.OrdinalIgnoreCase))
            {
                sseChannel.Writer.TryWrite(FormatSseContentDelta($"\n\n> ⚠️ **Lỗi kết nối mô hình/công cụ ({evt.Phase}):** {evt.Message}"));
                sseChannel.Writer.TryComplete();
                try { runCts.Cancel(); } catch { }
            }
        });

        var agentInputPrompt = userPrompt;
        if (intent.IsMutating)
        {
            var currentFilesSnapshot = BuildQuickFileTreeSummary(workspace.RootPath);
            agentInputPrompt = $@"{userPrompt}

[TRẠNG THÁI TỆP THỰC TẾ TRÊN ĐĨA HIỆN TẠI (TRƯỚC LƯỢT NÀY)]:
{currentFilesSnapshot}
BẮT BUỘC gọi tool trực tiếp (`safe_delete_to_trash`, `fs_write`, `fs_edit`, ...) tác động thật lên các tệp ở trên — không trả lời dựa theo lịch sử cũ và không tự bịa kết quả.";
        }

        string? preScanDupJson = null;
        if (intent.Intent == WorkspaceAgentIntent.DuplicateAuditCleanup)
        {
            try
            {
                preScanDupJson = await _ragService.FindDuplicatesAsync(workspace.RootPath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pre-scan find_duplicates thất bại.");
            }
        }

        if (!string.IsNullOrWhiteSpace(preScanDupJson))
        {
            yield return FormatSseAgentStep("end", "find_duplicates", "pre_scan", preScanDupJson, "Completed");
            agentInputPrompt = $@"{userPrompt}

[DỮ LIỆU QUÉT TRÙNG LẶP THỰC TẾ TỪ `find_duplicates`]:
```json
{preScanDupJson}
```
HÃY HÀNH ĐỘNG NGAY: Dựa vào `exactDuplicateGroups` / `codeClones` ở trên, nếu người dùng yêu cầu xóa/dọn bản thừa và có `duplicatePaths`, hãy gọi NGAY tool `safe_delete_to_trash(paths: [...], reason: ""..."")` ngay bây giờ (giữ lại `keepPath`).";
        }

        var producerTask = Task.Run(async () =>
        {
            var completedMutatingTools = 0;

            async Task<string> ConsumeAgentTurnAsync(string promptToSend, bool streamTextToUi = true)
            {
                var since = agent.EventBus.GetLastBookmark();
                var anyTextStreamed = false;
                var turnText = new StringBuilder();

                await foreach (var envelope in agent.ChatStreamAsync(
                    promptToSend,
                    new Agent.StreamOptions { Since = since },
                    runCts.Token))
                {
                    switch (envelope.Event)
                    {
                        case TextChunkEvent textEvt:
                            if (!string.IsNullOrEmpty(textEvt.Delta))
                            {
                                anyTextStreamed = true;
                                turnText.Append(textEvt.Delta);
                                if (streamTextToUi)
                                {
                                    sseChannel.Writer.TryWrite(FormatSseContentDelta(textEvt.Delta));
                                }
                            }
                            break;

                        case TextChunkEndEvent textEnd:
                            if (!anyTextStreamed && !string.IsNullOrEmpty(textEnd.Text))
                            {
                                anyTextStreamed = true;
                                turnText.Append(textEnd.Text);
                                if (streamTextToUi)
                                {
                                    sseChannel.Writer.TryWrite(FormatSseContentDelta(textEnd.Text));
                                }
                            }
                            break;

                        case ToolStartEvent startEvt:
                            sseChannel.Writer.TryWrite(FormatSseAgentStep(
                                "start",
                                startEvt.Call.Name,
                                startEvt.Call.Id,
                                ExtractSnapshotJson(startEvt.Call),
                                null));
                            break;

                        case ToolEndEvent endEvt:
                            var summaryJson = ExtractSnapshotJson(endEvt.Call);
                            sseChannel.Writer.TryWrite(FormatSseAgentStep(
                                "end",
                                endEvt.Call.Name,
                                endEvt.Call.Id,
                                summaryJson,
                                endEvt.Call.State.ToString()));

                            if (endEvt.Call.State == ToolCallState.Completed)
                            {
                                if (IsMutatingToolName(endEvt.Call.Name))
                                {
                                    completedMutatingTools++;
                                }
                                if (string.Equals(endEvt.Call.Name, "safe_delete_to_trash", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(endEvt.Call.Name, "restore_from_trash", StringComparison.OrdinalIgnoreCase))
                                {
                                    sseChannel.Writer.TryWrite(FormatSseTrashUpdated(workspace.SessionId));
                                }
                            }
                            break;

                        case ToolErrorEvent errEvt:
                            sseChannel.Writer.TryWrite(FormatSseAgentStep(
                                "error",
                                errEvt.Call.Name,
                                errEvt.Call.Id,
                                errEvt.Error,
                                null));
                            break;

                        case DoneEvent doneEvt:
                            if (doneEvt.Reason == "interrupted" && !pendingForRun.IsEmpty)
                            {
                                await Task.WhenAll(pendingForRun.Select(t => t.Task)).WaitAsync(runCts.Token);
                                while (!pendingForRun.IsEmpty) pendingForRun.TryTake(out _);
                                if (agent.RuntimeState == AgentRuntimeState.Paused)
                                {
                                    await agent.ResumeAsync(runCts.Token);
                                    continue;
                                }
                            }
                            break;
                    }
                }

                return turnText.ToString();
            }

            try
            {
                var firstTurnText = await ConsumeAgentTurnAsync(agentInputPrompt);
                if (intent.IsMutating && completedMutatingTools == 0)
                {
                    var remainingFiles = ListWorkspaceRelativeFiles(workspace.RootPath);
                    bool shouldRetry = !intent.IsDestructive || remainingFiles.Count > 0 || IsConfirmationStall(firstTurnText);

                    if (shouldRetry)
                    {
                        var liveSummary = remainingFiles.Count == 0
                            ? "(Thư mục hiện đang trống)"
                            : string.Join("\n", remainingFiles.Select(f => "- " + f));

                        sseChannel.Writer.TryWrite(FormatSseAgentStep(
                            "info",
                            "verify_retry",
                            "",
                            "Chưa có tool ghi/xóa nào được thực thi ở lượt đầu — đang yêu cầu Agent gọi tool trực tiếp trên đĩa...",
                            null));

                        var nudgePrompt = workspace.AutoApprove
                            ? $@"[SYSTEM VERIFICATION ERROR] Lượt vừa rồi KHÔNG có tool ghi/xóa nào thực thi (`completedMutatingTools = 0`). Không được trả lời suông như đã làm xong.
CÁC TỆP THỰC TẾ VẪN CÒN TRÊN ĐĨA:
{liveSummary}
Hãy GỌI NGAY `safe_delete_to_trash` / `fs_write` / `fs_edit` qua `tool_calls` ngay bây giờ (Auto-Approve đã bật, KHÔNG hỏi lại)."
                            : $@"[SYSTEM VERIFICATION ERROR] Lượt vừa rồi KHÔNG có tool ghi/xóa nào thực thi.
CÁC TỆP THỰC TẾ VẪN CÒN TRÊN ĐĨA:
{liveSummary}
Hãy GỌI NGAY `tool_calls` trực tiếp (UI sandbox sẽ tự hiển thị nút phê duyệt nếu cần).";

                        await ConsumeAgentTurnAsync(nudgePrompt, streamTextToUi: false);
                    }

                    if (completedMutatingTools == 0)
                    {
                        var afterCheck = ListWorkspaceRelativeFiles(workspace.RootPath);
                        if (!intent.IsDestructive || afterCheck.Count > 0)
                        {
                            var lowerPrompt = (userPrompt ?? string.Empty).ToLowerInvariant();
                            bool explicitDeleteAll = lowerPrompt.Contains("xóa tất cả") || lowerPrompt.Contains("xoá tất cả") ||
                                                     lowerPrompt.Contains("xóa hết") || lowerPrompt.Contains("xoá hết") ||
                                                     lowerPrompt.Contains("xóa các file") || lowerPrompt.Contains("xoá các file") ||
                                                     lowerPrompt.Contains("xóa file") || lowerPrompt.Contains("xoá file") ||
                                                     lowerPrompt.Contains("delete all") || lowerPrompt.Contains("clean all");

                            bool modelClaimedDeleted = firstTurnText.Contains("đã xóa", StringComparison.OrdinalIgnoreCase) ||
                                                       firstTurnText.Contains("đã xoá", StringComparison.OrdinalIgnoreCase) ||
                                                       firstTurnText.Contains("deleted", StringComparison.OrdinalIgnoreCase);

                            // Nếu người dùng có ý định xóa rõ ràng và AI khẳng định đã xóa nhưng quên phát tool,
                            // COW.AI tự động thực thi dọn đĩa an toàn bằng cách di chuyển tệp vào .cowai_trash (có thể hoàn tác).
                            if (intent.IsDestructive && (explicitDeleteAll || modelClaimedDeleted) && afterCheck.Count > 0)
                            {
                                var movedList = new List<string>();
                                foreach (var rel in afterCheck)
                                {
                                    try
                                    {
                                        var (entry, _) = await _ragService.SoftDeleteToTrashAsync(
                                            workspace.RootPath,
                                            workspace.SessionId,
                                            rel,
                                            "Tự động chuyển vào thùng rác an toàn (Mô hình 7B quên phát Tool Call)",
                                            CancellationToken.None);
                                        movedList.Add(rel);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning(ex, "Lỗi auto fallback soft delete cho {Rel}", rel);
                                    }
                                }

                                if (movedList.Count > 0)
                                {
                                    completedMutatingTools += movedList.Count;
                                    sseChannel.Writer.TryWrite(FormatSseTrashUpdated(workspace.SessionId));
                                    sseChannel.Writer.TryWrite(FormatSseAgentStep(
                                        "end",
                                        "safe_delete_to_trash",
                                        "auto_fallback",
                                        $"Tự động chuyển {movedList.Count} tệp vào .cowai_trash",
                                        "Completed"));

                                    var filesInline = string.Join(", ", movedList.Take(10).Select(f => $"`{f}`"));
                                    sseChannel.Writer.TryWrite(FormatSseContentDelta(
                                        $"\n\n> 🛡️ **Tự động bảo đảm đĩa:** Mô hình AI cục bộ (7B) trả lời văn bản nhưng chưa phát lệnh Tool Call. Hệ thống COW.AI đã **tự động chuyển an toàn {movedList.Count} tệp** ({filesInline}) vào `.cowai_trash` để hoàn tất chính xác trên đĩa thật. Bạn có thể khôi phục bất cứ lúc nào qua nút Hoàn tác."));
                                }
                            }

                            if (completedMutatingTools == 0)
                            {
                                var fileListInline = afterCheck.Count > 0
                                    ? string.Join(", ", afterCheck.Take(10).Select(f => $"`{f}`"))
                                    : "(chưa có tệp mới)";
                                sseChannel.Writer.TryWrite(FormatSseAgentStep(
                                    "error",
                                    "disk_verify",
                                    "",
                                    $"Chưa có thao tác ghi/xóa thực tế nào chạy trên đĩa (còn {afterCheck.Count} tệp).",
                                    null));
                                sseChannel.Writer.TryWrite(FormatSseContentDelta(
                                    $"\n\n> ⚠️ **Xác minh thực tế trên đĩa:** Mô hình AI chưa phát lệnh tool hợp lệ nên **chưa có tệp nào bị xóa/ghi trên ổ đĩa**. Hiện vẫn còn **{afterCheck.Count}** tệp: {fileListInline}. Hãy thử gửi lại yêu cầu cụ thể hoặc chuyển sang model khác."));
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled cleanly
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi thực thi KodeWorkspaceAgentService cho session {SessionId}", workspace.SessionId);
                sseChannel.Writer.TryWrite(FormatSseContentDelta($"\n\n> ⚠️ **Lỗi AI Agent:** {ex.Message}"));
            }
            finally
            {
                foreach (var p in pendingForRun) p.TrySetResult(false);
                sseChannel.Writer.TryComplete();
            }
        }, CancellationToken.None);

        await using (agent)
        {
            await foreach (var line in sseChannel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return line;
            }
            await producerTask;
        }

        // Cập nhật lại chỉ mục RAG nếu Agent vừa sửa/xóa/tạo file
        if (intent.IsMutating)
        {
            try
            {
                var idxJson = await _ragService.IndexWorkspaceAsync(workspace.RootPath, CancellationToken.None);
                using var doc = JsonDocument.Parse(idxJson);
                if (doc.RootElement.TryGetProperty("fileCount", out var fc))
                {
                    await UpdateWorkspaceIndexStatsAsync(workspace.SessionId, fc.GetInt32(), CancellationToken.None);
                }
            }
            catch { /* non-fatal */ }
        }

        yield return "data: [DONE]\n\n";
    }

    private (IModelProvider Provider, string ResolvedModel, string Label, bool IsLocal) ResolveProviderAndModel(
        string? uiModel,
        WorkspaceAgentIntent intent)
    {
        var openRouterKey = _config["OpenRouter:ApiKey"]
            ?? _config["OPENROUTER_API_KEY"]
            ?? Environment.GetEnvironmentVariable("OpenRouter__ApiKey")
            ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");

        bool hasValidOpenRouter = !string.IsNullOrWhiteSpace(openRouterKey)
            && !openRouterKey.Contains("xxxxxxxx", StringComparison.OrdinalIgnoreCase)
            && !openRouterKey.Contains("your_", StringComparison.OrdinalIgnoreCase)
            && !openRouterKey.Contains("placeholder", StringComparison.OrdinalIgnoreCase);

        var keyLower = (uiModel ?? "auto").Trim().ToLowerInvariant();
        bool wantsLocal = keyLower.Contains("local") || keyLower.Contains("ollama") || keyLower.Contains("cow-ai") || keyLower == "auto";

        if (!wantsLocal && hasValidOpenRouter)
        {
            var resolvedCloud = keyLower switch
            {
                "gemini" => _config["OpenRouter:DefaultModel"] ?? "google/gemini-2.0-flash-exp:free",
                "deepseek" => _config["OpenRouter:CodeGenModel"] ?? "deepseek/deepseek-r1:free",
                "llama3" or "llama" => _config["OpenRouter:SummaryModel"] ?? "meta-llama/llama-3-8b-instruct:free",
                "omniroute" => _config["OpenRouter:OmniRouteModel"] ?? "openrouter/auto",
                _ => uiModel!.Trim()
            };

            var cloudBase = (_config["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1").TrimEnd('/');
            var provider = new ToolCallRecoveryModelProvider(
                new OpenAIProvider(
                    new OpenAIOptions { ApiKey = openRouterKey!, BaseUrl = cloudBase, DefaultModel = resolvedCloud },
                    _loggerFactory.CreateLogger<OpenAIProvider>()));
            return (provider, resolvedCloud, "OpenRouter Agent", false);
        }

        // Dùng Local Ollama mặc định tại FallbackLocalUrl (http://localhost:11434/v1)
        // Tránh gọi nhầm https://ollama.com/v1 khi không có API key cloud
        var configuredOllamaUrl = _config["Ollama:FallbackLocalUrl"];
        if (string.IsNullOrWhiteSpace(configuredOllamaUrl))
        {
            var rawBase = _config["Ollama:BaseUrl"] ?? "http://localhost:11434/v1";
            configuredOllamaUrl = rawBase.Contains("localhost", StringComparison.OrdinalIgnoreCase) || rawBase.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                ? rawBase
                : "http://localhost:11434/v1";
        }

        var ollamaBase = configuredOllamaUrl.TrimEnd('/');
        var ollamaV1 = ollamaBase.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? ollamaBase : $"{ollamaBase}/v1";
        var localModel = intent is WorkspaceAgentIntent.ExploreRead or WorkspaceAgentIntent.DuplicateAuditCleanup
            ? "qwen2.5:7b"
            : "qwen2.5-coder:7b";

        var localProvider = new ToolCallRecoveryModelProvider(
            new OpenAIProvider(
                new OpenAIOptions { ApiKey = "ollama", BaseUrl = ollamaV1, DefaultModel = localModel },
                _loggerFactory.CreateLogger<OpenAIProvider>()));
        return (localProvider, localModel, "COW.AI Local Agent", true);
    }

    private static bool IsMutatingToolName(string? name) =>
        name is not null &&
        (string.Equals(name, "safe_delete_to_trash", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(name, "fs_write", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(name, "fs_edit", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(name, "restore_from_trash", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(name, "bash_run", StringComparison.OrdinalIgnoreCase));

    private static bool IsConfirmationStall(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 600) return false;
        var lower = text.ToLowerInvariant();
        return lower.Contains('?') &&
               (lower.Contains("tiếp tục") ||
                lower.Contains("xác nhận") ||
                lower.Contains("chắc chắn") ||
                lower.Contains("đồng ý") ||
                lower.Contains("confirm") ||
                lower.Contains("proceed") ||
                lower.Contains("continue"));
    }

    private static string BuildWorkspaceSystemPrompt(WorkspaceSession workspace, AgentIntentAnalysis intent)
    {
        var fileSummary = BuildQuickFileTreeSummary(workspace.RootPath);
        var autoApproveLine = workspace.AutoApprove
            ? "ENABLED (Auto-Approve = true) — user permission already granted; execute all delete/write tools immediately without asking."
            : "MANUAL (UI approval modal handles confirmation automatically; still invoke the tool call directly right now without asking in text).";

        return $@"You are **COW.AI Autonomous Workspace Agent** (`Kode.Agent.Sdk`), scoped strictly to ONE project folder and isolated from all normal chat sessions:
- **Project:** {workspace.FolderName}
- **Sandbox Root:** {workspace.RootPath}
- **Permission:** {autoApproveLine}

CURRENT WORKSPACE FILES:
{fileSummary}

EXECUTION RULES:
1. Work strictly inside `{workspace.RootPath}` using relative paths matching `CURRENT WORKSPACE FILES`.
2. ALWAYS invoke real `tool_calls` (`safe_delete_to_trash`, `fs_write`, `fs_edit`, `fs_read`, `fs_list`, `rag_search`, `find_duplicates`, `restore_from_trash`, `bash_run`). NEVER print raw JSON tool calls as plain text or fabricate unexecuted outputs.
3. Deleting files is 100% safe and reversible via `.cowai_trash` (`safe_delete_to_trash`). Execute `safe_delete_to_trash(paths: [...], reason: ""..."")` directly on existing files (never `.cowai_trash`) without asking text questions first.
4. Writing/updating files: execute `fs_write` / `fs_edit` with complete real content.
5. Multi-step requests (e.g. delete files then create `index.html`): execute ALL required tool calls to completion first, then give a concise Vietnamese summary at the end.

TASK INTENT ({intent.Intent}):
{intent.GuidanceNote}";
    }

    private static List<string> ListWorkspaceRelativeFiles(string rootPath)
    {
        if (!Directory.Exists(rootPath)) return [];
        string[] ignored = ["node_modules", ".git", "bin", "obj", ".vs", ".cowai_trash", ".kode"];
        return Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(rootPath, f).Replace('\\', '/'))
            .Where(rel => !rel.Split('/').Any(seg => ignored.Contains(seg, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(rel => rel, StringComparer.OrdinalIgnoreCase)
            .Take(60)
            .ToList();
    }

    private static string BuildQuickFileTreeSummary(string rootPath)
    {
        try
        {
            if (!Directory.Exists(rootPath)) return "(Thư mục không tồn tại)";
            var files = ListWorkspaceRelativeFiles(rootPath);
            return files.Count == 0 ? "(Thư mục đang trống)" : string.Join("\n", files.Select(f => "- " + f));
        }
        catch
        {
            return "(Không đọc nhanh được danh sách tệp)";
        }
    }

    private async Task UpdateWorkspaceIndexStatsAsync(string sessionId, int fileCount, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ws = await db.WorkspaceSessions.FirstOrDefaultAsync(x => x.SessionId == sessionId, ct);
            if (ws != null)
            {
                ws.FileCount = fileCount;
                ws.LastIndexedAt = DateTime.UtcNow;
                ws.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }
        catch { /* ignore */ }
    }

    private static string ExtractSnapshotJson(ToolCallSnapshot snap)
    {
        try
        {
            var json = JsonSerializer.Serialize(snap);
            return json.Length > 800 ? json[..800] + "..." : json;
        }
        catch
        {
            return snap.Name;
        }
    }

    private static string FormatSseContentDelta(string text)
    {
        var payload = new { choices = new[] { new { delta = new { content = text } } } };
        return $"data: {JsonSerializer.Serialize(payload)}\n\n";
    }

    private static string FormatSseAgentStep(string phase, string tool, string callId, string preview, string? state)
    {
        var payload = new
        {
            agent_step = new
            {
                phase,
                tool,
                callId,
                preview,
                state
            }
        };
        return $"data: {JsonSerializer.Serialize(payload)}\n\n";
    }

    private static string FormatSsePermissionRequired(string approvalId, string callId, string tool, string preview)
    {
        var payload = new
        {
            permission_required = new
            {
                approvalId,
                callId,
                tool,
                preview
            }
        };
        return $"data: {JsonSerializer.Serialize(payload)}\n\n";
    }

    private static string FormatSseTrashUpdated(string sessionId)
    {
        var payload = new { trash_updated = new { sessionId, hasTrash = true } };
        return $"data: {JsonSerializer.Serialize(payload)}\n\n";
    }

    /// <summary>
    /// Decorator cho IModelProvider giúp tự động khôi phục các lời gọi tool bị model local (Ollama 7B)
    /// xuất nhầm dưới dạng chuỗi JSON hoặc thẻ &lt;tool_call&gt; thành ToolUseChunk chuẩn của Kode.Agent.Sdk.
    /// </summary>
    private sealed class ToolCallRecoveryModelProvider : IModelProvider
    {
        private readonly IModelProvider _inner;

        public ToolCallRecoveryModelProvider(IModelProvider inner) => _inner = inner;
        public string ProviderName => _inner.ProviderName;
        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default) =>
            _inner.CompleteAsync(request, cancellationToken);
        public Task<bool> ValidateAsync(CancellationToken cancellationToken = default) =>
            _inner.ValidateAsync(cancellationToken);

        public async IAsyncEnumerable<StreamChunk> StreamAsync(
            ModelRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var validTools = request.Tools?.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
                             ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var textBuffer = new StringBuilder();
            var fullStreamedText = new StringBuilder();
            bool? bufferingForToolJson = null;
            bool sawNativeToolUse = false;

            await foreach (var chunk in _inner.StreamAsync(request, cancellationToken))
            {
                if (chunk.Type is StreamChunkType.ToolUseStart or StreamChunkType.ToolUseInputDelta or StreamChunkType.ToolUseComplete)
                {
                    sawNativeToolUse = true;
                    if (textBuffer.Length > 0)
                    {
                        yield return new StreamChunk { Type = StreamChunkType.TextDelta, TextDelta = textBuffer.ToString() };
                        textBuffer.Clear();
                    }
                    yield return chunk;
                    continue;
                }

                if (chunk.Type == StreamChunkType.TextDelta && !string.IsNullOrEmpty(chunk.TextDelta))
                {
                    fullStreamedText.Append(chunk.TextDelta);

                    if (bufferingForToolJson == null)
                    {
                        var trimmedStart = chunk.TextDelta.TrimStart();
                        if (string.IsNullOrEmpty(trimmedStart))
                        {
                            textBuffer.Append(chunk.TextDelta);
                            continue;
                        }
                        bufferingForToolJson = validTools.Count > 0 ||
                                               trimmedStart.StartsWith("{") ||
                                               trimmedStart.StartsWith("```") ||
                                               trimmedStart.StartsWith("<tool_call>", StringComparison.OrdinalIgnoreCase);
                    }

                    if (bufferingForToolJson == true)
                    {
                        textBuffer.Append(chunk.TextDelta);
                        continue;
                    }

                    if (textBuffer.Length > 0)
                    {
                        yield return new StreamChunk { Type = StreamChunkType.TextDelta, TextDelta = textBuffer.ToString() };
                        textBuffer.Clear();
                    }
                    yield return chunk;
                    continue;
                }

                if (chunk.Type == StreamChunkType.MessageStop)
                {
                    var candidate = textBuffer.Length > 0 ? textBuffer.ToString() : fullStreamedText.ToString();
                    if (!sawNativeToolUse && !string.IsNullOrWhiteSpace(candidate) && validTools.Count > 0 &&
                        TryParseRecoveredToolCalls(candidate, validTools, out var recovered))
                    {
                        foreach (var (name, argsObj, argsJson) in recovered)
                        {
                            var callId = "call_" + Guid.NewGuid().ToString("N")[..12];
                            yield return new StreamChunk
                            {
                                Type = StreamChunkType.ToolUseStart,
                                ToolUse = new ToolUseChunk { Id = callId, Name = name }
                            };
                            yield return new StreamChunk
                            {
                                Type = StreamChunkType.ToolUseInputDelta,
                                ToolUse = new ToolUseChunk { Id = callId, InputDelta = argsJson }
                            };
                            yield return new StreamChunk
                            {
                                Type = StreamChunkType.ToolUseComplete,
                                ToolUse = new ToolUseChunk { Id = callId, Name = name, Input = argsObj }
                            };
                        }
                        textBuffer.Clear();
                        yield return new StreamChunk
                        {
                            Type = StreamChunkType.MessageStop,
                            StopReason = ModelStopReason.ToolUse,
                            Usage = chunk.Usage
                        };
                        yield break;
                    }

                    if (textBuffer.Length > 0)
                    {
                        yield return new StreamChunk { Type = StreamChunkType.TextDelta, TextDelta = textBuffer.ToString() };
                        textBuffer.Clear();
                    }

                    yield return chunk;
                    continue;
                }

                yield return chunk;
            }
        }

        private static bool TryParseRecoveredToolCalls(
            string raw,
            HashSet<string> validTools,
            out List<(string Name, object Args, string ArgsJson)> calls)
        {
            calls = [];
            var cleaned = raw.Trim();
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"^```(?:json)?\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s*```\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = cleaned.Replace("<tool_call>", "").Replace("</tool_call>", "").Trim();

            try
            {
                using var doc = JsonDocument.Parse(cleaned);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (TryExtractSingleCall(doc.RootElement, validTools, out var single)) calls.Add(single);
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        if (TryExtractSingleCall(el, validTools, out var single)) calls.Add(single);
                    }
                }
            }
            catch
            {
                // Fallback: trích xuất từng JSON object cân bằng ngoặc {} kể cả khi model viết thêm text phía sau
                int idx = 0;
                while (idx < cleaned.Length)
                {
                    int start = cleaned.IndexOf('{', idx);
                    if (start < 0) break;
                    int depth = 0;
                    bool inString = false;
                    bool escaped = false;
                    int end = -1;
                    for (int i = start; i < cleaned.Length; i++)
                    {
                        char c = cleaned[i];
                        if (inString)
                        {
                            if (escaped) escaped = false;
                            else if (c == '\\') escaped = true;
                            else if (c == '"') inString = false;
                        }
                        else
                        {
                            if (c == '"') inString = true;
                            else if (c == '{') depth++;
                            else if (c == '}' && --depth == 0) { end = i; break; }
                        }
                    }
                    if (end < 0) break;
                    var slice = cleaned.Substring(start, end - start + 1);
                    try
                    {
                        using var subDoc = JsonDocument.Parse(slice);
                        if (TryExtractSingleCall(subDoc.RootElement, validTools, out var single)) calls.Add(single);
                    }
                    catch { }
                    idx = end + 1;
                }
            }

            return calls.Count > 0;
        }

        private static bool TryExtractSingleCall(JsonElement el, HashSet<string> validTools, out (string Name, object Args, string ArgsJson) call)
        {
            call = default;
            if (el.ValueKind != JsonValueKind.Object) return false;
            if (!el.TryGetProperty("name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String) return false;
            var name = nameProp.GetString();
            if (string.IsNullOrWhiteSpace(name) || !validTools.Contains(name)) return false;

            object argsObj = new { };
            string argsJson = "{}";
            if (el.TryGetProperty("arguments", out var argsProp) || el.TryGetProperty("input", out argsProp))
            {
                if (argsProp.ValueKind == JsonValueKind.String)
                {
                    var innerJson = argsProp.GetString() ?? "{}";
                    try
                    {
                        argsObj = JsonSerializer.Deserialize<object>(innerJson) ?? new { };
                        argsJson = innerJson;
                    }
                    catch
                    {
                        argsObj = new { };
                        argsJson = "{}";
                    }
                }
                else
                {
                    var rawText = argsProp.GetRawText();
                    argsObj = JsonSerializer.Deserialize<object>(rawText) ?? new { };
                    argsJson = string.IsNullOrWhiteSpace(rawText) ? "{}" : rawText;
                }
            }
            call = (name, argsObj, argsJson);
            return true;
        }
    }
}
