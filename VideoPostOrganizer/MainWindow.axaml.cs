using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
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
    private static readonly Regex HashtagRegex = new(@"(^|\s)#[\p{L}\p{N}_]+", RegexOptions.Compiled);
    private static readonly Dictionary<string, string> DescriptionKeywordHashtags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["swarm"] = "#swarm",
        ["queen"] = "#honeybeequeen"
    };
    private readonly VideoLibraryService _service = new();
    private readonly DescriptionFreshener _descriptionFreshener;
    private readonly HashtagRuleEngine _hashtagRuleEngine = new();
    private HashtagRuleSet _hashtagRuleSet = new();

    private string _storageFoldersText = string.Empty;
    private string _sourceFolderText = string.Empty;
    private VideoEntry? _selectedEntry;
    private string? _selectedDescriptionFile;
    private string _descriptionText = string.Empty;
    private string _previewStatus = "Select a video to preview.";
    private string _performanceLevel = "Normal";
    private DateTimeOffset? _lastPostDate;
    private string _tagsText = string.Empty;
    private string _searchQuery = string.Empty;
    private string _generatedHashtagsText = string.Empty;
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
    private int _previewRequestId;
    private DateTimeOffset? _selectedScheduleDate = DateTimeOffset.Now.Date;
    private string _firstPostTime = "09:00";
    private string _repeatPostTime = "13:00";
    private string _firstPostSubtype = "post";
    private string _repeatPostSubtype = "reel";
    private string _repeatEveryDaysText = "7";
    private string _scheduleStatus = "Load storage to start scheduling.";
    private string _coreCountText = "3";
    private string _nicheCountText = "5";
    private string _testCountText = "2";
    private string _maxTagsText = "12";
    private string _cooldownDaysText = "7";

    public ObservableCollection<VideoEntry> Entries { get; } = new();
    public ObservableCollection<VideoEntry> ReadyEntries { get; } = new();
    public ObservableCollection<string> DescriptionFiles { get; } = new();
    public ObservableCollection<string> CoreHashtags { get; } = new();
    public ObservableCollection<string> NicheHashtags { get; } = new();
    public ObservableCollection<string> TestHashtags { get; } = new();
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
        _descriptionFreshener = new DescriptionFreshener(DescriptionFreshener.LoadSettings(AppContext.BaseDirectory));
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

            if (SelectedEntry is not null)
            {
                _ = PrepareEmbeddedPreviewAsync(SelectedEntry.VideoPath);
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
    public string GeneratedHashtagsText { get => _generatedHashtagsText; set => SetField(ref _generatedHashtagsText, value); }
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


    public DateTimeOffset? SelectedScheduleDate { get => _selectedScheduleDate; set => SetField(ref _selectedScheduleDate, value); }
    public string FirstPostTime { get => _firstPostTime; set => SetField(ref _firstPostTime, value); }
    public string RepeatPostTime { get => _repeatPostTime; set => SetField(ref _repeatPostTime, value); }
    public string FirstPostSubtype { get => _firstPostSubtype; set => SetField(ref _firstPostSubtype, value); }
    public string RepeatPostSubtype { get => _repeatPostSubtype; set => SetField(ref _repeatPostSubtype, value); }
    public string RepeatEveryDaysText { get => _repeatEveryDaysText; set => SetField(ref _repeatEveryDaysText, value); }
    public string ScheduleStatus { get => _scheduleStatus; set => SetField(ref _scheduleStatus, value); }
    public string CoreCountText { get => _coreCountText; set => SetField(ref _coreCountText, value); }
    public string NicheCountText { get => _nicheCountText; set => SetField(ref _nicheCountText, value); }
    public string TestCountText { get => _testCountText; set => SetField(ref _testCountText, value); }
    public string MaxTagsText { get => _maxTagsText; set => SetField(ref _maxTagsText, value); }
    public string CooldownDaysText { get => _cooldownDaysText; set => SetField(ref _cooldownDaysText, value); }

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

        _ = PrepareEmbeddedPreviewAsync(entry.VideoPath);
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

    private async void OnLoadStorageClick(object? sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        StorageFoldersText = folder;
        LoadStorageInternal();
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


    private async void OnImportInstagramArchiveClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PrimaryStorageFolder))
        {
            await ShowMessageAsync("Load at least one storage folder first.");
            return;
        }

        var archiveFolder = await PickFolderAsync();
        if (string.IsNullOrWhiteSpace(archiveFolder))
        {
            return;
        }

        var result = _service.ImportInstagramArchive(archiveFolder, PrimaryStorageFolder);
        _allEntries = _allEntries
            .Concat(result.ImportedEntries)
            .OrderBy(x => x.VideoName)
            .ToList();
        ApplyVideoFilter();

        await ShowMessageAsync($"Imported {result.ImportedEntries.Count} IG videos. Skipped duplicates: {result.DuplicateCount}.");
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
        LoadStorageInternal();
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
        LoadStorageInternal();
    }

    private async void OnRenameAllFilesClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PrimaryStorageFolder))
        {
            await ShowMessageAsync("Load storage first.");
            return;
        }

        if (!await ConfirmAsync("Rename all imported video files using AI titles? This will update folder and file names."))
        {
            return;
        }

        if (!_descriptionFreshener.IsConfigured)
        {
            await ShowMessageAsync("OpenAI API key is missing. Set OPENAI_API_KEY or appsettings.Local.json.");
            return;
        }

        var titleCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var renamed = 0;
        foreach (var entry in _allEntries.OrderBy(x => x.VideoName))
        {
            var descriptionFile = entry.DescriptionFiles.OrderBy(x => x).FirstOrDefault();
            var description = string.IsNullOrWhiteSpace(descriptionFile)
                ? entry.VideoName
                : _service.LoadDescription(entry, descriptionFile) ?? string.Empty;

            var cacheKey = description.Trim();
            if (!titleCache.TryGetValue(cacheKey, out var title))
            {
                try
                {
                    title = await _descriptionFreshener.GenerateTitleAsync(description);
                }
                catch (Exception ex)
                {
                    PreviewStatus = $"AI title failed for {entry.VideoName}: {ex.Message}";
                    continue;
                }

                if (string.IsNullOrWhiteSpace(title))
                {
                    title = entry.VideoName;
                }

                titleCache[cacheKey] = title;
            }

            var slug = VideoLibraryService.SlugifyTitle(title);
            if (string.IsNullOrWhiteSpace(slug))
            {
                slug = VideoLibraryService.SlugifyTitle(entry.VideoName);
            }

            if (string.IsNullOrWhiteSpace(slug))
            {
                slug = $"video-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            }

            _service.RenameVideoFileAndFolder(entry, slug);
            renamed++;
            PreviewStatus = $"Renamed {renamed}/{_allEntries.Count} files...";
        }

        LoadStorageInternal();
        PreviewStatus = $"Renamed {renamed} files.";
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

    private async void OnRefreshDescriptionClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedEntry is null)
        {
            await ShowMessageAsync("Select a video first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(DescriptionText))
        {
            await ShowMessageAsync("Description is empty.");
            return;
        }

        if (!_descriptionFreshener.IsConfigured)
        {
            await ShowMessageAsync("OpenAI API key is missing. Set OPENAI_API_KEY or appsettings.Local.json.");
            return;
        }

        try
        {
            var styleSamples = LoadStyleSamples();
            var rewritten = await _descriptionFreshener.RefreshDescriptionAsync(DescriptionText, styleSamples);
            DescriptionText = rewritten;
            SaveButtonText = "Review & Save";
            PreviewStatus = "Description refreshed for style and guardrails.";
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Unable to refresh description: {ex.Message}");
        }
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

    private async void OnAddCoreHashtagClick(object? sender, RoutedEventArgs e)
        => await PromptAndAddHashtagAsync(CoreHashtags, "Add core hashtag");

    private async void OnAddNicheHashtagClick(object? sender, RoutedEventArgs e)
        => await PromptAndAddHashtagAsync(NicheHashtags, "Add niche hashtag");

    private async void OnAddTestHashtagClick(object? sender, RoutedEventArgs e)
        => await PromptAndAddHashtagAsync(TestHashtags, "Add test hashtag");

    private async void OnRemoveCoreHashtagsClick(object? sender, RoutedEventArgs e)
        => await RemoveSelectedHashtagsAsync("CoreHashtagsList", CoreHashtags);

    private async void OnRemoveNicheHashtagsClick(object? sender, RoutedEventArgs e)
        => await RemoveSelectedHashtagsAsync("NicheHashtagsList", NicheHashtags);

    private async void OnRemoveTestHashtagsClick(object? sender, RoutedEventArgs e)
        => await RemoveSelectedHashtagsAsync("TestHashtagsList", TestHashtags);

    private async void OnGenerateHashtagsClick(object? sender, RoutedEventArgs e)
    {
        SyncHashtagRulesFromUi();
        var combinedDescriptionHashtags = CollectLibraryDescriptionHashtags()
            .Concat(ExtractHashtagsFromText(DescriptionText))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = _hashtagRuleEngine.BuildHashtags(_hashtagRuleSet, _allEntries, combinedDescriptionHashtags);
        GeneratedHashtagsText = string.Join(Environment.NewLine, result);
        await SaveHashtagRulesInternalAsync();
    }

    private void OnAppendGeneratedHashtagsClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GeneratedHashtagsText))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(DescriptionText) && !DescriptionText.EndsWith(Environment.NewLine))
        {
            DescriptionText += Environment.NewLine;
        }

        DescriptionText += GeneratedHashtagsText;
    }

    private async void OnSaveHashtagRulesClick(object? sender, RoutedEventArgs e)
    {
        await SaveHashtagRulesInternalAsync();
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

        RefreshReadyEntries();
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


    private void RefreshReadyEntries()
    {
        ReadyEntries.Clear();
        foreach (var entry in Entries.Where(x => x.ReadyForUse).OrderBy(x => x.VideoName))
        {
            ReadyEntries.Add(entry);
        }
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

    private void LoadStorageInternal()
    {
        _allEntries = ParseStorageFolders()
            .SelectMany(_service.LoadFromStorage)
            .OrderBy(x => x.VideoName)
            .ToList();
        ApplyVideoFilter();

        CoreHashtags.Clear();
        NicheHashtags.Clear();
        TestHashtags.Clear();
        ScheduledPosts.Clear();
        if (!string.IsNullOrWhiteSpace(PrimaryStorageFolder))
        {
            _hashtagRuleSet = _service.LoadHashtagRules(PrimaryStorageFolder);
            foreach (var tag in _hashtagRuleSet.CoreHashtags)
            {
                CoreHashtags.Add(tag);
            }

            foreach (var tag in _hashtagRuleSet.NicheHashtags)
            {
                NicheHashtags.Add(tag);
            }

            foreach (var tag in _hashtagRuleSet.TestHashtags)
            {
                TestHashtags.Add(tag);
            }

            CoreCountText = _hashtagRuleSet.CoreCount.ToString();
            NicheCountText = _hashtagRuleSet.NicheCount.ToString();
            TestCountText = _hashtagRuleSet.TestCount.ToString();
            MaxTagsText = _hashtagRuleSet.MaxTags.ToString();
            CooldownDaysText = _hashtagRuleSet.CooldownDays.ToString();

            foreach (var post in _service.LoadSchedule(PrimaryStorageFolder))
            {
                ScheduledPosts.Add(post);
            }

            var settings = _service.LoadScheduleSettings(PrimaryStorageFolder);
            FirstPostTime = settings.FirstPostTime;
            RepeatPostTime = settings.RepeatPostTime;
            FirstPostSubtype = settings.FirstPostSubtype;
            RepeatPostSubtype = settings.RepeatPostSubtype;
            RepeatEveryDaysText = settings.RepeatEveryDays.ToString();
            ScheduleStatus = "Schedule loaded.";
        }
    }

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

    private async Task PrepareEmbeddedPreviewAsync(string videoPath)
    {
        var requestId = Interlocked.Increment(ref _previewRequestId);
        if (!TryPrepareMedia(videoPath))
        {
            return;
        }

        var media = _currentMedia;
        if (media == null)
        {
            PreviewStatus = "Unable to prepare preview.";
            return;
        }

        _mediaPlayer.Play(media);
        await Task.Delay(50);
        if (requestId != _previewRequestId)
        {
            return;
        }

        _mediaPlayer.SetPause(true);
        PlayPauseButtonText = "Play";
        PreviewStatus = $"Ready in preview: {Path.GetFileName(videoPath)}";
    }


    private async Task SaveHashtagRulesInternalAsync()
    {
        if (string.IsNullOrWhiteSpace(PrimaryStorageFolder))
        {
            await ShowMessageAsync("Load storage first.");
            return;
        }

        SyncHashtagRulesFromUi();
        _service.SaveHashtagRules(PrimaryStorageFolder, _hashtagRuleSet);
        PreviewStatus = "Hashtag rules saved.";
    }

    private void SyncHashtagRulesFromUi()
    {
        _hashtagRuleSet.CoreHashtags = CoreHashtags.ToList();
        _hashtagRuleSet.NicheHashtags = NicheHashtags.ToList();
        _hashtagRuleSet.TestHashtags = TestHashtags.ToList();
        _hashtagRuleSet.CoreCount = ParseNonNegative(CoreCountText, 3);
        _hashtagRuleSet.NicheCount = ParseNonNegative(NicheCountText, 5);
        _hashtagRuleSet.TestCount = ParseNonNegative(TestCountText, 2);
        _hashtagRuleSet.MaxTags = ParsePositive(MaxTagsText, 12);
        _hashtagRuleSet.CooldownDays = ParseNonNegative(CooldownDaysText, 7);
    }

    private static int ParsePositive(string value, int fallback)
    {
        return int.TryParse(value, out var parsed) ? Math.Max(1, parsed) : fallback;
    }

    private static int ParseNonNegative(string value, int fallback)
    {
        return int.TryParse(value, out var parsed) ? Math.Max(0, parsed) : fallback;
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
        if (!TryPrepareMedia(videoPath))
        {
            return;
        }

        if (_currentMedia == null)
        {
            PreviewStatus = "Unable to load selected video.";
            return;
        }

        _mediaPlayer.Play(_currentMedia);
        PlayPauseButtonText = "Pause";
        PreviewStatus = $"Playing in preview: {Path.GetFileName(videoPath)}";
    }

    private bool TryPrepareMedia(string videoPath)
    {
        if (!EnsurePreviewHost())
        {
            return false;
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
            return false;
        }

        _mediaPlayer.Media = _currentMedia;
        return true;
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
            FirstPostTime = FirstPostTime,
            RepeatPostTime = RepeatPostTime,
            FirstPostSubtype = FirstPostSubtype,
            RepeatPostSubtype = RepeatPostSubtype,
            RepeatEveryDays = int.TryParse(RepeatEveryDaysText, out var days) ? Math.Max(1, days) : 7
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

        if (!TimeSpan.TryParse(FirstPostTime, out var firstPostTime) || !TimeSpan.TryParse(RepeatPostTime, out var repeatPostTime))
        {
            ScheduleStatus = "Post times must be HH:mm.";
            return;
        }

        if (!int.TryParse(RepeatEveryDaysText, out var repeatEveryDays) || repeatEveryDays < 1)
        {
            ScheduleStatus = "Repeat every must be a positive whole number.";
            return;
        }

        var baseDate = SelectedScheduleDate.Value.Date + firstPostTime;

        foreach (var entry in selected)
        {
            ScheduledPosts.Add(new ScheduledPost
            {
                VideoName = entry.VideoName,
                VideoFileName = entry.VideoFileName,
                FolderPath = entry.FolderPath,
                ScheduledAt = baseDate,
                PostSubtype = FirstPostSubtype,
                RepeatEveryDays = repeatEveryDays
            });

            ScheduledPosts.Add(new ScheduledPost
            {
                VideoName = entry.VideoName,
                VideoFileName = entry.VideoFileName,
                FolderPath = entry.FolderPath,
                ScheduledAt = SelectedScheduleDate.Value.Date.AddDays(repeatEveryDays) + repeatPostTime,
                PostSubtype = RepeatPostSubtype,
                RepeatEveryDays = repeatEveryDays
            });
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

    private List<string> LoadStyleSamples()
    {
        var samples = new List<string>();
        foreach (var entry in _allEntries)
        {
            foreach (var file in entry.DescriptionFiles)
            {
                var text = _service.LoadDescription(entry, file);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    samples.Add(text.Trim());
                }
            }
        }

        return samples;
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

    private async Task PromptAndAddHashtagAsync(ObservableCollection<string> target, string title)
    {
        var input = await PromptAsync(title, "Enter hashtag text:", string.Empty);
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        var normalized = HashtagRuleEngine.Normalize(input);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (!target.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            target.Add(normalized);
            await SaveHashtagRulesInternalAsync();
        }
    }

    private async Task RemoveSelectedHashtagsAsync(string listName, ObservableCollection<string> target)
    {
        var selected = this.FindControl<ListBox>(listName)?.SelectedItems?.OfType<string>().ToList() ?? new List<string>();
        if (selected.Count == 0)
        {
            return;
        }

        foreach (var tag in selected)
        {
            target.Remove(tag);
        }

        await SaveHashtagRulesInternalAsync();
    }

    private List<string> CollectLibraryDescriptionHashtags()
    {
        var collected = new List<string>();
        foreach (var entry in _allEntries)
        {
            foreach (var file in entry.DescriptionFiles)
            {
                var text = _service.LoadDescription(entry, file);
                collected.AddRange(ExtractHashtagsFromText(text));
            }
        }

        return collected
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ExtractHashtagsFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        var results = new List<string>();

        foreach (Match match in HashtagRegex.Matches(text))
        {
            var tag = match.Value.Trim();
            if (!string.IsNullOrWhiteSpace(tag))
            {
                results.Add(HashtagRuleEngine.Normalize(tag));
            }
        }

        foreach (var pair in DescriptionKeywordHashtags)
        {
            var pattern = $@"\b{Regex.Escape(pair.Key)}\w*\b";
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
            {
                results.Add(HashtagRuleEngine.Normalize(pair.Value));
            }
        }

        return results
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
