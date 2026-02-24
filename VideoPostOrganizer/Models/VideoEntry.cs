using System.Text.Json.Serialization;

namespace VideoPostOrganizer.Models;

public class VideoEntry
{
    public required string VideoName { get; set; }
    public required string VideoFileName { get; set; }
    public required string VideoPath { get; set; }
    public required string FolderPath { get; set; }
    public List<string> DescriptionFiles { get; set; } = new();
    public string Category { get; set; } = string.Empty;
    public string PerformanceNotes { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public DateTime? LastPostDate { get; set; }

    [JsonIgnore]
    public string DisplayName => LastPostDate is null
        ? VideoName
        : $"{VideoName} (last post: {LastPostDate:yyyy-MM-dd})";
}
