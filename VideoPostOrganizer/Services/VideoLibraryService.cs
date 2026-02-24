using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using VideoPostOrganizer.Models;

namespace VideoPostOrganizer.Services;

public class VideoLibraryService
{
    private const string MetadataFileName = "metadata.json";
    private const string CommonHashtagsFileName = "common-hashtags.json";
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

    public List<string> LoadCommonHashtags(string storageFolder)
    {
        var hashtagsPath = Path.Combine(storageFolder, CommonHashtagsFileName);
        if (!File.Exists(hashtagsPath))
        {
            return new List<string>();
        }

        try
        {
            var json = File.ReadAllText(hashtagsPath);
            var tags = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            return tags
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    public void SaveCommonHashtags(string storageFolder, List<string> hashtags)
    {
        Directory.CreateDirectory(storageFolder);
        var hashtagsPath = Path.Combine(storageFolder, CommonHashtagsFileName);
        var normalized = hashtags
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        var json = JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(hashtagsPath, json);
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

    private static string NormalizePerformanceLevel(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "low" => "Low",
            "high" => "High",
            _ => "Normal"
        };
    }
    private static HashSet<string> BuildFingerprintIndex(string storageFolder, HashSet<string> videoExtensions)
    {
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);

        if (!Directory.Exists(storageFolder))
        {
            return fingerprints;
        }

        foreach (var directory in Directory.GetDirectories(storageFolder))
        {
            var videoPath = Directory
                .GetFiles(directory)
                .FirstOrDefault(x => videoExtensions.Contains(Path.GetExtension(x)));

            if (string.IsNullOrWhiteSpace(videoPath))
            {
                continue;
            }

            fingerprints.Add(ComputeFileFingerprint(videoPath));
        }

        return fingerprints;
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
