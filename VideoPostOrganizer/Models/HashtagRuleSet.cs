namespace VideoPostOrganizer.Models;

public class HashtagRuleSet
{
    public List<string> CoreHashtags { get; set; } = new();
    public List<string> NicheHashtags { get; set; } = new();
    public List<string> TestHashtags { get; set; } = new();
    public int CoreCount { get; set; } = 3;
    public int NicheCount { get; set; } = 5;
    public int TestCount { get; set; } = 2;
    public int PostMaxTags { get; set; } = 8;
    public int ReelMaxTags { get; set; } = 12;
    public int CooldownDays { get; set; } = 7;
}
