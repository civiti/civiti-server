namespace Civica.Api.Models.Domain;

public class IssuePhoto
{
    public Guid Id { get; set; }
    public Guid IssueId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string? Caption { get; set; }
    public string? Description { get; set; }
    public bool IsPrimary { get; set; } = false;
    public PhotoQuality Quality { get; set; } = PhotoQuality.Medium;
    public int? FileSize { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Format { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Issue Issue { get; set; } = null!;
}

public enum PhotoQuality
{
    Low,
    Medium,
    High
}