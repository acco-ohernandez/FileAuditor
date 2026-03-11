# FileAuditor

A Windows desktop and CLI tool for auditing file system paths — counting files and folders, analyzing storage by file type, detecting stale content, and safely cleaning up files based on configurable date and type filters. Built with native Box Drive integration for enterprise environments.

---

## Projects

The solution contains three projects:

| Project | Type | Target |
|---|---|---|
| `FileAuditor.Core` | Class library | net8.0 |
| `FileAuditor.WPF` | WPF desktop app | net8.0-windows |
| `FileAuditor.CLI` | Console application | net8.0 |

---

## Requirements

- Windows 10 or Windows 11
- .NET 8 SDK (build) or .NET 8 Runtime (run)
- Visual Studio 2022 or later (optional, for development)

---

## Building

Open `FileAuditor.sln` in Visual Studio 2022 and build the solution, or use the CLI:

```bash
dotnet build FileAuditor.sln
```

To publish a self-contained WPF executable:

```bash
dotnet publish FileAuditor.WPF/FileAuditor.WPF.csproj -c Release -r win-x64 --self-contained
```

---

## Features

### File Counter (Scan)

- Scans one or more local, UNC network, or Box Drive paths simultaneously
- Counts files, folders, or both (configurable)
- Recursive scanning with optional maximum depth limit
- Configurable parallel thread count (default: 4)
- Optional inclusion of hidden and system files
- File type filtering — include-only or exclude lists by extension
- Per-extension file type breakdown (e.g. `.docx`, `.pdf`, `.xlsx` counts)
- Total size calculation per scanned path
- Real-time progress reporting and graceful cancellation
- Error collection (access denied, path not found, network offline) without aborting the full scan
- Sortable results DataGrid with status color-coding
- Export results to CSV or JSON
- Import path lists from CSV
- Scan history with configurable retention and delta comparison between runs

### Box Drive Integration

- Detects Box Drive installation path from the Windows Registry (`HKCU\Software\Box\Box\preferences`)
- Falls back to common mount points (`C:\CORP BOX`, `B:\`)
- Detects cloud-only (offline/stub) files using Win32 `GetFileAttributes` — checks `FILE_ATTRIBUTE_OFFLINE` and `FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS`
- Triggers file and directory hydration (download from cloud) before scanning for accurate counts
- Configurable hydration timeout
- Registry path cache expires after 5 minutes — Box Drive installed mid-session is detected automatically without an app restart

### Cleanup

Two-phase workflow: **Analyze** (dry-run preview) then **Execute**.

**Date filter modes:**

| Mode | Behavior |
|---|---|
| AnyDate | No date filter — all matching files |
| OlderThan | Last-modified before a specified date/time |
| NewerThan | Last-modified after a specified date/time |
| DateRange | Last-modified between two dates (optional time precision) |
| ExactDate | Last-modified on a specific date (optional time) |

**Operation modes:**

| Mode | Behavior |
|---|---|
| Delete | Remove items (to Recycle Bin or permanently) |
| Move to Folder | Relocate items to a per-path destination, preserving relative structure |

**Paths to Clean format:**
- Delete mode: one source path per line — `C:\Source`
- Move to Folder mode: source and destination separated by a comma — `C:\Source,D:\Archive`

**Date/time quick-set:**
- Every date picker has a **📅 Today** button that sets both the date and its associated time fields to the current moment
- All date/time fields default to the current date and time when the application starts

**Additional options:**
- Target files only, folders only, or both
- File type include/exclude filters (case-insensitive, dot-optional — `txt`, `.txt`, and `.TXT` all match)
- Regex-based exclusion patterns
- Move to Recycle Bin vs. permanent delete (Delete mode only)
- Confirmation dialog before execution
- Auto-export of cleanup results to a timestamped CSV in the Logs folder

### CLI Automation

The CLI is designed for use with Windows Task Scheduler for unattended, scheduled audits.

```
FileAuditor.CLI scan    --config <config.json> [--output <path>] [--verbose]
FileAuditor.CLI cleanup --config <config.json> [--dry-run | --execute] [--verbose] [--move <dest>]
FileAuditor.CLI help    [scan | cleanup]
FileAuditor.CLI version
```

`--move <dest>` switches the cleanup command to Move to Folder mode, sending all matched items to `<dest>` while preserving relative path structure. For per-path destinations, set `DestinationPath` on each `PathItem` in the JSON config directly.

Configurations saved from the WPF application are interchangeable with the CLI.

---

## Configuration Files

Configurations are JSON files that can be created manually or saved from the WPF UI.

### Scan configuration example (`sample-config.json`)

```json
{
  "paths": [
    { "path": "C:\\Users\\YourUsername\\Documents", "isBoxDrivePath": false, "isNetworkPath": false },
    { "path": "C:\\CORP BOX\\Box", "isBoxDrivePath": true, "isNetworkPath": false }
  ],
  "countMode": "Both",
  "isRecursive": true,
  "maxDepth": 1,
  "parallelThreadCount": 4,
  "includeHiddenFiles": false,
  "boxDriveRefreshEnabled": true,
  "boxDriveRefreshTimeoutSeconds": 30,
  "enableFileTypeBreakdown": true,
  "includeFileTypes": [ ".docx", ".xlsx", ".pdf" ],
  "excludeFileTypes": [ ".tmp", ".log" ],
  "name": "Weekly Audit Configuration",
  "outputFormat": "csv",
  "outputPath": "C:\\Reports\\scan_results_{timestamp}.csv",
  "enableHistory": true,
  "enableComparison": true,
  "comparisonRetentionDays": 90
}
```

---

## Runtime Data

All runtime data is stored under `%LocalAppData%\FileAuditor\`:

| Folder | Contents |
|---|---|
| `History\` | JSON files of past scan results |
| `Configurations\` | Saved scan configurations |
| `CleanupConfigurations\` | Saved cleanup configurations |
| `Logs\` | Rolling Serilog log files (WPF and CLI) |

---

## NuGet Dependencies

### FileAuditor.Core
- `CsvHelper` 33.1.0
- `Newtonsoft.Json` 13.0.4
- `Serilog` 4.3.0
- `Serilog.Extensions.Logging` 10.0.0
- `Serilog.Sinks.File` 7.0.0
- `Microsoft.Extensions.Logging.Abstractions` 10.0.2

### FileAuditor.WPF
- `CommunityToolkit.Mvvm` 8.4.0
- `Microsoft.Extensions.DependencyInjection` 10.0.2
- `Microsoft.Extensions.Logging` 10.0.2
- `Microsoft.VisualBasic` 10.3.0
- `Microsoft.Xaml.Behaviors.Wpf` 1.1.135
- `Serilog.Extensions.Logging` 10.0.0
- `Serilog.Sinks.Debug` 3.0.0
- `Serilog.Sinks.File` 7.0.0

### FileAuditor.CLI
- `System.CommandLine` 2.0.2
- `Microsoft.Extensions.Logging` 10.0.2
- `Serilog` 4.3.0
- `Serilog.Extensions.Logging` 10.0.0
- `Serilog.Sinks.Console` 6.1.1
- `Serilog.Sinks.File` 7.0.0

---

## Architecture

The solution follows a clean separation of concerns:

```
FileAuditor.Core        → domain models, enums, services, helpers (no UI dependency)
FileAuditor.WPF         → MVVM presentation layer (CommunityToolkit.Mvvm)
FileAuditor.CLI         → thin console host over the same Core services
```

The WPF application uses Microsoft.Extensions.DependencyInjection with constructor injection throughout. ViewModels use `[ObservableProperty]` and `[RelayCommand]` source generators from CommunityToolkit.Mvvm. Logging is handled by Serilog in both the WPF and CLI projects via the `Microsoft.Extensions.Logging` abstraction.

---

## Quality of Life

- **File → Clear All**: Resets both the File Counter and Cleanup tabs to their default settings in one step. Prompts for confirmation and is blocked while any operation is in progress.
- **📅 Today buttons**: Each date picker in the Cleanup tab has a "Today" button that sets both the date and its time fields to the current moment — ideal for "delete/move everything newer than right now" or "starting from this instant" scenarios.
- **Current time defaults**: All date/time fields in the Cleanup tab start at today's current time when the app opens, not at midnight or a fixed offset.
- **Case-insensitive file type filters**: Extension entries like `TXT`, `.txt`, and `.TXT` are all treated identically in both the File Counter and Cleanup tabs.

---

## In-App Help

The WPF application includes a built-in five-tab help window (Help menu) covering:

- Getting Started — overview, features, and quick start
- File Counter — depth options, file type filters, Box Drive scanning
- Cleanup — workflow, date modes, Move to Folder, Today/Now buttons, Clear All, safety options
- CLI usage and Windows Task Scheduler examples
- Box Drive integration details

---

## License

Private / internal use.
