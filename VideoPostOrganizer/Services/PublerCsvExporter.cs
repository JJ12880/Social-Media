using System.Text;
using VideoPostOrganizer.Models;

namespace VideoPostOrganizer.Services;

public static class PublerCsvExporter
{
    private static readonly string[] Header =
    {
        "Date - Intl. format or prompt",
        "Text",
        "Link(s) - Separated by comma for FB carousels",
        "Media URL(s) - Separated by comma",
        "Title - For the video, pin, PDF ..",
        "Label(s) - Separated by comma",
        "Alt text(s) - Separated by ||",
        "Comment(s) - Separated by ||",
        "Pin board, FB album, or Google category",
        "Post subtype - I.e. story, reel, PDF ..",
        "CTA - For Facebook links or Google",
        "Reminder - For stories, reels, shorts, and TikToks"
    };

    public static string BuildCsv(IEnumerable<ScheduledPost> posts, Func<ScheduledPost, string> descriptionResolver, Func<ScheduledPost, string> mediaResolver)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', Header.Select(Escape)));

        foreach (var post in posts.OrderBy(x => x.ScheduledAt))
        {
            var row = new[]
            {
                post.ScheduledAt.ToString("yyyy-MM-dd HH:mm"),
                descriptionResolver(post),
                string.Empty,
                mediaResolver(post),
                post.VideoName,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                post.PostSubtype,
                string.Empty,
                string.Empty
            };

            sb.AppendLine(string.Join(',', row.Select(Escape)));
        }

        return sb.ToString();
    }

    private static string Escape(string? value)
    {
        var raw = value ?? string.Empty;
        if (!raw.Contains(',') && !raw.Contains('"') && !raw.Contains('\n') && !raw.Contains('\r'))
        {
            return raw;
        }

        return $"\"{raw.Replace("\"", "\"\"")}\"";
    }
}
