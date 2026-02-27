using VideoPostOrganizer.Models;

namespace VideoPostOrganizer.Services;

public class HashtagRuleEngine
{
    public List<string> BuildHashtags(HashtagRuleSet ruleSet, IReadOnlyCollection<VideoEntry> history, string subtype)
    {
        var now = DateTime.Now.Date;
        var isReel = string.Equals(subtype, "reel", StringComparison.OrdinalIgnoreCase);
        var maxTags = Math.Max(1, isReel ? ruleSet.ReelMaxTags : ruleSet.PostMaxTags);

        var selected = new List<string>();
        AppendFromTier(selected, ruleSet.CoreHashtags, Math.Max(0, ruleSet.CoreCount), history, ruleSet.CooldownDays, now);
        AppendFromTier(selected, ruleSet.NicheHashtags, Math.Max(0, ruleSet.NicheCount), history, ruleSet.CooldownDays, now);
        AppendFromTier(selected, ruleSet.TestHashtags, Math.Max(0, ruleSet.TestCount), history, ruleSet.CooldownDays, now);

        return selected
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxTags)
            .ToList();
    }

    private static void AppendFromTier(
        List<string> selected,
        IEnumerable<string> pool,
        int take,
        IReadOnlyCollection<VideoEntry> history,
        int cooldownDays,
        DateTime now)
    {
        if (take <= 0)
        {
            return;
        }

        var ranked = pool
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(tag => new
            {
                Tag = tag,
                Score = ComputeScore(tag, history),
                RecentlyUsed = WasUsedRecently(tag, history, cooldownDays, now)
            })
            .OrderBy(x => x.RecentlyUsed)
            .ThenByDescending(x => x.Score)
            .ThenBy(x => x.Tag)
            .Take(take)
            .Select(x => x.Tag);

        selected.AddRange(ranked);
    }

    private static double ComputeScore(string tag, IReadOnlyCollection<VideoEntry> history)
    {
        var score = 0d;
        foreach (var entry in history)
        {
            if (!entry.Tags.Any(x => string.Equals(Normalize(x), tag, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            score += entry.PerformanceLevel switch
            {
                "High" => 3,
                "Normal" => 1,
                _ => 0.25
            };

            if (entry.LastPostDate.HasValue)
            {
                var daysAgo = (DateTime.Now.Date - entry.LastPostDate.Value.Date).TotalDays;
                if (daysAgo <= 14)
                {
                    score += 0.5;
                }
            }
        }

        return score;
    }

    private static bool WasUsedRecently(string tag, IReadOnlyCollection<VideoEntry> history, int cooldownDays, DateTime now)
    {
        if (cooldownDays <= 0)
        {
            return false;
        }

        return history.Any(entry =>
            entry.LastPostDate.HasValue
            && (now - entry.LastPostDate.Value.Date).TotalDays <= cooldownDays
            && entry.Tags.Any(x => string.Equals(Normalize(x), tag, StringComparison.OrdinalIgnoreCase)));
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith('#') ? trimmed : $"#{trimmed}";
    }
}
