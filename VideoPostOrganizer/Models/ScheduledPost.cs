using System.Text.Json.Serialization;

namespace VideoPostOrganizer.Models;

public class ScheduledPost
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string VideoName { get; set; } = string.Empty;
    public string VideoFileName { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public DateTime ScheduledAt { get; set; }
    public string PostSubtype { get; set; } = "post";
    public int RepeatEveryDays { get; set; }

    [JsonIgnore]
    public string ScheduledAtDisplay => ScheduledAt.ToString("yyyy-MM-dd HH:mm");

    [JsonIgnore]
    public string RepeatEveryDaysDisplay => RepeatEveryDays > 0 ? $"q{RepeatEveryDays}d" : "-";
}
