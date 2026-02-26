using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LibVLCSharp.Shared;
using LibVLCSharp.Avalonia;
using VideoPostOrganizer.Models;
using VideoPostOrganizer.Services;

namespace VideoPostOrganizer;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly VideoLibraryService _service = new();

    private string _storageFoldersText = string.Empty;
    private string _sourceFolderText = string.Empty;
    private VideoEntry? _selectedEntry;
    private string? _selectedDescriptionFile;
    private string _descriptionText = string.Empty;
    private string _previewStatus = "Select a video to preview.";
    private string _performanceLevel = "Normal";
    private DateTimeOffset? _lastPostDate;
    private string _tagsText = string.Empty;
    private string _newHashtag = string.Empty;
    private string _searchQuery = string.Empty;
    private bool _readyForUse;
    private string _saveButtonText = "Save Selected Video";
    private string _playPauseButtonText = "Play";

    private string _sortColumn = "name";
    private bool _sortAscending = true;

    private List<VideoEntry> _allEntries = new();

    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private VideoView? _videoView;
    private Media? _currentMedia;
    private bool _isPreviewHostReady;
    private double _nameColumnWidth = 220;
    private double _performanceColumnWidth = 120;
    private double _createdColumnWidth = 110;
    private double _readyColumnWidth = 80;
    private DateTimeOffset? _selectedScheduleDate = DateTimeOffset.Now.Date;
    private string _scheduleDefaultTime = "09:00";
    private string _firstPostSubtype = "post";
    private string _repeatPostSubtype = "reel";
    private int _repeatEveryDays = 7;
    private int _repeatCount;
    private string _scheduleStatus = "Load storage to start scheduling.";

    public ObservableCollection<VideoEntry> Entries { get; } = new();
    public ObservableCollection<string> DescriptionFiles { get; } = new();
    public ObservableCollection<string> CommonHashtags { get; } = new();
    public List<string> PerformanceLevels { get; } = new() { "Low", "Normal", "High" };
    public List<string> PostSubtypes { get; } = new() { "post", "reel" };
    public ObservableCollection<ScheduledPost> ScheduledPosts { get; } = new();

    private event PropertyChangedEventHandler? ViewModelPropertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => ViewModelPropertyChanged += value;
        remove => ViewModelPropertyChanged -= value;
    }

    public MainWindow()
    {
        Core.Initialize();
        _libVlc = new LibVLC();
        _mediaPlayer = new MediaPlayer(_libVlc);

        InitializeComponent();
        _videoView = this.FindControl<VideoView>("VideoPreviewView");
        DataContext = this;
        Opened += (_, _) =>
        {
            _isPreviewHostReady = true;
            if (_videoView != null && _videoView.MediaPlayer != _mediaPlayer)
            {
                _videoView.MediaPlayer = _mediaPlayer;
            }
        };
        Closed += OnWindowClosed;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public string StorageFoldersText { get => _storageFoldersText; set => SetField(ref _storageFoldersText, value); }
    public string SourceFolderText { get => _sourceFolderText; set => SetField(ref _sourceFolderText, value); }
    public VideoEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!SetField(ref _selectedEntry, value) || value is null)
            {
                return;
            }

            BindSelectedEntry(value);
        }
    }

    public string? SelectedDescriptionFile
    {
        get => _selectedDescriptionFile;
        set
        {
            if (!SetField(ref _selectedDescriptionFile, value))
            {
                return;
            }

            LoadSelectedDescription();
        }
    }

    public string DescriptionText { get => _descriptionText; set => SetField(ref _descriptionText, value); }
    public string PreviewStatus { get => _previewStatus; set => SetField(ref _previewStatus, value); }
    public string PerformanceLevel { get => _performanceLevel; set => SetField(ref _performanceLevel, value); }
    public DateTimeOffset? LastPostDate { get => _lastPostDate; set => SetField(ref _lastPostDate, value); }
    public string TagsText { get => _tagsText; set => SetField(ref _tagsText, value); }
    public string NewHashtag { get => _newHashtag; set => SetField(ref _newHashtag, value); }
    public bool ReadyForUse { get => _readyForUse; set => SetField(ref _readyForUse, value); }
    public string SaveButtonText { get => _saveButtonText; set => SetField(ref _saveButtonText, value); }
    public string PlayPauseButtonText { get => _playPauseButtonText; set => SetField(ref _playPauseButtonText, value); }
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetField(ref _searchQuery, value))
            {
                return;
            }

            ApplyVideoFilter();
        }
    }


    public double NameColumnWidth { get => _nameColumnWidth; set => SetField(ref _nameColumnWidth, value); }
    public double PerformanceColumnWidth { get => _performanceColumnWidth; set => SetField(ref _performanceColumnWidth, value); }
    public double CreatedColumnWidth { get => _createdColumnWidth; set => SetField(ref _createdColumnWidth, value); }
    public double ReadyColumnWidth { get => _readyColumnWidth; set => SetField(ref _readyColumnWidth, value); }
    public DateTimeOffset? SelectedScheduleDate { get => _selectedScheduleDate; set => SetField(ref _selectedScheduleDate, value); }
    public string ScheduleDefaultTime { get => _scheduleDefaultTime; set => SetField(ref _scheduleDefaultTime, value); }
    public string FirstPostSubtype { get => _firstPostSubtype; set => SetField(ref _firstPostSubtype, value); }
    public string RepeatPostSubtype { get => _repeatPostSubtype; set => SetField(ref _repeatPostSubtype, value); }
    public int RepeatEveryDays { get => _repeatEveryDays; set => SetField(ref _repeatEveryDays, Math.Max(1, value)); }
    public int RepeatCount { get => _repeatCount; set => SetField(ref _repeatCount, Math.Max(0, value)); }
    public string ScheduleStatus { get => _scheduleStatus; set => SetField(ref _scheduleStatus, value); }

    private string? PrimaryStorageFolder => ParseStorageFolders().FirstOrDefault();

    private List<string> ParseStorageFolders() => StorageFoldersText
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private void BindSelectedEntry(VideoEntry entry)
    {
        DescriptionFiles.Clear();
        foreach (var f in entry.DescriptionFiles.OrderBy(x => x))
        {
            DescriptionFiles.Add(f);
        }

        SelectedDescriptionFile = DescriptionFiles.FirstOrDefault();
        PerformanceLevel = entry.PerformanceLevel;
        LastPostDate = entry.LastPostDate.HasValue ? new DateTimeOffset(entry.LastPostDate.Value) : null;
        TagsText = string.Join(", ", entry.Tags);
        ReadyForUse = entry.ReadyForUse;
        SaveButtonText = "Save Selected Video";
        PlayPauseButtonText = "Play";
        if (!File.Exists(entry.VideoPath))
        {
            PreviewStatus = "Video file not found in storage.";
            _mediaPlayer.Stop();
            return;
        }

        PrepareEmbeddedPreview(entry.VideoPath);
    }

    private void LoadSelectedDescription()
    {
        if (SelectedEntry is null || string.IsNullOrWhiteSpace(SelectedDescriptionFile))
        {
            DescriptionText = string.Empty;
            return;
        }

        DescriptionText = _service.LoadDescription(SelectedEntry, SelectedDescriptionFile);
    }

    private async void OnAddStorageClick(object? sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        var set = ParseStorageFolders();
        if (!set.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            set.Add(folder);
        }

        StorageFoldersText = string.Join(";", set);
    }

    private async void OnBrowseSourceClick(object? sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(folder))
        {
            SourceFolderText = folder;
        }
    }

    private void OnClearSearchClick(object? sender, RoutedEventArgs e)
    {
        SearchQuery = string.Empty;
    }

    private void OnLoadStorageClick(object? sender, RoutedEventArgs e)
    {
        _allEntries = ParseStorageFolders()
            .SelectMany(_service.LoadFromStorage)
            .OrderBy(x => x.VideoName)
            .ToList();
        ApplyVideoFilter();

        CommonHashtags.Clear();
        ScheduledPosts.Clear();
        if (!string.IsNullOrWhiteSpace(PrimaryStorageFolder))
        {
            foreach (var tag in _service.LoadCommonHashtags(PrimaryStorageFolder))
            {
                CommonHashtags.Add(tag);
            }

            foreach (var post in _service.LoadSchedule(PrimaryStorageFolder))
            {
                ScheduledPosts.Add(post);
            }

            var settings = _service.LoadScheduleSettings(PrimaryStorageFolder);
            ScheduleDefaultTime = settings.DefaultPostTime;
            FirstPostSubtype = settings.FirstPostSubtype;
            RepeatPostSubtype = settings.RepeatPostSubtype;
            RepeatEveryDays = settings.RepeatEveryDays;
            RepeatCount = settings.RepeatCount;
            ScheduleStatus = "Schedule loaded.";
        }
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SourceFolderText) || string.IsNullOrWhiteSpace(PrimaryStorageFolder))
        {
            await ShowMessageAsync("Choose source folder and load at least one storage folder first.");
            return;
        }

        var result = _service.ImportVideos(SourceFolderText, PrimaryStorageFolder);
        _allEntries = _allEntries
            .Concat(result.ImportedEntries)
            .OrderBy(x => x.VideoName)
            .ToList();
        ApplyVideoFilter();

        await ShowMessageAsync($"Imported {result.ImportedEntries.Count} videos. Skipped duplicates: {result.DuplicateCount}.");
    }

    private async void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedEntry is null)
        {
            return;
        }

        var newName = await PromptAsync("Rename video", "Enter new name:", SelectedEntry.VideoName);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        if (IsVideoLoadedOrPlaying(SelectedEntry.VideoPath))
        {
            StopAndUnloadPreview();
        }

        _service.RenameVideo(SelectedEntry, newName);
        OnLoadStorageClick(sender, e);
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedEntry is null)
        {
            return;
        }

        if (!await ConfirmAsync($"Delete '{SelectedEntry.VideoName}' from storage?"))
        {
            return;
        }

        if (IsVideoLoadedOrPlaying(SelectedEntry.VideoPath))
        {
            StopAndUnloadPreview();
        }

        _service.DeleteVideo(SelectedEntry);
        OnLoadStorageClick(sender, e);
    }

    private async void OnAddDescriptionClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedEntry is null)
        {
            await ShowMessageAsync("Select a video first.");
            return;
        }

        var file = _service.AddDescription(SelectedEntry);
        DescriptionFiles.Add(file);
        SelectedDescriptionFile = file;
    }

    private async void OnOpenExternallyClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedEntry is null || !File.Exists(SelectedEntry.VideoPath))
        {
            await ShowMessageAsync("Video file not found in storage.");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = SelectedEntry.VideoPath,
            UseShellExecute = true
        });
    }


    private async void OnOpenInFolderClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedEntry is null || !Directory.Exists(SelectedEntry.FolderPath))
        {
            await ShowMessageAsync("Video folder not found.");
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{SelectedEntry.FolderPath}\"",
                    UseShellExecute = true
                });
                return;
            }

            if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"\"{SelectedEntry.FolderPath}\"",
                    UseShellExecute = false
                });
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = $"\"{SelectedEntry.FolderPath}\"",
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Unable to open folder: {ex.Message}");
        }
    }

    private void OnAddHashtagClick(object? sender, RoutedEventArgs e)
    {
        var storage = PrimaryStorageFolder;
        if (string.IsNullOrWhiteSpace(storage))
        {
            return;
        }

        var tag = NormalizeHashtag(NewHashtag);
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        if (!CommonHashtags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            CommonHashtags.Add(tag);
            _service.SaveCommonHashtags(storage, CommonHashtags.ToList());
            NewHashtag = string.Empty;
        }
    }

    private void OnRemoveHashtagClick(object? sender, RoutedEventArgs e)
    {
        var list = this.FindControl<ListBox>("CommonHashtagsList");
        var selected = list?.SelectedItems?.OfType<string>().ToList() ?? new List<string>();
        foreach (var tag in selected)
        {
            CommonHashtags.Remove(tag);
        }

        if (!string.IsNullOrWhiteSpace(PrimaryStorageFolder))
        {
            _service.SaveCommonHashtags(PrimaryStorageFolder, CommonHashtags.ToList());
        }
    }

    private void OnAppendHashtagClick(object? sender, RoutedEventArgs e)
    {
        var list = this.FindControl<ListBox>("CommonHashtagsList");
        var selected = list?.SelectedItems?.OfType<string>().ToList() ?? new List<string>();
        if (selected.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(DescriptionText) && !DescriptionText.EndsWith(Environment.NewLine))
        {
            DescriptionText += Environment.NewLine;
        }

        DescriptionText += string.Join(Environment.NewLine, selected);
    }


    private void ApplyVideoFilter()
    {
        var query = SearchQuery?.Trim() ?? string.Empty;
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allEntries
            : _allEntries.Where(x => x.VideoName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        var sorted = SortEntries(filtered);

        Entries.Clear();
        foreach (var entry in sorted)
        {
            Entries.Add(entry);
        }
    }


    private IEnumerable<VideoEntry> SortEntries(IEnumerable<VideoEntry> source)
    {
        return (_sortColumn, _sortAscending) switch
        {
            ("name", true) => source.OrderBy(x => x.VideoName),
            ("name", false) => source.OrderByDescending(x => x.VideoName),
            ("performance", true) => source.OrderBy(x => x.PerformanceLevel).ThenBy(x => x.VideoName),
            ("performance", false) => source.OrderByDescending(x => x.PerformanceLevel).ThenBy(x => x.VideoName),
            ("created", true) => source.OrderBy(x => x.FileCreatedOn ?? DateTime.MinValue).ThenBy(x => x.VideoName),
            ("created", false) => source.OrderByDescending(x => x.FileCreatedOn ?? DateTime.MinValue).ThenBy(x => x.VideoName),
            ("ready", true) => source.OrderBy(x => x.ReadyForUse).ThenBy(x => x.VideoName),
            ("ready", false) => source.OrderByDescending(x => x.ReadyForUse).ThenBy(x => x.VideoName),
            _ => source.OrderBy(x => x.VideoName)
        };
    }

    private void ToggleSort(string column)
    {
        if (_sortColumn.Equals(column, StringComparison.OrdinalIgnoreCase))
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = column;
            _sortAscending = true;
        }

        ApplyVideoFilter();
    }

    private void OnSortByNameClick(object? sender, RoutedEventArgs e) => ToggleSort("name");

    private void OnSortByPerformanceClick(object? sender, RoutedEventArgs e) => ToggleSort("performance");

    private void OnSortByCreatedClick(object? sender, RoutedEventArgs e) => ToggleSort("created");

    private void OnSortByReadyClick(object? sender, RoutedEventArgs e) => ToggleSort("ready");

    private bool IsVideoLoadedOrPlaying(string videoPath)
    {
        var selectedUri = new Uri(videoPath).AbsoluteUri;
        var loadedUri = _currentMedia?.Mrl;
        return string.Equals(loadedUri, selectedUri, StringComparison.OrdinalIgnoreCase);
    }

    private void StopAndUnloadPreview()
    {
        // Avoid UI hangs seen with _mediaPlayer.Stop() after pause on some platforms.
        if (_videoView != null)
        {
            _videoView.MediaPlayer = null;
        }

        _mediaPlayer.Media = null;
        _currentMedia?.Dispose();
        _currentMedia = null;

        if (_videoView != null)
        {
            _videoView.MediaPlayer = _mediaPlayer;
        }

        PlayPauseButtonText = "Play";
        PreviewStatus = "Preview unloaded.";
    }

    private void PrepareEmbeddedPreview(string videoPath)
    {
        if (!EnsurePreviewHost())
        {
            return;
        }

        var selectedUri = new Uri(videoPath).AbsoluteUri;
        var loadedUri = _currentMedia?.Mrl;
        if (!string.Equals(loadedUri, selectedUri, StringComparison.OrdinalIgnoreCase))
        {
            _currentMedia?.Dispose();
            _currentMedia = new Media(_libVlc, new Uri(videoPath));
        }

        if (_currentMedia == null)
        {
            PreviewStatus = "Unable to prepare preview.";
            return;
        }

        _mediaPlayer.Media = _currentMedia;

        // Render first frame in preview without continuing playback.
        _mediaPlayer.Play(_currentMedia);
        _mediaPlayer.SetPause(true);
        PlayPauseButtonText = "Play";
        PreviewStatus = $"Ready in preview: {Path.GetFileName(videoPath)}";
    }


    private void OnMoveHashtagUpClick(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<ListBox>("CommonHashtagsList")?.SelectedItem is not string selectedTag)
        {
            return;
        }

        var currentIndex = CommonHashtags.IndexOf(selectedTag);
        if (currentIndex <= 0)
        {
            return;
        }

        CommonHashtags.Move(currentIndex, currentIndex - 1);
        PersistCommonHashtagsOrder();
        this.FindControl<ListBox>("CommonHashtagsList")!.SelectedItem = selectedTag;
    }

    private void OnMoveHashtagDownClick(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<ListBox>("CommonHashtagsList")?.SelectedItem is not string selectedTag)
        {
            return;
        }

        var currentIndex = CommonHashtags.IndexOf(selectedTag);
        if (currentIndex < 0 || currentIndex >= CommonHashtags.Count - 1)
        {
            return;
        }

        CommonHashtags.Move(currentIndex, currentIndex + 1);
        PersistCommonHashtagsOrder();
        this.FindControl<ListBox>("CommonHashtagsList")!.SelectedItem = selectedTag;
    }

    private void PersistCommonHashtagsOrder()
    {
        if (string.IsNullOrWhiteSpace(PrimaryStorageFolder))
        {
            return;
        }

        _service.SaveCommonHashtags(PrimaryStorageFolder, CommonHashtags.ToList());
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedEntry is null)
        {
            return;
        }

        SelectedEntry.PerformanceLevel = PerformanceLevel;
        SelectedEntry.Tags = TagsText
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        SelectedEntry.LastPostDate = LastPostDate?.Date;
        SelectedEntry.ReadyForUse = ReadyForUse;

        _service.SaveMetadata(SelectedEntry);
        if (!string.IsNullOrWhiteSpace(SelectedDescriptionFile))
        {
            _service.SaveDescription(SelectedEntry, SelectedDescriptionFile, DescriptionText);
        }

        ApplyVideoFilter();

        SaveButtonText = "Saved!";
        await Task.Delay(2000);
        SaveButtonText = "Save Selected Video";
        PlayPauseButtonText = "Play";
    }


    private void OnPlayPauseClick(object? sender, RoutedEventArgs e)
    {
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
            PlayPauseButtonText = "Play";
            if (SelectedEntry is not null)
            {
                PreviewStatus = $"Paused: {Path.GetFileName(SelectedEntry.VideoPath)}";
            }

            return;
        }

        if (SelectedEntry is null || !File.Exists(SelectedEntry.VideoPath))
        {
            PreviewStatus = "Video file not found in storage.";
            return;
        }

        StartEmbeddedPreview(SelectedEntry.VideoPath);
        PlayPauseButtonText = "Pause";
    }


    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        _mediaPlayer.Stop();
        PlayPauseButtonText = "Play";
        PreviewStatus = "Stopped.";
    }

    private void StartEmbeddedPreview(string videoPath)
    {
        PrepareEmbeddedPreview(videoPath);

        if (_currentMedia == null)
        {
            PreviewStatus = "Unable to load selected video.";
            return;
        }

        _mediaPlayer.Play(_currentMedia);
        PlayPauseButtonText = "Pause";
        PreviewStatus = $"Playing in preview: {Path.GetFileName(videoPath)}";
    }

    private bool EnsurePreviewHost()
    {
        if (_videoView == null)
        {
            PreviewStatus = "Video preview host is unavailable.";
            return false;
        }

        if (_videoView.MediaPlayer != _mediaPlayer)
        {
            _videoView.MediaPlayer = _mediaPlayer;
        }

        if (!_isPreviewHostReady)
        {
            PreviewStatus = "Preview host is not ready yet. Try again.";
            return false;
        }

        return true;
    }


    private void OnSaveScheduleSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PrimaryStorageFolder))
        {
            ScheduleStatus = "Load storage first.";
            return;
        }

        var settings = new ScheduleSettings
        {
            DefaultPostTime = ScheduleDefaultTime,
            FirstPostSubtype = FirstPostSubtype,
            RepeatPostSubtype = RepeatPostSubtype,
            RepeatEveryDays = RepeatEveryDays,
            RepeatCount = RepeatCount
        };

        _service.SaveScheduleSettings(PrimaryStorageFolder, settings);
        ScheduleStatus = "Schedule settings saved.";
    }

    private void OnScheduleSelectedVideosClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PrimaryStorageFolder))
        {
            ScheduleStatus = "Load storage first.";
            return;
        }

        var selected = this.FindControl<ListBox>("ScheduleVideoList")?.SelectedItems?.OfType<VideoEntry>().ToList() ?? new List<VideoEntry>();
        if (selected.Count == 0)
        {
            ScheduleStatus = "Select one or more videos to schedule.";
            return;
        }

        if (SelectedScheduleDate is null)
        {
            ScheduleStatus = "Choose a target day.";
            return;
        }

        if (!TimeSpan.TryParse(ScheduleDefaultTime, out var postTime))
        {
            ScheduleStatus = "Time must be HH:mm.";
            return;
        }

        var baseDate = SelectedScheduleDate.Value.Date + postTime;

        foreach (var entry in selected)
        {
            ScheduledPosts.Add(new ScheduledPost
            {
                VideoName = entry.VideoName,
                VideoFileName = entry.VideoFileName,
                FolderPath = entry.FolderPath,
                ScheduledAt = baseDate,
                PostSubtype = FirstPostSubtype,
                RepeatEveryDays = RepeatEveryDays
            });

            for (var i = 1; i <= RepeatCount; i++)
            {
                ScheduledPosts.Add(new ScheduledPost
                {
                    VideoName = entry.VideoName,
                    VideoFileName = entry.VideoFileName,
                    FolderPath = entry.FolderPath,
                    ScheduledAt = baseDate.AddDays(RepeatEveryDays * i),
                    PostSubtype = RepeatPostSubtype,
                    RepeatEveryDays = RepeatEveryDays
                });
            }
        }

        PersistSchedule();
        ScheduleStatus = $"Scheduled {selected.Count} video(s) with repeats.";
    }

    private async void OnExportPublerCsvClick(object? sender, RoutedEventArgs e)
    {
        if (ScheduledPosts.Count == 0)
        {
            ScheduleStatus = "No scheduled posts to export.";
            return;
        }

        var filePath = await PickSaveFileAsync();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            ScheduleStatus = "Export canceled.";
            return;
        }

        var csv = PublerCsvExporter.BuildCsv(
            ScheduledPosts,
            ResolveScheduledDescription,
            ResolveScheduledMediaPath);

        File.WriteAllText(filePath, csv);
        ScheduleStatus = $"Exported CSV: {filePath}";
    }

    private string ResolveScheduledDescription(ScheduledPost post)
    {
        var entry = _allEntries.FirstOrDefault(x => string.Equals(x.FolderPath, post.FolderPath, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return string.Empty;
        }

        var descriptionFile = entry.DescriptionFiles.OrderBy(x => x).FirstOrDefault();
        return string.IsNullOrWhiteSpace(descriptionFile) ? string.Empty : _service.LoadDescription(entry, descriptionFile);
    }

    private string ResolveScheduledMediaPath(ScheduledPost post)
    {
        var preferredPath = Path.Combine(post.FolderPath, post.VideoFileName);
        return File.Exists(preferredPath) ? preferredPath : string.Empty;
    }

    private void PersistSchedule()
    {
        if (string.IsNullOrWhiteSpace(PrimaryStorageFolder))
        {
            return;
        }

        _service.SaveSchedule(PrimaryStorageFolder, ScheduledPosts.OrderBy(x => x.ScheduledAt).ToList());
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _mediaPlayer.Stop();
        _currentMedia?.Dispose();

        if (_videoView != null)
        {
            _videoView.MediaPlayer = null;
        }

        _mediaPlayer.Dispose();
        _libVlc.Dispose();
    }

    private static string NormalizeHashtag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith('#') ? trimmed : $"#{trimmed}";
    }

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose folder",
            AllowMultiple = false
        });

        return folders.FirstOrDefault()?.Path.LocalPath;
    }

    private async Task<string?> PickSaveFileAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Publer CSV",
            SuggestedFileName = "publer-schedule.csv",
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("CSV") { Patterns = new[] { "*.csv" } }
            }
        });

        return file?.Path.LocalPath;
    }

    private async Task ShowMessageAsync(string message)
    {
        var dialog = new TextPromptWindow("Info", message, okOnly: true);
        await dialog.ShowDialog(this);
    }

    private async Task<bool> ConfirmAsync(string message)
    {
        var dialog = new TextPromptWindow("Confirm", message, okOnly: false);
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task<string?> PromptAsync(string title, string message, string initialValue)
    {
        var dialog = new InputPromptWindow(title, message, initialValue);
        return await dialog.ShowDialog<string?>(this);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        ViewModelPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
