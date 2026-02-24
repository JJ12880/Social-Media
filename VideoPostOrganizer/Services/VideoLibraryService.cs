using System.Text.Json;
using VideoPostOrganizer.Models;

namespace VideoPostOrganizer.Services;

public class VideoLibraryService
{
    private const string MetadataFileName = "metadata.json";

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

    public List<VideoEntry> ImportVideos(string sourceFolder, string storageFolder)
    {
        if (!Directory.Exists(sourceFolder))
        {
            throw new DirectoryNotFoundException($"Source folder does not exist: {sourceFolder}");
        }

        Directory.CreateDirectory(storageFolder);

        var imported = new List<VideoEntry>();
        var videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".m4v"
        };

        foreach (var file in Directory.GetFiles(sourceFolder))
        {
            var extension = Path.GetExtension(file);
            if (!videoExtensions.Contains(extension))
            {
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
                DescriptionFiles = new List<string> { Path.GetFileName(descriptionFile) }
            };

            SaveMetadata(entry);
            imported.Add(entry);
        }

        return imported.OrderBy(x => x.VideoName).ToList();
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
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(metadataPath, json);
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
}
