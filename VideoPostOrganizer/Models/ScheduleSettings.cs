namespace VideoPostOrganizer.Models;

public class ScheduleSettings
{
    public string DefaultPostTime { get; set; } = "09:00";
    public string FirstPostSubtype { get; set; } = "post";
    public string RepeatPostSubtype { get; set; } = "reel";
    public int RepeatEveryDays { get; set; } = 7;
    public int RepeatCount { get; set; } = 0;
}
