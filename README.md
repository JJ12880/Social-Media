# Social Media Video Organizer (C# WinForms)

This project provides a desktop GUI application to organize video files for social media posting workflows.

## What it does

- Lets you choose a **source folder** of videos and a **storage folder**.
- Copies each video into its own folder in storage (named from the video filename).
- Creates at least one description file (`description-1.txt`) per video folder.
- Supports creating additional description files from the GUI.
- Shows an in-app video preview with thumbnail-style first frame and Play/Pause/Stop controls.
- Lets you edit and save descriptions.
- Tracks metadata per video:
  - Category
  - Performance notes
  - Tags
  - Last post date
- Stores metadata in `metadata.json` in each video folder.

> CSV generation UI is intentionally deferred and can be added later using the metadata model already included.

## Project structure

- `VideoPostOrganizer/` - WinForms application source.
- `VideoPostOrganizer/Models/VideoEntry.cs` - core metadata model.
- `VideoPostOrganizer/Services/VideoLibraryService.cs` - import/copy/metadata/description file operations.
- `VideoPostOrganizer/MainForm.cs` - GUI for importing and editing videos.

## Build and run

Requires .NET 8 SDK on Windows (WinForms target):

```bash
dotnet run --project VideoPostOrganizer/VideoPostOrganizer.csproj
```

## Notes for future CSV module

All fields needed for bulk upload prep are available in `VideoEntry` and `metadata.json`:
- video file path/name
- multiple description files
- tags/category/performance notes
- last post date

A future feature can read all `metadata.json` files from storage and export a CSV view.
