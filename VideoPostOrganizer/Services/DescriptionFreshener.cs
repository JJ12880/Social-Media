using System.Text.Json;
using System.Text.RegularExpressions;
using OpenAI;
using OpenAI.Chat;
using VideoPostOrganizer.Models;

namespace VideoPostOrganizer.Services;

public class DescriptionFreshener
{
    private static readonly Regex UrlRegex = new(@"https?://\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MentionRegex = new(@"(?<!\w)@[A-Za-z0-9._]+", RegexOptions.Compiled);
    private static readonly Regex HashtagRegex = new(@"(^|\s)#[\p{L}\p{N}_]+", RegexOptions.Compiled);
    private readonly OpenAiSettings _settings;

    public DescriptionFreshener(OpenAiSettings settings)
    {
        _settings = settings;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.ApiKey);

    public async Task<string> RefreshDescriptionAsync(string originalText, IReadOnlyList<string> styleSamples)
    {
        if (string.IsNullOrWhiteSpace(originalText))
        {
            return originalText;
        }

        if (!IsConfigured)
        {
            throw new InvalidOperationException("OpenAI API key is not configured. Set OPENAI_API_KEY or appsettings.Local.json.");
        }

        var expectedUrls = UrlRegex.Matches(originalText).Select(m => m.Value).Distinct(StringComparer.Ordinal).ToList();
        var expectedMentions = MentionRegex.Matches(originalText).Select(m => m.Value).Distinct(StringComparer.Ordinal).ToList();
        var minLength = (int)Math.Floor(originalText.Length * 0.85);
        var maxLength = (int)Math.Ceiling(originalText.Length * 1.15);

        var client = new OpenAIClient(_settings.ApiKey);
        var chatClient = client.GetChatClient(_settings.Model);
        var styleContext = styleSamples.Count == 0
            ? "No prior samples provided."
            : string.Join("\n---\n", styleSamples.Where(x => !string.IsNullOrWhiteSpace(x)).TakeLast(20));

        var systemPrompt =
            "You are editing a social media caption for freshness. Keep the core meaning and factual claims unchanged. " +
            "Do not invent details, outcomes, metrics, people, locations, or events. " +
            "Preserve all @mentions and all URLs exactly. " +
            "Strip all hashtags from the rewritten output. " +
            "Match the author's style using provided historical examples. " +
            "Return only the rewritten caption text.";

        var userPrompt = $"""
Rewrite this caption for freshness with guardrails.

Original caption:
{originalText}

Author style examples:
{styleContext}

Rules:
1) Keep meaning and claims intact.
2) Keep output length between {minLength} and {maxLength} characters.
3) Preserve these mentions exactly: {(expectedMentions.Count == 0 ? "(none)" : string.Join(", ", expectedMentions))}
4) Preserve these URLs exactly: {(expectedUrls.Count == 0 ? "(none)" : string.Join(", ", expectedUrls))}
5) Remove hashtags.
6) Return only the final caption.
""";

        var completion = await chatClient.CompleteChatAsync(new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        });

        var rewritten = completion.Value.Content.FirstOrDefault()?.Text?.Trim() ?? string.Empty;
        rewritten = StripHashtags(rewritten);
        rewritten = NormalizeLineEndings(rewritten);

        ValidateOutput(originalText, rewritten, expectedMentions, expectedUrls, minLength, maxLength);
        return rewritten;
    }

    public static OpenAiSettings LoadSettings(string appBasePath)
    {
        var settings = new OpenAiSettings();
        var sharedPath = Path.Combine(appBasePath, "appsettings.json");
        var localPath = Path.Combine(appBasePath, "appsettings.Local.json");

        ApplySettingsFromFile(sharedPath, settings);
        ApplySettingsFromFile(localPath, settings);

        var envApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(envApiKey))
        {
            settings.ApiKey = envApiKey;
        }

        return settings;
    }

    private static void ApplySettingsFromFile(string path, OpenAiSettings settings)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("OpenAI", out var openAiElement))
            {
                return;
            }

            if (openAiElement.TryGetProperty("Model", out var modelElement))
            {
                var model = modelElement.GetString();
                if (!string.IsNullOrWhiteSpace(model))
                {
                    settings.Model = model;
                }
            }

            if (openAiElement.TryGetProperty("ApiKey", out var keyElement))
            {
                settings.ApiKey = keyElement.GetString() ?? settings.ApiKey;
            }
        }
        catch
        {
            // Ignore malformed optional config.
        }
    }

    private static string StripHashtags(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        var withoutTags = HashtagRegex.Replace(input, "$1");
        return Regex.Replace(withoutTags, @"[ \t]{2,}", " ").Trim();
    }

    private static void ValidateOutput(
        string original,
        string rewritten,
        IReadOnlyCollection<string> expectedMentions,
        IReadOnlyCollection<string> expectedUrls,
        int minLength,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(rewritten))
        {
            throw new InvalidOperationException("ChatGPT returned an empty description.");
        }

        if (rewritten.Length < minLength || rewritten.Length > maxLength)
        {
            throw new InvalidOperationException("Rewritten description did not meet length guardrails (+/-15%).");
        }

        if (HashtagRegex.IsMatch(rewritten))
        {
            throw new InvalidOperationException("Rewritten description still includes hashtags.");
        }

        foreach (var mention in expectedMentions)
        {
            if (!rewritten.Contains(mention, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Rewritten description is missing mention: {mention}");
            }
        }

        foreach (var url in expectedUrls)
        {
            if (!rewritten.Contains(url, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Rewritten description is missing URL: {url}");
            }
        }
    }

    private static string NormalizeLineEndings(string input) => input.Replace("\r\n", "\n").Replace("\r", "\n");
}
