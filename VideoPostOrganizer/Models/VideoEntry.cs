using System.IO;
using System.Text.Json.Serialization;

namespace VideoPostOrganizer.Models;

public class VideoEntry
{
    public required string VideoName { get; set; }
    public required string VideoFileName { get; set; }
    public required string VideoPath { get; set; }
    public required string FolderPath { get; set; }
    public List<string> DescriptionFiles { get; set; } = new();
    public string PerformanceLevel { get; set; } = "Normal";
    public List<string> Tags { get; set; } = new();
    public DateTime? LastPostDate { get; set; }
    public bool ReadyForUse { get; set; }

    [JsonIgnore]
    public DateTime? FileCreatedOn => File.Exists(VideoPath) ? File.GetCreationTime(VideoPath) : null;

    [JsonIgnore]
    public string FileCreatedDisplay => FileCreatedOn?.ToString("yyyy-MM-dd") ?? "-";

    [JsonIgnore]
    public DateTime? FileCreatedOn => File.Exists(VideoPath) ? File.GetCreationTime(VideoPath) : null;

    [JsonIgnore]
    public string FileCreatedDisplay => FileCreatedOn?.ToString("yyyy-MM-dd") ?? "-";

    [JsonIgnore]
    public string DisplayName => LastPostDate is null
        ? VideoName
        : $"{VideoName} (last post: {LastPostDate:yyyy-MM-dd})";
}
