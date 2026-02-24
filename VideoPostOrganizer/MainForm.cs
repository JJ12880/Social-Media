using System.Windows;
using System.Windows.Media;
using System.Windows.Forms.Integration;
using VideoPostOrganizer.Models;
using VideoPostOrganizer.Services;
using System.IO;

namespace VideoPostOrganizer;

public class MainForm : Form
{
    private readonly VideoLibraryService _service = new();

    private readonly TextBox _storageFolderTextBox = new() { Width = 560 };
    private readonly TextBox _sourceFolderTextBox = new() { Width = 560 };
    private readonly ListBox _videoListBox = new() { Width = 360, Height = 420 };
    private readonly ComboBox _descriptionSelector = new() { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _descriptionEditor = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Width = 520, Height = 170 };
    private readonly TextBox _tagsTextBox = new() { Width = 520 };
    private readonly ListBox _commonHashtagsListBox = new() { Width = 520, Height = 90, SelectionMode = SelectionMode.MultiExtended };
    private readonly TextBox _commonHashtagInput = new() { Width = 360 };
    private readonly DateTimePicker _lastPostDatePicker = new() { Width = 200, Format = DateTimePickerFormat.Short, ShowCheckBox = true };
    private readonly Label _previewStatusLabel = new() { AutoSize = true, Text = "Select a video to preview." };

    private readonly RadioButton _performanceLowRadio = new() { Text = "Low", AutoSize = true };
    private readonly RadioButton _performanceNormalRadio = new() { Text = "Normal", AutoSize = true, Checked = true };
    private readonly RadioButton _performanceHighRadio = new() { Text = "High", AutoSize = true };

    private readonly ElementHost _videoPreviewHost = new() { Width = 520, Height = 280 };
    private readonly System.Windows.Controls.MediaElement _mediaElement = new()
    {
        LoadedBehavior = System.Windows.Controls.MediaState.Manual,
        UnloadedBehavior = System.Windows.Controls.MediaState.Stop,
        Stretch = Stretch.Uniform
    };

    private readonly BindingSource _entriesBinding = new();
    private List<VideoEntry> _entries = new();
    private bool _showThumbnailOnOpen;

    public MainForm()
    {
        Text = "Social Media Video Organizer";
        Width = 980;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;

        _videoPreviewHost.Child = _mediaElement;
        _mediaElement.MediaOpened += MediaElementOnMediaOpened;

        var browseStorageButton = new Button { Text = "Browse..." };
        browseStorageButton.Click += (_, _) => BrowseFolder(_storageFolderTextBox);

        var loadStorageButton = new Button { Text = "Load Storage" };
        loadStorageButton.Click += (_, _) => LoadStorage();

        var browseSourceButton = new Button { Text = "Browse..." };
        browseSourceButton.Click += (_, _) => BrowseFolder(_sourceFolderTextBox);

        var importButton = new Button { Text = "Import Videos" };
        importButton.Click += (_, _) => ImportVideos();

        var addDescriptionButton = new Button { Text = "Add Description" };
        addDescriptionButton.Click += (_, _) => AddDescription();

        var saveButton = new Button { Text = "Save Selected Video" };
        saveButton.Click += (_, _) => SaveCurrentVideo();

        var addCommonHashtagButton = new Button { Text = "Add Hashtag" };
        addCommonHashtagButton.Click += (_, _) => AddCommonHashtag();

        var removeCommonHashtagButton = new Button { Text = "Remove Selected" };
        removeCommonHashtagButton.Click += (_, _) => RemoveSelectedCommonHashtags();

        var appendCommonHashtagButton = new Button { Text = "Append Selected to Description" };
        appendCommonHashtagButton.Click += (_, _) => AppendSelectedCommonHashtagsToDescription();

        var playButton = new Button { Text = "Play" };
        playButton.Click += (_, _) => _mediaElement.Play();

        var pauseButton = new Button { Text = "Pause" };
        pauseButton.Click += (_, _) => _mediaElement.Pause();

        var stopButton = new Button { Text = "Stop" };
        stopButton.Click += (_, _) => _mediaElement.Stop();

        var videoMenu = new ContextMenuStrip();
        var renameMenuItem = new ToolStripMenuItem("Rename");
        renameMenuItem.Click += (_, _) => RenameSelectedVideo();
        videoMenu.Items.Add(renameMenuItem);

        var deleteMenuItem = new ToolStripMenuItem("Delete from Storage");
        deleteMenuItem.Click += (_, _) => DeleteSelectedVideo();
        videoMenu.Items.Add(deleteMenuItem);

        _videoListBox.DisplayMember = nameof(VideoEntry.DisplayName);
        _videoListBox.DataSource = _entriesBinding;
        _videoListBox.ContextMenuStrip = videoMenu;
        _videoListBox.MouseDown += VideoListBoxOnMouseDown;
        _videoListBox.SelectedIndexChanged += (_, _) => LoadSelectedVideo();
        _descriptionSelector.SelectedIndexChanged += (_, _) => LoadSelectedDescription();

        var topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 110,
            ColumnCount = 4,
            RowCount = 2,
            Padding = new Padding(8)
        };

        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        topPanel.Controls.Add(new Label { Text = "Storage folder:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        topPanel.Controls.Add(_storageFolderTextBox, 1, 0);
        topPanel.Controls.Add(browseStorageButton, 2, 0);
        topPanel.Controls.Add(loadStorageButton, 3, 0);

        topPanel.Controls.Add(new Label { Text = "Source videos:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        topPanel.Controls.Add(_sourceFolderTextBox, 1, 1);
        topPanel.Controls.Add(browseSourceButton, 2, 1);
        topPanel.Controls.Add(importButton, 3, 1);

        var leftPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            Width = 380,
            FlowDirection = System.Windows.Forms.FlowDirection.TopDown,
            Padding = new Padding(8)
        };

        leftPanel.Controls.Add(new Label { Text = "Videos", AutoSize = true });
        leftPanel.Controls.Add(_videoListBox);

        var previewControlRow = new FlowLayoutPanel { Width = 700, Height = 38 };
        previewControlRow.Controls.Add(new Label { Text = "Preview:", Width = 100, TextAlign = ContentAlignment.MiddleLeft });
        previewControlRow.Controls.Add(playButton);
        previewControlRow.Controls.Add(pauseButton);
        previewControlRow.Controls.Add(stopButton);

        var descriptionRow = new FlowLayoutPanel { Width = 700, Height = 38 };
        descriptionRow.Controls.Add(new Label { Text = "Description file:", Width = 100, TextAlign = ContentAlignment.MiddleLeft });
        descriptionRow.Controls.Add(_descriptionSelector);
        descriptionRow.Controls.Add(addDescriptionButton);

        var performanceRow = new FlowLayoutPanel { Width = 700, Height = 38 };
        performanceRow.Controls.Add(new Label { Text = "Performance:", Width = 100, TextAlign = ContentAlignment.MiddleLeft });
        performanceRow.Controls.Add(_performanceLowRadio);
        performanceRow.Controls.Add(_performanceNormalRadio);
        performanceRow.Controls.Add(_performanceHighRadio);

        var hashtagInputRow = new FlowLayoutPanel { Width = 700, Height = 38 };
        hashtagInputRow.Controls.Add(new Label { Text = "Common hashtags:", Width = 100, TextAlign = ContentAlignment.MiddleLeft });
        hashtagInputRow.Controls.Add(_commonHashtagInput);
        hashtagInputRow.Controls.Add(addCommonHashtagButton);
        hashtagInputRow.Controls.Add(removeCommonHashtagButton);

        var hashtagActionsRow = new FlowLayoutPanel { Width = 700, Height = 38 };
        hashtagActionsRow.Controls.Add(appendCommonHashtagButton);

        var dateRow = new FlowLayoutPanel { Width = 700, Height = 38 };
        dateRow.Controls.Add(new Label { Text = "Last post date:", Width = 100, TextAlign = ContentAlignment.MiddleLeft });
        dateRow.Controls.Add(_lastPostDatePicker);

        var rightPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = System.Windows.Forms.FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(8)
        };

        rightPanel.Controls.Add(previewControlRow);
        rightPanel.Controls.Add(_previewStatusLabel);
        rightPanel.Controls.Add(_videoPreviewHost);
        rightPanel.Controls.Add(descriptionRow);
        rightPanel.Controls.Add(new Label { Text = "Description text", AutoSize = true });
        rightPanel.Controls.Add(_descriptionEditor);
        rightPanel.Controls.Add(new Label { Text = "Common Hashtags", AutoSize = true });
        rightPanel.Controls.Add(hashtagInputRow);
        rightPanel.Controls.Add(_commonHashtagsListBox);
        rightPanel.Controls.Add(hashtagActionsRow);
        rightPanel.Controls.Add(performanceRow);
        rightPanel.Controls.Add(new Label { Text = "Tags (comma-separated)", AutoSize = true });
        rightPanel.Controls.Add(_tagsTextBox);
        rightPanel.Controls.Add(dateRow);
        rightPanel.Controls.Add(saveButton);

        Controls.Add(rightPanel);
        Controls.Add(leftPanel);
        Controls.Add(topPanel);
    }

    private void VideoListBoxOnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        var index = _videoListBox.IndexFromPoint(e.Location);
        if (index >= 0)
        {
            _videoListBox.SelectedIndex = index;
        }
    }

    private void DeleteSelectedVideo()
    {
        var entry = CurrentEntry;
        if (entry == null)
        {
            MessageBox.Show("Select a video first.");
            return;
        }

        var answer = MessageBox.Show(
            $"Delete '{entry.VideoName}' from storage? This only deletes files in the storage folder.",
            "Delete Video",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes)
        {
            return;
        }

        try
        {
            ClearPreview();
            _service.DeleteVideo(entry);
            _entries = _entries.Where(x => !x.FolderPath.Equals(entry.FolderPath, StringComparison.OrdinalIgnoreCase)).ToList();
            RebindEntries();
            LoadSelectedVideo();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Delete failed: {ex.Message}");
        }
    }

    private void RenameSelectedVideo()
    {
        var entry = CurrentEntry;
        if (entry == null)
        {
            System.Windows.Forms.MessageBox.Show("Select a video first.");
            return;
        }

        using var renameForm = new Form
        {
            Text = "Rename Video",
            Width = 400,
            Height = 150,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false
        };

        var nameTextBox = new TextBox { Left = 15, Top = 15, Width = 350, Text = entry.VideoName };
        var okButton = new Button { Text = "OK", Left = 210, Top = 50, Width = 75, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", Left = 290, Top = 50, Width = 75, DialogResult = DialogResult.Cancel };

        renameForm.Controls.Add(nameTextBox);
        renameForm.Controls.Add(okButton);
        renameForm.Controls.Add(cancelButton);
        renameForm.AcceptButton = okButton;
        renameForm.CancelButton = cancelButton;

        if (renameForm.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            ClearPreview();
            _mediaElement.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            System.Windows.Forms.Application.DoEvents();

            _service.RenameVideo(entry, nameTextBox.Text);
            _entries = _entries.OrderBy(x => x.VideoName).ToList();
            RebindEntries();

            var renamedEntry = _entries.FirstOrDefault(x => x.FolderPath.Equals(entry.FolderPath, StringComparison.OrdinalIgnoreCase));
            _videoListBox.SelectedItem = renamedEntry;

            if (renamedEntry != null)
            {
                LoadSelectedVideo();
            }
        }
        catch (IOException ex)
        {
            System.Windows.Forms.MessageBox.Show($"Rename failed: {ex.Message} Close preview and retry.");
        }
        catch (Exception ex)
        {
            System.Windows.Forms.MessageBox.Show($"Rename failed: {ex.Message}");
        }
    }

    private void MediaElementOnMediaOpened(object? sender, RoutedEventArgs e)
    {
        if (!_showThumbnailOnOpen)
        {
            return;
        }

        _showThumbnailOnOpen = false;
        _mediaElement.Position = TimeSpan.FromSeconds(0.2);
        _mediaElement.Pause();
    }

    private void BrowseFolder(TextBox destination)
    {
        using var dialog = new FolderBrowserDialog();
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            destination.Text = dialog.SelectedPath;
        }
    }

    private void LoadStorage()
    {
        if (string.IsNullOrWhiteSpace(_storageFolderTextBox.Text))
        {
            System.Windows.Forms.MessageBox.Show("Select a storage folder first.");
            return;
        }

        _entries = _service.LoadFromStorage(_storageFolderTextBox.Text);
        var commonTags = _service.LoadCommonHashtags(_storageFolderTextBox.Text);
        _commonHashtagsListBox.DataSource = commonTags;
        RebindEntries();
    }

    private void ImportVideos()
    {
        if (string.IsNullOrWhiteSpace(_sourceFolderTextBox.Text) || string.IsNullOrWhiteSpace(_storageFolderTextBox.Text))
        {
            System.Windows.Forms.MessageBox.Show("Choose source and storage folders first.");
            return;
        }

        try
        {
            var imported = _service.ImportVideos(_sourceFolderTextBox.Text, _storageFolderTextBox.Text);
            var existingByFolder = _entries.ToDictionary(x => x.FolderPath, StringComparer.OrdinalIgnoreCase);
            foreach (var item in imported)
            {
                existingByFolder[item.FolderPath] = item;
            }

            _entries = existingByFolder.Values.OrderBy(x => x.VideoName).ToList();
            RebindEntries();
            System.Windows.Forms.MessageBox.Show($"Imported {imported.Count} videos.");
        }
        catch (Exception ex)
        {
            System.Windows.Forms.MessageBox.Show($"Import failed: {ex.Message}");
        }
    }

    private void RebindEntries()
    {
        _entriesBinding.DataSource = null;
        _entriesBinding.DataSource = _entries;
    }

    private VideoEntry? CurrentEntry => _videoListBox.SelectedItem as VideoEntry;

    private void LoadSelectedVideo()
    {
        var entry = CurrentEntry;
        if (entry == null)
        {
            _descriptionSelector.DataSource = null;
            _descriptionEditor.Text = string.Empty;
            _performanceNormalRadio.Checked = true;
            _tagsTextBox.Text = string.Empty;
            _lastPostDatePicker.Checked = false;
            ClearPreview();
            return;
        }

        _descriptionSelector.DataSource = null;
        _descriptionSelector.DataSource = entry.DescriptionFiles.OrderBy(x => x).ToList();
        SetPerformance(entry.PerformanceLevel);
        _tagsTextBox.Text = string.Join(", ", entry.Tags);

        if (entry.LastPostDate.HasValue)
        {
            _lastPostDatePicker.Value = entry.LastPostDate.Value;
            _lastPostDatePicker.Checked = true;
        }
        else
        {
            _lastPostDatePicker.Checked = false;
        }

        LoadVideoPreview(entry.VideoPath);
        LoadSelectedDescription();
    }

    private void LoadVideoPreview(string videoPath)
    {
        if (!File.Exists(videoPath))
        {
            ClearPreview();
            _previewStatusLabel.Text = "Video file not found in storage.";
            return;
        }

        try
        {
            _showThumbnailOnOpen = true;
            _mediaElement.Stop();
            _mediaElement.Source = new Uri(videoPath);
            _mediaElement.Play();
            _previewStatusLabel.Text = $"Loaded: {Path.GetFileName(videoPath)}";
        }
        catch (Exception ex)
        {
            ClearPreview();
            _previewStatusLabel.Text = $"Unable to preview video: {ex.Message}";
        }
    }

    private void ClearPreview()
    {
        _showThumbnailOnOpen = false;
        _mediaElement.Stop();
        _mediaElement.Source = null;
        _previewStatusLabel.Text = "Select a video to preview.";
    }

    private void LoadSelectedDescription()
    {
        var entry = CurrentEntry;
        var selectedDescription = _descriptionSelector.SelectedItem as string;

        if (entry == null || string.IsNullOrWhiteSpace(selectedDescription))
        {
            _descriptionEditor.Text = string.Empty;
            return;
        }

        _descriptionEditor.Text = _service.LoadDescription(entry, selectedDescription);
    }

    private void AddDescription()
    {
        var entry = CurrentEntry;
        if (entry == null)
        {
            System.Windows.Forms.MessageBox.Show("Select a video first.");
            return;
        }

        var newDescription = _service.AddDescription(entry);
        _descriptionSelector.DataSource = null;
        _descriptionSelector.DataSource = entry.DescriptionFiles.OrderBy(x => x).ToList();
        _descriptionSelector.SelectedItem = newDescription;
    }

    private void AddCommonHashtag()
    {
        if (string.IsNullOrWhiteSpace(_storageFolderTextBox.Text))
        {
            MessageBox.Show("Load a storage folder first.");
            return;
        }

        var tag = NormalizeHashtag(_commonHashtagInput.Text);
        if (string.IsNullOrWhiteSpace(tag))
        {
            MessageBox.Show("Enter a hashtag value.");
            return;
        }

        var existing = (_commonHashtagsListBox.DataSource as List<string>) ?? new List<string>();
        if (!existing.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            existing.Add(tag);
        }

        var updated = existing.OrderBy(x => x).ToList();
        _commonHashtagsListBox.DataSource = updated;
        _service.SaveCommonHashtags(_storageFolderTextBox.Text, updated);
        _commonHashtagInput.Text = string.Empty;
    }

    private void RemoveSelectedCommonHashtags()
    {
        if (string.IsNullOrWhiteSpace(_storageFolderTextBox.Text))
        {
            MessageBox.Show("Load a storage folder first.");
            return;
        }

        var existing = (_commonHashtagsListBox.DataSource as List<string>) ?? new List<string>();
        var selected = _commonHashtagsListBox.SelectedItems.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0)
        {
            return;
        }

        var updated = existing.Where(x => !selected.Contains(x)).OrderBy(x => x).ToList();
        _commonHashtagsListBox.DataSource = updated;
        _service.SaveCommonHashtags(_storageFolderTextBox.Text, updated);
    }

    private void AppendSelectedCommonHashtagsToDescription()
    {
        var selected = _commonHashtagsListBox.SelectedItems.Cast<string>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select one or more common hashtags first.");
            return;
        }

        var existingWords = _descriptionEditor.Text
            .Split(new[] { ' ', '\r', '\n', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAppend = selected
            .Select(NormalizeHashtag)
            .Where(x => !string.IsNullOrWhiteSpace(x) && !existingWords.Contains(x))
            .ToList();

        if (toAppend.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_descriptionEditor.Text) && !_descriptionEditor.Text.EndsWith(' '))
        {
            _descriptionEditor.AppendText(" ");
        }

        _descriptionEditor.AppendText(string.Join(" ", toAppend));
    }

    private void SaveCurrentVideo()
    {
        var entry = CurrentEntry;
        var selectedDescription = _descriptionSelector.SelectedItem as string;

        if (entry == null)
        {
            System.Windows.Forms.MessageBox.Show("Select a video first.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(selectedDescription))
        {
            _service.SaveDescription(entry, selectedDescription, _descriptionEditor.Text);
        }

        entry.PerformanceLevel = GetPerformance();
        entry.Tags = _tagsTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        entry.LastPostDate = _lastPostDatePicker.Checked ? _lastPostDatePicker.Value.Date : null;

        _service.SaveMetadata(entry);
        RebindEntries();
        System.Windows.Forms.MessageBox.Show("Saved.");
    }

    private string GetPerformance()
    {
        if (_performanceLowRadio.Checked)
        {
            return "Low";
        }

        if (_performanceHighRadio.Checked)
        {
            return "High";
        }

        return "Normal";
    }

    private void SetPerformance(string? value)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "low":
                _performanceLowRadio.Checked = true;
                break;
            case "high":
                _performanceHighRadio.Checked = true;
                break;
            default:
                _performanceNormalRadio.Checked = true;
                break;
        }
    }

    private static string NormalizeHashtag(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return trimmed.StartsWith('#') ? trimmed : $"#{trimmed}";
    }
}
