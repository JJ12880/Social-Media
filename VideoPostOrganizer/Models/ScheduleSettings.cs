namespace VideoPostOrganizer.Models;

public class ScheduleSettings
{
    public string FirstPostTime { get; set; } = "09:00";
    public string RepeatPostTime { get; set; } = "13:00";
    public string FirstPostSubtype { get; set; } = "post";
    public string RepeatPostSubtype { get; set; } = "reel";
    public int RepeatEveryDays { get; set; } = 7;
}
