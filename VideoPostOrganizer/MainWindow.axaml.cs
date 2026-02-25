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

    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private VideoView? _videoView;
    private Media? _currentMedia;
    private bool _isPreviewHostReady;

    public ObservableCollection<VideoEntry> Entries { get; } = new();
    public ObservableCollection<string> DescriptionFiles { get; } = new();
    public ObservableCollection<string> CommonHashtags { get; } = new();
    public List<string> PerformanceLevels { get; } = new() { "Low", "Normal", "High" };

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
        if (!File.Exists(entry.VideoPath))
        {
            PreviewStatus = "Video file not found in storage.";
            _mediaPlayer.Stop();
            return;
        }

        StartEmbeddedPreview(entry.VideoPath);
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

    private void OnLoadStorageClick(object? sender, RoutedEventArgs e)
    {
        Entries.Clear();
        foreach (var entry in ParseStorageFolders().SelectMany(_service.LoadFromStorage).OrderBy(x => x.VideoName))
        {
            Entries.Add(entry);
        }

        CommonHashtags.Clear();
        if (!string.IsNullOrWhiteSpace(PrimaryStorageFolder))
        {
            foreach (var tag in _service.LoadCommonHashtags(PrimaryStorageFolder))
            {
                CommonHashtags.Add(tag);
            }
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
        foreach (var entry in result.ImportedEntries)
        {
            Entries.Add(entry);
        }

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

        if (!string.IsNullOrWhiteSpace(DescriptionText) && !DescriptionText.EndsWith(' '))
        {
            DescriptionText += " ";
        }

        DescriptionText += string.Join(" ", selected);
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

        _service.SaveMetadata(SelectedEntry);
        if (!string.IsNullOrWhiteSpace(SelectedDescriptionFile))
        {
            _service.SaveDescription(SelectedEntry, SelectedDescriptionFile, DescriptionText);
        }

        await ShowMessageAsync("Saved.");
    }


    private void OnPlayClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedEntry is null || !File.Exists(SelectedEntry.VideoPath))
        {
            PreviewStatus = "Video file not found in storage.";
            return;
        }

        StartEmbeddedPreview(SelectedEntry.VideoPath);
    }

    private void OnPauseClick(object? sender, RoutedEventArgs e)
    {
        _mediaPlayer.Pause();
        if (SelectedEntry is not null)
        {
            PreviewStatus = $"Paused: {Path.GetFileName(SelectedEntry.VideoPath)}";
        }
    }

    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        _mediaPlayer.Stop();
        PreviewStatus = "Stopped.";
    }

    private void StartEmbeddedPreview(string videoPath)
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
            PreviewStatus = "Unable to load selected video.";
            return;
        }

        _mediaPlayer.Play(_currentMedia);
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
