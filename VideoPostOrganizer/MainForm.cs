using VideoPostOrganizer.Models;
using VideoPostOrganizer.Services;

namespace VideoPostOrganizer;

public class MainForm : Form
{
    private readonly VideoLibraryService _service = new();

    private readonly TextBox _storageFolderTextBox = new() { Width = 560 };
    private readonly TextBox _sourceFolderTextBox = new() { Width = 560 };
    private readonly ListBox _videoListBox = new() { Width = 360, Height = 420 };
    private readonly ComboBox _descriptionSelector = new() { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _descriptionEditor = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Width = 520, Height = 250 };
    private readonly TextBox _categoryTextBox = new() { Width = 250 };
    private readonly TextBox _performanceTextBox = new() { Width = 250 };
    private readonly TextBox _tagsTextBox = new() { Width = 520 };
    private readonly DateTimePicker _lastPostDatePicker = new() { Width = 200, Format = DateTimePickerFormat.Short, ShowCheckBox = true };

    private readonly BindingSource _entriesBinding = new();
    private List<VideoEntry> _entries = new();

    public MainForm()
    {
        Text = "Social Media Video Organizer";
        Width = 980;
        Height = 720;
        StartPosition = FormStartPosition.CenterScreen;

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

        _videoListBox.DisplayMember = nameof(VideoEntry.DisplayName);
        _videoListBox.DataSource = _entriesBinding;
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
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(8)
        };

        leftPanel.Controls.Add(new Label { Text = "Videos", AutoSize = true });
        leftPanel.Controls.Add(_videoListBox);

        var rightPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(8)
        };

        var descriptionRow = new FlowLayoutPanel { Width = 700, Height = 38 };
        descriptionRow.Controls.Add(new Label { Text = "Description file:", Width = 100, TextAlign = ContentAlignment.MiddleLeft });
        descriptionRow.Controls.Add(_descriptionSelector);
        descriptionRow.Controls.Add(addDescriptionButton);

        var categoryRow = new FlowLayoutPanel { Width = 700, Height = 38 };
        categoryRow.Controls.Add(new Label { Text = "Category:", Width = 100, TextAlign = ContentAlignment.MiddleLeft });
        categoryRow.Controls.Add(_categoryTextBox);
        categoryRow.Controls.Add(new Label { Text = "Performance:", Width = 90, TextAlign = ContentAlignment.MiddleLeft });
        categoryRow.Controls.Add(_performanceTextBox);

        var dateRow = new FlowLayoutPanel { Width = 700, Height = 38 };
        dateRow.Controls.Add(new Label { Text = "Last post date:", Width = 100, TextAlign = ContentAlignment.MiddleLeft });
        dateRow.Controls.Add(_lastPostDatePicker);

        rightPanel.Controls.Add(descriptionRow);
        rightPanel.Controls.Add(new Label { Text = "Description text", AutoSize = true });
        rightPanel.Controls.Add(_descriptionEditor);
        rightPanel.Controls.Add(categoryRow);
        rightPanel.Controls.Add(new Label { Text = "Tags (comma-separated)", AutoSize = true });
        rightPanel.Controls.Add(_tagsTextBox);
        rightPanel.Controls.Add(dateRow);
        rightPanel.Controls.Add(saveButton);

        Controls.Add(rightPanel);
        Controls.Add(leftPanel);
        Controls.Add(topPanel);
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
            MessageBox.Show("Select a storage folder first.");
            return;
        }

        _entries = _service.LoadFromStorage(_storageFolderTextBox.Text);
        RebindEntries();
    }

    private void ImportVideos()
    {
        if (string.IsNullOrWhiteSpace(_sourceFolderTextBox.Text) || string.IsNullOrWhiteSpace(_storageFolderTextBox.Text))
        {
            MessageBox.Show("Choose source and storage folders first.");
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
            MessageBox.Show($"Imported {imported.Count} videos.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Import failed: {ex.Message}");
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
            _categoryTextBox.Text = string.Empty;
            _performanceTextBox.Text = string.Empty;
            _tagsTextBox.Text = string.Empty;
            _lastPostDatePicker.Checked = false;
            return;
        }

        _descriptionSelector.DataSource = null;
        _descriptionSelector.DataSource = entry.DescriptionFiles.OrderBy(x => x).ToList();
        _categoryTextBox.Text = entry.Category;
        _performanceTextBox.Text = entry.PerformanceNotes;
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

        LoadSelectedDescription();
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
            MessageBox.Show("Select a video first.");
            return;
        }

        var newDescription = _service.AddDescription(entry);
        _descriptionSelector.DataSource = null;
        _descriptionSelector.DataSource = entry.DescriptionFiles.OrderBy(x => x).ToList();
        _descriptionSelector.SelectedItem = newDescription;
    }

    private void SaveCurrentVideo()
    {
        var entry = CurrentEntry;
        var selectedDescription = _descriptionSelector.SelectedItem as string;

        if (entry == null)
        {
            MessageBox.Show("Select a video first.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(selectedDescription))
        {
            _service.SaveDescription(entry, selectedDescription, _descriptionEditor.Text);
        }

        entry.Category = _categoryTextBox.Text.Trim();
        entry.PerformanceNotes = _performanceTextBox.Text.Trim();
        entry.Tags = _tagsTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        entry.LastPostDate = _lastPostDatePicker.Checked ? _lastPostDatePicker.Value.Date : null;

        _service.SaveMetadata(entry);
        RebindEntries();
        MessageBox.Show("Saved.");
    }
}
