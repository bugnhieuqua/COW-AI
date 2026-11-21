using System.Text.RegularExpressions;

namespace COW_AI.Services;

public enum WorkspaceAgentIntent
{
    ExploreRead,
    SearchDebug,
    WriteCreate,
    EditRefactor,
    DuplicateAuditCleanup,
    BuildVerify
}

public sealed record AgentIntentAnalysis(
    WorkspaceAgentIntent Intent,
    bool IsMutating,
    bool IsDestructive,
    string PreferredOllamaModel,
    string GuidanceNote
);

/// <summary>
/// Phân tích ý định NLP (Tiếng Việt + Tiếng Anh) cho các tác vụ trên thư mục workspace.
/// </summary>
public sealed class AgentIntentClassifier
{
    private static readonly Regex DuplicateDeleteRegex = new(
        @"(trùng\s*lặp|trùng\s*tên|file\s*trùng|nhân\s*bản|duplicate|dedupe|clone|dọn\s*dẹp|xóa\s*bớt|xoá\s*bớt|remove\s*duplicate|clean\s*up|redundant)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GeneralDeleteRegex = new(
        @"\b(xóa|xoá|delete|remove|wipe|erase)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WriteCreateRegex = new(
        @"(tạo\s*(file|tệp|thư\s*mục|class|module|hàm|service|api|trang)|viết\s*(thêm|file|tệp|code|chương\s*trình|hàm|test|unit\s*test|readme|tài\s*liệu)|thêm\s*(file|tệp|tính\s*năng|chức\s*năng|endpoint|controller|model)|create\s*(file|folder|class|module|test)|write\s*(a\s*)?(new\s*)?(file|script|code|test|doc)|add\s*(new\s*)?(file|feature|endpoint)|generate\s*(file|code))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EditRefactorRegex = new(
        @"(sửa\s*(lỗi|file|code|bug|lại)|chỉnh\s*sửa|cập\s*nhật|nâng\s*cấp|tối\s*ưu|refactor|tách\s*file|đổi\s*tên|rename|fix\s*(bug|error|issue)|update\s*(file|code)|modify|patch|replace)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BuildVerifyRegex = new(
        @"(chạy\s*(test|build|lệnh|kiểm\s*thử|npm|dotnet|python)|kiểm\s*tra\s*lỗi\s*biên\s*dịch|dotnet\s+build|dotnet\s+test|npm\s+(test|run)|pytest|compile|verify)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SearchDebugRegex = new(
        @"(tìm\s*(kiếm|ở\s*đâu|chỗ\s*nào|hàm|class|file|lỗi)|tra\s*cứu|giải\s*thích\s*(luồng|hàm|logic)|nằm\s*ở\s*đâu|where\s*is|find\s+|search\s+|grep|locate|how\s+does)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public AgentIntentAnalysis Analyze(string prompt)
    {
        var text = (prompt ?? string.Empty).Trim();

        if (DuplicateDeleteRegex.IsMatch(text))
        {
            return new AgentIntentAnalysis(
                Intent: WorkspaceAgentIntent.DuplicateAuditCleanup,
                IsMutating: true,
                IsDestructive: text.Contains("xóa", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("xoá", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("delete", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("remove", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("dedupe", StringComparison.OrdinalIgnoreCase),
                PreferredOllamaModel: "qwen2.5:7b",
                GuidanceNote: "BẮT BUỘC thực thi trọn vẹn chuỗi hành động: (1) Gọi `find_duplicates` trước để tìm bản trùng SHA-256 và code clone jscpd; (2) Nếu có `duplicatePaths` và người dùng yêu cầu xóa/dọn, GỌI NGAY tool `safe_delete_to_trash` với danh sách `paths = duplicatePaths` ngay trong lượt chạy này (KHÔNG hỏi lại bằng chữ). Báo cáo chính xác `keepPath` đã giữ lại và danh sách tệp đã chuyển vào `.cowai_trash`.");
        }

        if (WriteCreateRegex.IsMatch(text))
        {
            var isDestructive = GeneralDeleteRegex.IsMatch(text);
            return new AgentIntentAnalysis(
                Intent: WorkspaceAgentIntent.WriteCreate,
                IsMutating: true,
                IsDestructive: isDestructive,
                PreferredOllamaModel: "qwen2.5-coder:7b",
                GuidanceNote: isDestructive
                    ? "Thực hiện ngay cả 2 bước bằng tool: (1) Gọi `safe_delete_to_trash` cho đúng các tệp thực tế cần xóa; (2) Gọi `fs_write` tạo/ghi nội dung tệp mới hoàn chỉnh rồi báo cáo ngắn gọn."
                    : "Người dùng yêu cầu tạo/viết mới tệp hoặc mã nguồn. Dùng `fs_write` tạo tệp thực tế vào đúng thư mục và xác nhận lại bằng `fs_read`.");
        }

        if (EditRefactorRegex.IsMatch(text) || GeneralDeleteRegex.IsMatch(text))
        {
            var isDestructive = GeneralDeleteRegex.IsMatch(text);
            return new AgentIntentAnalysis(
                Intent: WorkspaceAgentIntent.EditRefactor,
                IsMutating: true,
                IsDestructive: isDestructive,
                PreferredOllamaModel: "qwen2.5-coder:7b",
                GuidanceNote: isDestructive
                    ? "Người dùng yêu cầu xóa/chỉnh sửa tệp thực tế. Gọi trực tiếp `safe_delete_to_trash` (và `fs_edit`/`fs_write` nếu có), không hỏi lại bằng chữ."
                    : "Người dùng yêu cầu chỉnh sửa/refactor mã nguồn hiện có. Hãy tìm file bằng `rag_search`/`fs_grep`, đọc đầy đủ bằng `fs_read`, rồi sửa chính xác bằng `fs_edit` hoặc `fs_write`.");
        }

        if (BuildVerifyRegex.IsMatch(text))
        {
            return new AgentIntentAnalysis(
                Intent: WorkspaceAgentIntent.BuildVerify,
                IsMutating: false,
                IsDestructive: false,
                PreferredOllamaModel: "qwen2.5-coder:7b",
                GuidanceNote: "Người dùng yêu cầu build/test/kiểm tra thực thi. Sử dụng `bash_run` trong sandbox workspace hiện tại để chạy kiểm tra và tóm tắt kết quả chính xác.");
        }

        if (SearchDebugRegex.IsMatch(text))
        {
            return new AgentIntentAnalysis(
                Intent: WorkspaceAgentIntent.SearchDebug,
                IsMutating: false,
                IsDestructive: false,
                PreferredOllamaModel: "qwen2.5-coder:7b",
                GuidanceNote: "Sử dụng `rag_search` (Hybrid BM25 + ngữ nghĩa), `fs_grep` và `fs_read` để trích dẫn chính xác đường dẫn tệp, tên ký hiệu và số dòng.");
        }

        return new AgentIntentAnalysis(
            Intent: WorkspaceAgentIntent.ExploreRead,
            IsMutating: false,
            IsDestructive: false,
            PreferredOllamaModel: "qwen2.5:7b",
            GuidanceNote: "Khám phá tổng quan cấu trúc dự án bằng `fs_list`, `fs_glob`, `rag_search`, `fs_read` để trả lời dựa trên tệp thực tế trong thư mục.");
    }
}
