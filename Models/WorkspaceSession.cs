using System.ComponentModel.DataAnnotations;

namespace COW_AI.Models;

/// <summary>
/// Liên kết 1-1 giữa một phiên Chat trên Trang chủ và một Thư mục Workspace cục bộ.
/// </summary>
public class WorkspaceSession
{
    public int Id { get; set; }

    /// <summary>
    /// ID tài khoản người dùng sở hữu dự án/workspace này (bắt buộc đăng nhập với mục Dự án).
    /// </summary>
    public int? UserId { get; set; }

    /// <summary>
    /// Mã định danh phiên chat từ client (CowAiHistoryStore session.id).
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Tên thư mục hiển thị (ví dụ: COW-AI hoặc MyProject).
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string FolderName { get; set; } = string.Empty;

    /// <summary>
    /// Đường dẫn tuyệt đối trên ổ đĩa đã được chuẩn hóa (Workspace Jail root).
    /// </summary>
    [Required]
    [MaxLength(1024)]
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// Nguồn thư mục: "server_path" hoặc "browser_upload".
    /// </summary>
    [MaxLength(32)]
    public string SourceType { get; set; } = "server_path";

    /// <summary>
    /// Số lượng tệp hợp lệ được lập chỉ mục trong thư mục.
    /// </summary>
    public int FileCount { get; set; }

    /// <summary>
    /// Mã Agent của Kode.Agent.Sdk gắn riêng với phiên này (để resume hội thoại & tool context).
    /// </summary>
    [MaxLength(160)]
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Cho phép Agent tự động chạy các lệnh xóa/bash mà không cần hỏi xác nhận từng lần.
    /// </summary>
    public bool AutoApprove { get; set; } = false;

    /// <summary>
    /// Thời điểm lập chỉ mục RAG gần nhất.
    /// </summary>
    public DateTime? LastIndexedAt { get; set; }

    /// <summary>
    /// Danh sách JSON các tệp đã bị chuyển vào thùng rác an toàn (.cowai_trash) để hoàn tác.
    /// </summary>
    public string TrashManifestJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
