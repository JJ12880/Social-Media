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
    private string _newHashtag = string.Empty;
    private string _searchQuery = string.Empty;
    private string _generatedHashtagsText = string.Empty;
    private string _hashtagComposeSubtype = "post";
    private string _selectedHashtagTier = "Niche";
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
    private string _postMaxTagsText = "8";
    private string _reelMaxTagsText = "12";
    private string _cooldownDaysText = "7";

    public ObservableCollection<VideoEntry> Entries { get; } = new();
    public ObservableCollection<VideoEntry> ReadyEntries { get; } = new();
    public ObservableCollection<string> DescriptionFiles { get; } = new();
    public ObservableCollection<string> CoreHashtags { get; } = new();
    public ObservableCollection<string> NicheHashtags { get; } = new();
    public ObservableCollection<string> TestHashtags { get; } = new();
    public List<string> PerformanceLevels { get; } = new() { "Low", "Normal", "High" };
    public List<string> PostSubtypes { get; } = new() { "post", "reel" };
    public List<string> HashtagTiers { get; } = new() { "Core", "Niche", "Test" };
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
    public string GeneratedHashtagsText { get => _generatedHashtagsText; set => SetField(ref _generatedHashtagsText, value); }
    public string HashtagComposeSubtype { get => _hashtagComposeSubtype; set => SetField(ref _hashtagComposeSubtype, value); }
    public string SelectedHashtagTier { get => _selectedHashtagTier; set => SetField(ref _selectedHashtagTier, value); }
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
    public string PostMaxTagsText { get => _postMaxTagsText; set => SetField(ref _postMaxTagsText, value); }
    public string ReelMaxTagsText { get => _reelMaxTagsText; set => SetField(ref _reelMaxTagsText, value); }
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
            PostMaxTagsText = _hashtagRuleSet.PostMaxTags.ToString();
            ReelMaxTagsText = _hashtagRuleSet.ReelMaxTags.ToString();
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

    private async void OnAddRuleHashtagClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewHashtag))
        {
            return;
        }

        var targetCollection = GetTierCollection(SelectedHashtagTier);
        var normalized = HashtagRuleEngine.Normalize(NewHashtag);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (!targetCollection.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            targetCollection.Add(normalized);
            await SaveHashtagRulesInternalAsync();
            NewHashtag = string.Empty;
        }
    }

    private async void OnRemoveRuleHashtagsClick(object? sender, RoutedEventArgs e)
    {
        var coreSelected = this.FindControl<ListBox>("CoreHashtagsList")?.SelectedItems?.OfType<string>().ToList() ?? new List<string>();
        var nicheSelected = this.FindControl<ListBox>("NicheHashtagsList")?.SelectedItems?.OfType<string>().ToList() ?? new List<string>();
        var testSelected = this.FindControl<ListBox>("TestHashtagsList")?.SelectedItems?.OfType<string>().ToList() ?? new List<string>();

        foreach (var tag in coreSelected)
        {
            CoreHashtags.Remove(tag);
        }

        foreach (var tag in nicheSelected)
        {
            NicheHashtags.Remove(tag);
        }

        foreach (var tag in testSelected)
        {
            TestHashtags.Remove(tag);
        }

        await SaveHashtagRulesInternalAsync();
    }

    private async void OnGenerateHashtagsClick(object? sender, RoutedEventArgs e)
    {
        SyncHashtagRulesFromUi();
        var result = _hashtagRuleEngine.BuildHashtags(_hashtagRuleSet, _allEntries, HashtagComposeSubtype);
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


    private ObservableCollection<string> GetTierCollection(string? tier)
    {
        return tier?.Trim().ToLowerInvariant() switch
        {
            "core" => CoreHashtags,
            "test" => TestHashtags,
            _ => NicheHashtags
        };
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
        _hashtagRuleSet.PostMaxTags = ParsePositive(PostMaxTagsText, 8);
        _hashtagRuleSet.ReelMaxTags = ParsePositive(ReelMaxTagsText, 12);
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
