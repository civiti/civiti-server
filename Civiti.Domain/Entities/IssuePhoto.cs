namespace Civiti.Domain.Entities;

public class IssuePhoto
{
    public Guid Id { get; set; }
    public Guid IssueId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string? Caption { get; set; }
    public string? Description { get; set; }
    public bool IsPrimary { get; set; } = false;

    /// <summary>
    /// Position within the issue's photo set, as the owner arranged it — 0 is the primary photo.
    /// <para>
    /// Order is data, so it is stored rather than inferred. Deriving it from <see cref="CreatedAt"/>
    /// and <see cref="Id"/> does not work: a photo set is written in one go, so every row shares a
    /// timestamp and the id tiebreak is a fresh random GUID. Photos would come back in an
    /// arbitrary sequence, and re-submitting an unchanged list would look like a change.
    /// </para>
    /// <para>
    /// Rows predating this column are all 0 and fall back to the previous ordering. A photo set is
    /// always replaced wholesale, so a single issue never mixes the two.
    /// </para>
    /// </summary>
    public int DisplayOrder { get; set; }
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
    Unspecified = 0,
    Low = 1,
    Medium = 2,
    High = 3
}