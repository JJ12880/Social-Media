using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using VideoPostOrganizer.Models;

namespace VideoPostOrganizer.Services;

public class VideoLibraryService
{
    private const string MetadataFileName = "metadata.json";
    private const string CommonHashtagsFileName = "common-hashtags.json";
    private const string HashtagRulesFileName = "hashtag-rules.json";
    private const string ScheduleFileName = "schedule.json";
    private const string ScheduleSettingsFileName = "schedule-settings.json";
    private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".m4v"
    };

    public List<VideoEntry> LoadFromStorage(string storageFolder)
    {
        if (!Directory.Exists(storageFolder))
        {
            return new List<VideoEntry>();
        }

        var entries = new List<VideoEntry>();
        foreach (var directory in Directory.GetDirectories(storageFolder))
        {
            var metadataPath = Path.Combine(directory, MetadataFileName);
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(metadataPath);
                var entry = JsonSerializer.Deserialize<VideoEntry>(json);
                if (entry != null)
                {
                    entry.FolderPath = directory;
                    entry.VideoPath = ResolveVideoPath(directory, entry.VideoFileName);
                    entry.PerformanceLevel = NormalizePerformanceLevel(entry.PerformanceLevel);
                    entries.Add(entry);
                }
            }
            catch
            {
                // Skip corrupted metadata and keep loading others.
            }
        }

        return entries.OrderBy(x => x.VideoName).ToList();
    }

    public ImportResult ImportVideos(string sourceFolder, string storageFolder)
    {
        if (!Directory.Exists(sourceFolder))
        {
            throw new DirectoryNotFoundException($"Source folder does not exist: {sourceFolder}");
        }

        Directory.CreateDirectory(storageFolder);

        var imported = new List<VideoEntry>();
        var duplicateCount = 0;
        var existingFingerprints = BuildFingerprintIndex(storageFolder, SupportedVideoExtensions);

        foreach (var file in Directory.GetFiles(sourceFolder))
        {
            var extension = Path.GetExtension(file);
            if (!SupportedVideoExtensions.Contains(extension))
            {
                continue;
            }

            var sourceFingerprint = ComputeFileFingerprint(file);
            if (existingFingerprints.Contains(sourceFingerprint))
            {
                duplicateCount++;
                continue;
            }

            var videoName = Path.GetFileNameWithoutExtension(file);
            var safeFolderName = string.Join("_", videoName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrWhiteSpace(safeFolderName))
            {
                safeFolderName = $"video_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            }

            var targetFolder = Path.Combine(storageFolder, safeFolderName);
            Directory.CreateDirectory(targetFolder);

            var targetVideoPath = Path.Combine(targetFolder, Path.GetFileName(file));
            File.Copy(file, targetVideoPath, overwrite: true);

            var descriptionFile = Path.Combine(targetFolder, "description-1.txt");
            if (!File.Exists(descriptionFile))
            {
                File.WriteAllText(descriptionFile, string.Empty);
            }

            var entry = new VideoEntry
            {
                VideoName = videoName,
                VideoFileName = Path.GetFileName(file),
                VideoPath = targetVideoPath,
                FolderPath = targetFolder,
                DescriptionFiles = new List<string> { Path.GetFileName(descriptionFile) },
                PerformanceLevel = "Normal"
            };

            SaveMetadata(entry);
            imported.Add(entry);
            existingFingerprints.Add(sourceFingerprint);
        }

        return new ImportResult(imported.OrderBy(x => x.VideoName).ToList(), duplicateCount);
    }


    public ImportResult ImportInstagramArchive(string archiveRootFolder, string storageFolder)
    {
        if (!Directory.Exists(archiveRootFolder))
        {
            throw new DirectoryNotFoundException($"Archive folder does not exist: {archiveRootFolder}");
        }

        Directory.CreateDirectory(storageFolder);

        var imported = new List<VideoEntry>();
        var duplicateCount = 0;
        var fingerprintIndex = BuildFingerprintEntryIndex(storageFolder, SupportedVideoExtensions);

        foreach (var jsonPath in Directory.GetFiles(archiveRootFolder, "*.json", SearchOption.AllDirectories))
        {
            JsonNode? root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(jsonPath));
            }
            catch
            {
                continue;
            }

            var reels = root?["ig_reels_media"]?.AsArray();
            if (reels is null)
            {
                continue;
            }

            foreach (var reel in reels)
            {
                var mediaArray = reel?["media"]?.AsArray();
                if (mediaArray is null)
                {
                    continue;
                }

                foreach (var media in mediaArray)
                {
                    var uri = media?["uri"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(uri))
                    {
                        continue;
                    }

                    var normalizedRelativePath = uri.Replace('/', Path.DirectorySeparatorChar);
                    var sourceVideoPath = Path.Combine(archiveRootFolder, normalizedRelativePath);
                    var extension = Path.GetExtension(sourceVideoPath);
                    if (!File.Exists(sourceVideoPath) || !SupportedVideoExtensions.Contains(extension))
                    {
                        continue;
                    }

                    var title = media?["title"]?.GetValue<string>() ?? string.Empty;
                    var normalizedTitle = NormalizeTitleForName(title);
                    var baseName = string.IsNullOrWhiteSpace(normalizedTitle)
                        ? Path.GetFileNameWithoutExtension(sourceVideoPath)
                        : normalizedTitle;

                    DateTime? sourceCreatedAt = null;
                    var timestamp = media?["creation_timestamp"]?.GetValue<long?>();
                    if (timestamp.HasValue && timestamp.Value > 0)
                    {
                        sourceCreatedAt = DateTimeOffset.FromUnixTimeSeconds(timestamp.Value).LocalDateTime;
                    }

                    var fingerprint = ComputeFileFingerprint(sourceVideoPath);
                    if (fingerprintIndex.TryGetValue(fingerprint, out var existing))
                    {
                        duplicateCount++;
                        if (IsOlder(sourceCreatedAt, existing.SourceCreationTime))
                        {
                            existing.Entry.SourceCreationTime = sourceCreatedAt;
                            existing.Entry.LastPostDate = sourceCreatedAt?.Date;
                            if (!string.IsNullOrWhiteSpace(title))
                            {
                                SavePrimaryDescription(existing.Entry, title);
                            }

                            SaveMetadata(existing.Entry);
                            fingerprintIndex[fingerprint] = new FingerprintEntry(existing.Entry, sourceCreatedAt);
                        }

                        continue;
                    }

                    var safeBaseName = ToSafeName(baseName);
                    var fileName = $"{safeBaseName}{extension}";
                    var targetFolder = GetUniqueFolderPath(Path.Combine(storageFolder, safeBaseName));
                    Directory.CreateDirectory(targetFolder);

                    var targetVideoPath = Path.Combine(targetFolder, fileName);
                    File.Copy(sourceVideoPath, targetVideoPath, overwrite: true);

                    if (sourceCreatedAt.HasValue)
                    {
                        File.SetLastWriteTime(targetVideoPath, sourceCreatedAt.Value);
                    }

                    var descriptionFile = Path.Combine(targetFolder, "description-1.txt");
                    File.WriteAllText(descriptionFile, title);

                    var entry = new VideoEntry
                    {
                        VideoName = baseName,
                        VideoFileName = fileName,
                        VideoPath = targetVideoPath,
                        FolderPath = targetFolder,
                        DescriptionFiles = new List<string> { Path.GetFileName(descriptionFile) },
                        PerformanceLevel = "Normal",
                        SourceCreationTime = sourceCreatedAt,
                        LastPostDate = sourceCreatedAt?.Date
                    };

                    SaveMetadata(entry);
                    imported.Add(entry);
                    fingerprintIndex[fingerprint] = new FingerprintEntry(entry, sourceCreatedAt);
                }
            }
        }

        return new ImportResult(imported.OrderBy(x => x.VideoName).ToList(), duplicateCount);
    }

    public void DeleteVideo(VideoEntry entry)
    {
        if (Directory.Exists(entry.FolderPath))
        {
            Directory.Delete(entry.FolderPath, recursive: true);
        }
    }



    public void RenameVideo(VideoEntry entry, string newVideoName)
    {
        if (string.IsNullOrWhiteSpace(newVideoName))
        {
            throw new ArgumentException("Video name cannot be empty.", nameof(newVideoName));
        }

        var trimmedName = newVideoName.Trim();
        var safeFolderName = string.Join("_", trimmedName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeFolderName))
        {
            throw new InvalidOperationException("The new video name is invalid for a folder name.");
        }

        var parentFolder = Directory.GetParent(entry.FolderPath)?.FullName
            ?? throw new InvalidOperationException("Unable to resolve storage folder.");

        var requestedPath = Path.Combine(parentFolder, safeFolderName);
        var targetFolder = string.Equals(requestedPath, entry.FolderPath, StringComparison.OrdinalIgnoreCase)
            ? entry.FolderPath
            : GetUniqueFolderPath(requestedPath);

        if (!string.Equals(targetFolder, entry.FolderPath, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(entry.FolderPath, targetFolder);
            entry.FolderPath = targetFolder;
            entry.VideoPath = Path.Combine(targetFolder, entry.VideoFileName);
        }

        entry.VideoName = trimmedName;
        SaveMetadata(entry);
    }

    public void SaveMetadata(VideoEntry entry)
    {
        var metadataPath = Path.Combine(entry.FolderPath, MetadataFileName);
        entry.PerformanceLevel = NormalizePerformanceLevel(entry.PerformanceLevel);
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(metadataPath, json);
    }

    public HashtagRuleSet LoadHashtagRules(string storageFolder)
    {
        var rulesPath = Path.Combine(storageFolder, HashtagRulesFileName);
        if (File.Exists(rulesPath))
        {
            try
            {
                var json = File.ReadAllText(rulesPath);
                var legacyMaxTags = TryReadLegacyMaxTags(json);
                var loaded = JsonSerializer.Deserialize<HashtagRuleSet>(json);
                if (loaded != null)
                {
                    loaded.CoreHashtags = NormalizeHashtags(loaded.CoreHashtags);
                    loaded.NicheHashtags = NormalizeHashtags(loaded.NicheHashtags);
                    loaded.TestHashtags = NormalizeHashtags(loaded.TestHashtags);
                    loaded.CoreCount = Math.Max(0, loaded.CoreCount);
                    loaded.NicheCount = Math.Max(0, loaded.NicheCount);
                    loaded.TestCount = Math.Max(0, loaded.TestCount);
                    loaded.MaxTags = Math.Max(1, loaded.MaxTags);
                    if (legacyMaxTags.HasValue)
                    {
                        loaded.MaxTags = Math.Max(1, legacyMaxTags.Value);
                    }

                    loaded.CooldownDays = Math.Max(0, loaded.CooldownDays);
                    return loaded;
                }
            }
            catch
            {
                // Fall back to defaults below.
            }
        }

        var legacyPath = Path.Combine(storageFolder, CommonHashtagsFileName);
        var defaults = new HashtagRuleSet();
        if (!File.Exists(legacyPath))
        {
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(legacyPath);
            var tags = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            defaults.NicheHashtags = NormalizeHashtags(tags);
        }
        catch
        {
            // Keep defaults.
        }

        return defaults;
    }

    public void SaveHashtagRules(string storageFolder, HashtagRuleSet rules)
    {
        Directory.CreateDirectory(storageFolder);
        rules.CoreHashtags = NormalizeHashtags(rules.CoreHashtags);
        rules.NicheHashtags = NormalizeHashtags(rules.NicheHashtags);
        rules.TestHashtags = NormalizeHashtags(rules.TestHashtags);
        rules.CoreCount = Math.Max(0, rules.CoreCount);
        rules.NicheCount = Math.Max(0, rules.NicheCount);
        rules.TestCount = Math.Max(0, rules.TestCount);
        rules.MaxTags = Math.Max(1, rules.MaxTags);
        rules.CooldownDays = Math.Max(0, rules.CooldownDays);

        var rulesPath = Path.Combine(storageFolder, HashtagRulesFileName);
        var json = JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(rulesPath, json);
    }

    public List<ScheduledPost> LoadSchedule(string storageFolder)
    {
        var schedulePath = Path.Combine(storageFolder, ScheduleFileName);
        if (!File.Exists(schedulePath))
        {
            return new List<ScheduledPost>();
        }

        try
        {
            var json = File.ReadAllText(schedulePath);
            return JsonSerializer.Deserialize<List<ScheduledPost>>(json) ?? new List<ScheduledPost>();
        }
        catch
        {
            return new List<ScheduledPost>();
        }
    }

    public void SaveSchedule(string storageFolder, List<ScheduledPost> schedule)
    {
        Directory.CreateDirectory(storageFolder);
        var schedulePath = Path.Combine(storageFolder, ScheduleFileName);
        var json = JsonSerializer.Serialize(schedule.OrderBy(x => x.ScheduledAt).ToList(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(schedulePath, json);
    }

    public ScheduleSettings LoadScheduleSettings(string storageFolder)
    {
        var settingsPath = Path.Combine(storageFolder, ScheduleSettingsFileName);
        if (!File.Exists(settingsPath))
        {
            return new ScheduleSettings();
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            return JsonSerializer.Deserialize<ScheduleSettings>(json) ?? new ScheduleSettings();
        }
        catch
        {
            return new ScheduleSettings();
        }
    }

   

   

    

  

    public void SaveScheduleSettings(string storageFolder, ScheduleSettings settings)
    {
        Directory.CreateDirectory(storageFolder);
        var settingsPath = Path.Combine(storageFolder, ScheduleSettingsFileName);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(settingsPath, json);
    }

    public string AddDescription(VideoEntry entry)
    {
        var nextIndex = entry.DescriptionFiles
            .Select(GetDescriptionIndex)
            .DefaultIfEmpty(0)
            .Max() + 1;

        var fileName = $"description-{nextIndex}.txt";
        var fullPath = Path.Combine(entry.FolderPath, fileName);
        File.WriteAllText(fullPath, string.Empty);

        entry.DescriptionFiles.Add(fileName);
        entry.DescriptionFiles = entry.DescriptionFiles.OrderBy(x => x).ToList();
        SaveMetadata(entry);

        return fileName;
    }

    public string LoadDescription(VideoEntry entry, string descriptionFile)
    {
        var fullPath = Path.Combine(entry.FolderPath, descriptionFile);
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
    }

    public void SaveDescription(VideoEntry entry, string descriptionFile, string content)
    {
        var fullPath = Path.Combine(entry.FolderPath, descriptionFile);
        File.WriteAllText(fullPath, content ?? string.Empty);
    }

    private static int GetDescriptionIndex(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var parts = name.Split('-');
        return parts.Length > 1 && int.TryParse(parts[^1], out var n) ? n : 0;
    }

    private static string GetUniqueFolderPath(string basePath)
    {
        if (!Directory.Exists(basePath))
        {
            return basePath;
        }

        for (var i = 1; i < 1000; i++)
        {
            var candidate = $"{basePath}-{i}";
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Unable to generate a unique folder name for renamed video.");
    }


    private static Dictionary<string, FingerprintEntry> BuildFingerprintEntryIndex(string storageFolder, HashSet<string> videoExtensions)
    {
        var index = new Dictionary<string, FingerprintEntry>(StringComparer.Ordinal);
        foreach (var entry in LoadEntriesForFingerprint(storageFolder, videoExtensions))
        {
            index[entry.Fingerprint] = new FingerprintEntry(entry.VideoEntry, entry.VideoEntry.SourceCreationTime);
        }

        return index;
    }

    private static HashSet<string> BuildFingerprintIndex(string storageFolder, HashSet<string> videoExtensions)
    {
        return LoadEntriesForFingerprint(storageFolder, videoExtensions)
            .Select(x => x.Fingerprint)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<(string Fingerprint, VideoEntry VideoEntry)> LoadEntriesForFingerprint(string storageFolder, HashSet<string> videoExtensions)
    {
        if (!Directory.Exists(storageFolder))
        {
            yield break;
        }

        foreach (var directory in Directory.GetDirectories(storageFolder))
        {
            var metadataPath = Path.Combine(directory, MetadataFileName);
            VideoEntry? entry = null;
            if (File.Exists(metadataPath))
            {
                try
                {
                    entry = JsonSerializer.Deserialize<VideoEntry>(File.ReadAllText(metadataPath));
                }
                catch
                {
                    entry = null;
                }
            }

            var videoPath = Directory
                .GetFiles(directory)
                .FirstOrDefault(x => videoExtensions.Contains(Path.GetExtension(x)));

            if (string.IsNullOrWhiteSpace(videoPath))
            {
                continue;
            }

            entry ??= new VideoEntry
            {
                VideoName = Path.GetFileNameWithoutExtension(videoPath),
                VideoFileName = Path.GetFileName(videoPath),
                VideoPath = videoPath,
                FolderPath = directory
            };

            entry.FolderPath = directory;
            entry.VideoPath = videoPath;
            yield return (ComputeFileFingerprint(videoPath), entry);
        }
    }

    private static string NormalizeTitleForName(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var singleLine = string.Join(' ', title
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return singleLine.Length <= 50 ? singleLine : singleLine[..50].TrimEnd();
    }

    private static string ToSafeName(string value)
    {
        var safe = string.Join("_", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = $"video_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        }

        return safe;
    }

    private static bool IsOlder(DateTime? candidate, DateTime? existing)
    {
        if (!candidate.HasValue)
        {
            return false;
        }

        if (!existing.HasValue)
        {
            return true;
        }

        return candidate.Value < existing.Value;
    }

    private void SavePrimaryDescription(VideoEntry entry, string content)
    {
        var descriptionFileName = entry.DescriptionFiles.OrderBy(x => x).FirstOrDefault() ?? "description-1.txt";
        if (!entry.DescriptionFiles.Contains(descriptionFileName, StringComparer.OrdinalIgnoreCase))
        {
            entry.DescriptionFiles.Add(descriptionFileName);
            entry.DescriptionFiles = entry.DescriptionFiles.OrderBy(x => x).ToList();
        }

        SaveDescription(entry, descriptionFileName, content);
    }

    private sealed record FingerprintEntry(VideoEntry Entry, DateTime? SourceCreationTime);

    private static int? TryReadLegacyMaxTags(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("MaxTags", out _))
            {
                return null;
            }

            var legacyValues = new List<int>();
            if (root.TryGetProperty("PostMaxTags", out var postElement) && postElement.TryGetInt32(out var postMax))
            {
                legacyValues.Add(postMax);
            }

            if (root.TryGetProperty("ReelMaxTags", out var reelElement) && reelElement.TryGetInt32(out var reelMax))
            {
                legacyValues.Add(reelMax);
            }

            return legacyValues.Count == 0 ? null : legacyValues.Max();
        }
        catch
        {
            return null;
        }
    }


    private static List<string> NormalizeHashtags(IEnumerable<string> hashtags)
    {
        return hashtags
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Select(x => x.StartsWith('#') ? x : $"#{x}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
    }

    private static string NormalizePerformanceLevel(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "low" => "Low",
            "high" => "High",
            _ => "Normal"
        };
    }
    private static string ResolveVideoPath(string directory, string videoFileName)
    {
        var preferredPath = Path.Combine(directory, videoFileName);
        if (File.Exists(preferredPath))
        {
            return preferredPath;
        }

        return Directory
            .GetFiles(directory)
            .FirstOrDefault(x => SupportedVideoExtensions.Contains(Path.GetExtension(x)))
            ?? preferredPath;
    }

    private static string ComputeFileFingerprint(string filePath)
    {
        const int partialThresholdBytes = 64 * 1024 * 1024;
        const int partialBytes = 4 * 1024 * 1024;

        using var stream = File.OpenRead(filePath);
        var fileLengthBytes = stream.Length;
        using var sha = SHA256.Create();

        if (fileLengthBytes > partialThresholdBytes)
        {
            var bufferSize = (int)Math.Min(partialBytes, fileLengthBytes);
            var buffer = new byte[bufferSize];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);

            sha.TransformBlock(buffer, 0, bytesRead, null, 0);
            var lengthBytes = BitConverter.GetBytes(fileLengthBytes);
            sha.TransformFinalBlock(lengthBytes, 0, lengthBytes.Length);
            return Convert.ToHexString(sha.Hash!);
        }

        var fullHash = sha.ComputeHash(stream);
        return Convert.ToHexString(fullHash);
    }
}


public sealed record ImportResult(List<VideoEntry> ImportedEntries, int DuplicateCount);
