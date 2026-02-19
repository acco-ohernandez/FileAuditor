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

**Additional options:**
- Target files only, folders only, or both
- File type include/exclude filters
- Regex-based exclusion patterns
- Move to Recycle Bin vs. permanent delete
- Confirmation dialog before execution
- Auto-export of cleanup results to a timestamped CSV in the Logs folder

### CLI Automation

The CLI is designed for use with Windows Task Scheduler for unattended, scheduled audits.

```
FileAuditor.CLI scan --config <config.json> [--output <path>] [--verbose]
FileAuditor.CLI cleanup --config <config.json> [--dry-run | --execute] [--verbose]
FileAuditor.CLI help [scan | cleanup]
FileAuditor.CLI version
```

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

## In-App Help

The WPF application includes a built-in five-tab help window (Help menu) covering:

- Getting Started
- File Counter options and Box Drive scanning
- Cleanup workflow, date modes, and safety options
- CLI usage and Windows Task Scheduler examples
- Box Drive integration details
- FAQ and troubleshooting

---

## License

Private / internal use.
