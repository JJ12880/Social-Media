namespace VideoPostOrganizer.Models;

public class OpenAiSettings
{
    public string Model { get; set; } = "gpt-4o-mini";
    public string ApiKey { get; set; } = string.Empty;
}
