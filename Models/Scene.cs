using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COW_AI.Models;

public class Scene
{
    [Key]
    public int Id { get; set; }

    public int ProjectId { get; set; }

    [ForeignKey("ProjectId")]
    [System.Text.Json.Serialization.JsonIgnore]
    public Project? Project { get; set; }

    public int OrderIndex { get; set; } = 1;

    [Required]
    [MaxLength(100)]
    public string Title { get; set; } = "Cảnh 1";

    [Required]
    public string Content { get; set; } = string.Empty;

    public string? ImagePrompt { get; set; }

    public string? ImageUrl { get; set; }

    public string? AudioUrl { get; set; }

    public double DurationSeconds { get; set; } = 4.0;

    [MaxLength(50)]
    public string AnimationEffect { get; set; } = "ken-burns";

    [MaxLength(50)]
    public string FilterPreset { get; set; } = "none";

    [MaxLength(50)]
    public string SubtitlePosition { get; set; } = "bottom";

    [MaxLength(50)]
    public string? SceneType { get; set; } = "standard";

    [MaxLength(50)]
    public string? AvatarPosition { get; set; } = "center";

    [MaxLength(50)]
    public string? Speaker { get; set; } = "Host";
}
