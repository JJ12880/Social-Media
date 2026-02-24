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
    private readonly DataGridView _videoGrid = new()
    {
        Width = 360,
        Height = 420,
        ReadOnly = true,
        AutoGenerateColumns = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false
    };
    private readonly ComboBox _descriptionSelector = new() { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _descriptionEditor = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Width = 520, Height = 170 };
    private readonly TextBox _tagsTextBox = new() { Width = 280 };
    private readonly ListBox _commonHashtagsListBox = new() { Width = 390, Height = 90, SelectionMode = SelectionMode.MultiExtended };
    private readonly TextBox _commonHashtagInput = new() { Width = 190 };
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
    private readonly List<string> _storageFolders = new();
    private readonly DataGridView _bulkUploadGrid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AutoGenerateColumns = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false
    };
    private bool _showThumbnailOnOpen;
    private bool _isUpdatingUi;
    private string _sortColumn = nameof(VideoEntry.VideoName);
    private bool _sortAscending = true;

    public MainForm()
    {
        Text = "Social Media Video Organizer";
        Width = 980;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;

        _videoPreviewHost.Child = _mediaElement;
        _mediaElement.MediaOpened += MediaElementOnMediaOpened;

        var browseStorageButton = new Button { Text = "Add Storage..." };
        browseStorageButton.Click += (_, _) => AddStorageFolder();

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

        _videoGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "VideoNameColumn",
            DataPropertyName = nameof(VideoEntry.VideoName),
            HeaderText = "Video",
            SortMode = DataGridViewColumnSortMode.Programmatic,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _videoGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "PerformanceColumn",
            DataPropertyName = nameof(VideoEntry.PerformanceLevel),
            HeaderText = "Performance",
            Width = 110,
            SortMode = DataGridViewColumnSortMode.Programmatic
        });
        _videoGrid.DataSource = _entriesBinding;
        _videoGrid.ContextMenuStrip = videoMenu;
        _videoGrid.CellMouseDown += VideoGridOnCellMouseDown;
        _videoGrid.SelectionChanged += (_, _) => LoadSelectedVideo();
        _videoGrid.ColumnHeaderMouseClick += VideoGridOnColumnHeaderMouseClick;

        _bulkUploadGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(VideoEntry.VideoName),
            HeaderText = "Video",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _bulkUploadGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(VideoEntry.PerformanceLevel),
            HeaderText = "Performance",
            Width = 110
        });
        _bulkUploadGrid.DataSource = _entriesBinding;

        _performanceLowRadio.CheckedChanged += PerformanceRadioOnCheckedChanged;
        _performanceNormalRadio.CheckedChanged += PerformanceRadioOnCheckedChanged;
        _performanceHighRadio.CheckedChanged += PerformanceRadioOnCheckedChanged;
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

        topPanel.Controls.Add(new Label { Text = "Storage folders:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
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
        leftPanel.Controls.Add(_videoGrid);

        var previewControlRow = new FlowLayoutPanel { Width = 960, Height = 38 };
        previewControlRow.Controls.Add(new Label { Text = "Preview:", Width = 100, TextAlign = ContentAlignment.MiddleLeft });
        previewControlRow.Controls.Add(playButton);
        previewControlRow.Controls.Add(pauseButton);
        previewControlRow.Controls.Add(stopButton);

        var descriptionRow = new FlowLayoutPanel { Width = 960, Height = 38 };
        descriptionRow.Controls.Add(new Label { Text = "Description file:", Width = 100, TextAlign = ContentAlignment.MiddleLeft });
        descriptionRow.Controls.Add(_descriptionSelector);
        descriptionRow.Controls.Add(addDescriptionButton);

        var performanceRow = new FlowLayoutPanel { Width = 400, Height = 38 };
        performanceRow.Controls.Add(new Label { Text = "Performance:", Width = 100, TextAlign = ContentAlignment.MiddleLeft });
        performanceRow.Controls.Add(_performanceLowRadio);
        performanceRow.Controls.Add(_performanceNormalRadio);
        performanceRow.Controls.Add(_performanceHighRadio);

        var hashtagInputRow = new FlowLayoutPanel { Width = 400, Height = 38 };
        hashtagInputRow.Controls.Add(new Label { Text = "Hashtag:", Width = 100, TextAlign = ContentAlignment.MiddleLeft });
        hashtagInputRow.Controls.Add(_commonHashtagInput);
        hashtagInputRow.Controls.Add(addCommonHashtagButton);

        var hashtagActionsRow = new FlowLayoutPanel { Width = 400, Height = 38 };
        hashtagActionsRow.Controls.Add(removeCommonHashtagButton);
        hashtagActionsRow.Controls.Add(appendCommonHashtagButton);

        var tagsRow = new FlowLayoutPanel { Width = 400, Height = 64 };
        tagsRow.Controls.Add(new Label { Text = "Tags:", Width = 100, TextAlign = ContentAlignment.MiddleLeft });
        tagsRow.Controls.Add(_tagsTextBox);

        var previewAndMetadataRow = new FlowLayoutPanel
        {
            Width = 960,
            Height = 330,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        var metadataPanel = new FlowLayoutPanel
        {
            Width = 420,
            Height = 320,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(8)
        };

        metadataPanel.Controls.Add(performanceRow);
        metadataPanel.Controls.Add(tagsRow);
        metadataPanel.Controls.Add(new Label { Text = "Common Hashtags", AutoSize = true });
        metadataPanel.Controls.Add(hashtagInputRow);
        metadataPanel.Controls.Add(_commonHashtagsListBox);
        metadataPanel.Controls.Add(hashtagActionsRow);

        previewAndMetadataRow.Controls.Add(_videoPreviewHost);
        previewAndMetadataRow.Controls.Add(metadataPanel);

        var dateRow = new FlowLayoutPanel { Width = 960, Height = 38 };
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
        rightPanel.Controls.Add(previewAndMetadataRow);
        rightPanel.Controls.Add(descriptionRow);
        rightPanel.Controls.Add(new Label { Text = "Description text", AutoSize = true });
        rightPanel.Controls.Add(_descriptionEditor);
        rightPanel.Controls.Add(dateRow);
        rightPanel.Controls.Add(saveButton);

        var organizerTab = new TabPage("Organizer");
        organizerTab.Controls.Add(rightPanel);
        organizerTab.Controls.Add(leftPanel);
        organizerTab.Controls.Add(topPanel);

        var bulkTab = new TabPage("Bulk Upload CSV");
        bulkTab.Padding = new Padding(8);
        bulkTab.Controls.Add(_bulkUploadGrid);

        var tabControl = new TabControl
        {
            Dock = DockStyle.Fill
        };

        tabControl.TabPages.Add(organizerTab);
        tabControl.TabPages.Add(bulkTab);

        Controls.Add(tabControl);
    }

    private void VideoGridOnCellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || e.RowIndex < 0)
        {
            return;
        }

        _videoGrid.ClearSelection();
        _videoGrid.Rows[e.RowIndex].Selected = true;
        _videoGrid.CurrentCell = _videoGrid.Rows[e.RowIndex].Cells[0];
    }

    private void VideoGridOnColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex < 0)
        {
            System.Windows.Forms.MessageBox.Show("Select a video first.");
            return;
        }

        var column = _videoGrid.Columns[e.ColumnIndex];
        var dataProperty = column.DataPropertyName;
        if (string.IsNullOrWhiteSpace(dataProperty))
        {
            return;
        }

        if (string.Equals(_sortColumn, dataProperty, StringComparison.Ordinal))
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = dataProperty;
            _sortAscending = true;
        }

        RebindEntries();
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
            var currentIndex = _videoGrid.CurrentRow?.Index ?? -1;
            ClearPreview();
            _service.DeleteVideo(entry);
            _entries = _entries.Where(x => !x.FolderPath.Equals(entry.FolderPath, StringComparison.OrdinalIgnoreCase)).ToList();
            RebindEntries();

            if (_videoGrid.Rows.Count > 0)
            {
                var nextIndex = Math.Min(Math.Max(currentIndex, 0), _videoGrid.Rows.Count - 1);
                _videoGrid.ClearSelection();
                _videoGrid.Rows[nextIndex].Selected = true;
                _videoGrid.CurrentCell = _videoGrid.Rows[nextIndex].Cells[0];
            }
            else
            {
                LoadSelectedVideo();
            }
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
            RebindEntries();

            var renamedEntry = _entries.FirstOrDefault(x => x.FolderPath.Equals(entry.FolderPath, StringComparison.OrdinalIgnoreCase));
            if (renamedEntry != null)
            {
                SelectEntry(renamedEntry);
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

    private void AddStorageFolder()
    {
        using var dialog = new FolderBrowserDialog();
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var selected = dialog.SelectedPath;
        if (_storageFolders.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _storageFolders.Add(selected);
        _storageFolderTextBox.Text = string.Join("; ", _storageFolders);
    }

    private List<string> GetStorageFoldersFromInput()
    {
        var fromInput = _storageFolderTextBox.Text
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return fromInput;
    }

    private string? PrimaryStorageFolder => _storageFolders.FirstOrDefault();

    private void LoadStorage()
    {
        _storageFolders.Clear();
        _storageFolders.AddRange(GetStorageFoldersFromInput());

        if (_storageFolders.Count == 0)
        {
            MessageBox.Show("Select one or more storage folders first.");
            return;
        }

        _entries = _storageFolders
            .SelectMany(_service.LoadFromStorage)
            .GroupBy(x => x.FolderPath, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        var commonTags = _service.LoadCommonHashtags(PrimaryStorageFolder!);
        _commonHashtagsListBox.DataSource = commonTags;
        RebindEntries();
    }

    private void ImportVideos()
    {
        var storageFolder = PrimaryStorageFolder;
        if (string.IsNullOrWhiteSpace(_sourceFolderTextBox.Text) || string.IsNullOrWhiteSpace(storageFolder))
        {
            MessageBox.Show("Choose source folder and load at least one storage folder first.");
            return;
        }

        try
        {
            var importResult = _service.ImportVideos(_sourceFolderTextBox.Text, storageFolder);
            var imported = importResult.ImportedEntries;
            var existingByFolder = _entries.ToDictionary(x => x.FolderPath, StringComparer.OrdinalIgnoreCase);
            foreach (var item in imported)
            {
                existingByFolder[item.FolderPath] = item;
            }

            _entries = existingByFolder.Values.OrderBy(x => x.VideoName).ToList();
            RebindEntries();
            MessageBox.Show($"Imported {imported.Count} videos. Skipped duplicates: {importResult.DuplicateCount}.");
        }
        catch (Exception ex)
        {
            System.Windows.Forms.MessageBox.Show($"Import failed: {ex.Message}");
        }
    }

    private void RebindEntries()
    {
        _entries = SortEntries(_entries).ToList();
        _entriesBinding.DataSource = null;
        _entriesBinding.DataSource = _entries;
    }

    private IEnumerable<VideoEntry> SortEntries(IEnumerable<VideoEntry> source)
    {
        return (_sortColumn, _sortAscending) switch
        {
            (nameof(VideoEntry.PerformanceLevel), true) => source.OrderBy(x => x.PerformanceLevel).ThenBy(x => x.VideoName),
            (nameof(VideoEntry.PerformanceLevel), false) => source.OrderByDescending(x => x.PerformanceLevel).ThenBy(x => x.VideoName),
            (_, true) => source.OrderBy(x => x.VideoName),
            _ => source.OrderByDescending(x => x.VideoName)
        };
    }

    private void SelectEntry(VideoEntry entry)
    {
        for (var i = 0; i < _videoGrid.Rows.Count; i++)
        {
            if (_videoGrid.Rows[i].DataBoundItem is not VideoEntry rowEntry)
            {
                continue;
            }

            if (!rowEntry.FolderPath.Equals(entry.FolderPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _videoGrid.ClearSelection();
            _videoGrid.Rows[i].Selected = true;
            _videoGrid.CurrentCell = _videoGrid.Rows[i].Cells[0];
            return;
        }
    }

    private VideoEntry? CurrentEntry => _videoGrid.CurrentRow?.DataBoundItem as VideoEntry;

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
        var storageFolder = PrimaryStorageFolder;
        if (string.IsNullOrWhiteSpace(storageFolder))
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
        _service.SaveCommonHashtags(storageFolder, updated);
        _commonHashtagInput.Text = string.Empty;
    }

    private void RemoveSelectedCommonHashtags()
    {
        var storageFolder = PrimaryStorageFolder;
        if (string.IsNullOrWhiteSpace(storageFolder))
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
        _service.SaveCommonHashtags(storageFolder, updated);
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
        _isUpdatingUi = true;
        try
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
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void PerformanceRadioOnCheckedChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingUi || sender is not RadioButton radio || !radio.Checked)
        {
            return;
        }

        var entry = CurrentEntry;
        if (entry == null)
        {
            return;
        }

        var newPerformance = GetPerformance();
        if (string.Equals(entry.PerformanceLevel, newPerformance, StringComparison.Ordinal))
        {
            return;
        }

        entry.PerformanceLevel = newPerformance;
        _service.SaveMetadata(entry);
        RebindEntries();
        SelectEntry(entry);
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
        _isUpdatingUi = true;
        try
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
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void PerformanceRadioOnCheckedChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingUi || sender is not RadioButton radio || !radio.Checked)
        {
            return;
        }

        var entry = CurrentEntry;
        if (entry == null)
        {
            return;
        }

        var newPerformance = GetPerformance();
        if (string.Equals(entry.PerformanceLevel, newPerformance, StringComparison.Ordinal))
        {
            return;
        }

        entry.PerformanceLevel = newPerformance;
        _service.SaveMetadata(entry);
        RebindEntries();
        SelectEntry(entry);
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
