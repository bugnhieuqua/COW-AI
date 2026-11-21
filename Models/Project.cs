using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COW_AI.Models;

public class Project
{
    [Key]
    public int Id { get; set; }

    public int? UserId { get; set; }

    [ForeignKey("UserId")]
    [System.Text.Json.Serialization.JsonIgnore]
    public User? User { get; set; }

    [Required]
    [MaxLength(150)]
    public string Title { get; set; } = "Dự án Studio mới";

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(30)]
    public string Status { get; set; } = "Empty"; // Empty, Reviewing, Processing, Completed

    public string? ScriptJson { get; set; }

    [MaxLength(50)]
    public string Voice { get; set; } = "Hải Đăng";

    [MaxLength(50)]
    public string VideoType { get; set; } = "comparison"; // comparison, sequential

    [MaxLength(16)]
    public string AspectRatio { get; set; } = "16:9"; // 16:9, 9:16, 1:1

    public bool IsAspectLocked { get; set; } = false; // true khi chọn 16:9 / 9:16 / 1:1 cố định; false khi chọn Bảng vẽ trống

    public string? AvatarsJson { get; set; } // JSON array of up to 3 avatar image paths

    public string? PlanMarkdown { get; set; } // Generated Markdown plan (.md)

    [MaxLength(255)]
    public string? SourceFileName { get; set; }

    public string? SourceContent { get; set; }

    public string? SourceMarkdown { get; set; }

    [MaxLength(30)]
    public string SequentialSubMode { get; set; } = "auto"; // auto, frame, kinetic

    [MaxLength(30)]
    public string DurationPreset { get; set; } = "auto"; // auto, short_35s, medium_2_3m, deep_4_6m

    public double EstimatedDurationSeconds { get; set; } = 36.0;

    [MaxLength(60)]
    public string SkillKey { get; set; } = "compare-3zone";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<Scene> Scenes { get; set; } = new();
}
