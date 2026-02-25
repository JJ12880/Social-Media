# Social Media Video Organizer (Avalonia)

This project provides a cross-platform desktop GUI application to organize video files for social media posting workflows.

## What it does

- Lets you choose one or more **storage folders** and a **source folder** of videos.
- Copies each video into its own folder in storage (named from the video filename).
- Creates at least one description file (`description-1.txt`) per video folder.
- Supports creating additional description files from the GUI.
- Lets you rename or delete stored videos.
- Lets you edit and save descriptions.
- Tracks metadata per video:
  - Category/performance level
  - Tags
  - Last post date
- Stores metadata in `metadata.json` in each video folder.

> Note: the Avalonia migration keeps video management and metadata workflows cross-platform. In-app embedded playback was replaced by an external player launch button for portability.

## Project structure

- `VideoPostOrganizer/` - Avalonia application source.
- `VideoPostOrganizer/Models/VideoEntry.cs` - core metadata model.
- `VideoPostOrganizer/Services/VideoLibraryService.cs` - import/copy/metadata/description file operations.
- `VideoPostOrganizer/MainWindow.axaml` - GUI layout.
- `VideoPostOrganizer/MainWindow.axaml.cs` - GUI behavior.

## Build and run

```bash
dotnet restore VideoPostOrganizer/VideoPostOrganizer.csproj --packages $HOME/.nuget/offline-cache
dotnet build VideoPostOrganizer/VideoPostOrganizer.csproj --no-restore -c Release
dotnet run --project VideoPostOrganizer/VideoPostOrganizer.csproj --no-build
```
