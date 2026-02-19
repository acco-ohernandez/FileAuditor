# CLAUDE.md — FileAuditor Project Guide

This file provides context for Claude Code when working on the FileAuditor solution.

---

## Solution Layout

```
FileAuditor.sln
├── FileAuditor.Core/          # Shared class library (net8.0)
│   ├── Enums/                 # CountMode, CleanupDateMode, CleanupTarget, ScanStatus, etc.
│   ├── Models/                # ScanConfiguration, ScanResult, CleanupConfiguration, CleanupResult, etc.
│   ├── Helpers/               # PathValidator, FileSystemHelper (Win32 P/Invoke)
│   └── Services/              # FileScanner, BoxDriveHandler, ScanHistoryService, IExportService, ICleanupService
├── FileAuditor.WPF/           # WPF desktop app (net8.0-windows)
│   ├── ViewModels/            # MainViewModel (File Counter tab), CleanupViewModel (Cleanup tab)
│   ├── Views/                 # HelpWindow.xaml (5-tab in-app help)
│   ├── Converters/            # Value converters for XAML bindings
│   ├── App.xaml.cs            # DI container setup + Serilog configuration
│   └── MainWindow.xaml        # Two-tab main window (File Counter + Cleanup)
└── FileAuditor.CLI/           # Console app (net8.0)
    ├── Program.cs             # scan and cleanup commands
    ├── sample-config.json     # Example scan configuration
    └── cleanup-test-config.json
```

---

## Key Architectural Decisions

- **MVVM via CommunityToolkit.Mvvm**: ViewModels use `[ObservableProperty]` and `[RelayCommand]` source generators. Do not hand-roll `INotifyPropertyChanged` — always use the source generator attributes.
- **Dependency injection**: `App.xaml.cs` registers all Core services with `Microsoft.Extensions.DependencyInjection`. Add new services there and inject via constructor.
- **Logging**: Serilog is used everywhere via the `Microsoft.Extensions.Logging` abstraction (`ILogger<T>`). Never use `Console.WriteLine` or `Debug.WriteLine` for logging in non-test code.
- **Async throughout**: All scan and cleanup operations are async. The Core services use `Parallel.ForEachAsync` for parallel path processing. Do not block on async calls (no `.Result` or `.Wait()`).
- **No UI logic in Core**: `FileAuditor.Core` has no reference to WPF or any UI framework. Keep it that way.
- **Cancellation**: All long-running operations accept a `CancellationToken`. The ViewModels manage `CancellationTokenSource` for start/cancel.

---

## Core Services

| Service | Interface | Implementation | Notes |
|---|---|---|---|
| File scanning | `IFileScanner` | `FileScanner` | Recursive async enumeration with parallel threads |
| Box Drive | `IBoxDriveHandler` | `BoxDriveHandler` | Registry lookup + Win32 offline file detection |
| History | `IScanHistoryService` | `ScanHistoryService` | JSON persistence under `%LocalAppData%\FileAuditor\` |
| Export | `IExportService` | (implemented in WPF) | CSV via CsvHelper, JSON via Newtonsoft.Json |
| Cleanup | `ICleanupService` | (implemented in WPF) | Two-phase: Analyze then Execute |

---

## Enums Reference

### `CleanupDateMode`
```csharp
AnyDate      // No date filter
OlderThan    // LastWriteTime < specified date
NewerThan    // LastWriteTime > specified date
DateRange    // LastWriteTime between two dates
ExactDate    // LastWriteTime == specified date (optional time precision)
```

### `CountMode`
```csharp
FilesOnly
FoldersOnly
Both
```

### `CleanupTarget`
```csharp
FilesOnly
FoldersOnly
Both
```

### `ScanStatus`
```csharp
Pending
Running
Completed
Failed
Cancelled
```

---

## XAML / WPF Patterns

- **`DateModeToVisibilityConverter`**: Controls visibility of date picker fields in the Cleanup tab based on the selected `CleanupDateMode`. When adding new date modes, update this converter.
- **`BooleanToVisibilityConverter`**: Standard bool-to-Visibility binding helper.
- **`BytesToSizeConverter`**: Formats byte counts as human-readable strings (KB, MB, GB).
- **`ScanStatusToColorConverter`**: Maps `ScanStatus` enum values to brush colors for the results DataGrid.
- Expanders in the main window are used for collapsible configuration panels. Keep configuration sections inside Expanders to manage vertical space.

---

## Runtime Storage (`%LocalAppData%\FileAuditor\`)

| Path | Purpose |
|---|---|
| `History\` | JSON scan result history files |
| `Configurations\` | Named scan configurations (JSON) |
| `CleanupConfigurations\` | Named cleanup configurations (JSON) |
| `Logs\` | Rolling Serilog log files; WPF app writes `log-.txt`, CLI writes `cli-log-.txt` |

`ScanHistoryService` manages all reads and writes here. Do not write to this directory from anywhere else.

---

## Box Drive Integration

- Registry key: `HKCU\Software\Box\Box\preferences` → value `SyncRootPath`
- Fallback paths: `C:\CORP BOX`, `B:\`
- Offline file detection uses `GetFileAttributes` via P/Invoke in `FileSystemHelper.cs`; checks `FILE_ATTRIBUTE_OFFLINE` (0x1000) and `FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS` (0x400000)
- Hydration is triggered by opening the file to force a cloud download before scanning. Configurable timeout via `boxDriveRefreshTimeoutSeconds`.

---

## CLI Commands

```
FileAuditor.CLI scan    --config <path> [--output <path>] [--verbose]
FileAuditor.CLI cleanup --config <path> [--dry-run | --execute] [--verbose]
FileAuditor.CLI help    [scan | cleanup]
FileAuditor.CLI version
```

Exit codes: `0` = success, `1` = error.

Configs saved from the WPF app are valid CLI configs (same JSON schema for both `ScanConfiguration` and `CleanupConfiguration`).

---

## Common Tasks

### Adding a new CleanupDateMode
1. Add the value to `FileAuditor.Core/Enums/CleanupDateMode.cs`
2. Update `CleanupViewModel.cs` — add handling in the date-mode switch logic
3. Update `DateModeToVisibilityConverter.cs` — add visibility rules for any new UI controls
4. Update `CleanupViewModel`'s filter predicate in the Analyze/Execute path
5. Update `HelpWindow.xaml` — document the new mode in the Cleanup Help tab

### Adding a new scan option to `ScanConfiguration`
1. Add the property to `FileAuditor.Core/Models/ScanConfiguration.cs`
2. Expose it in `MainViewModel.cs` as an `[ObservableProperty]`
3. Bind it in `MainWindow.xaml`
4. Use it in `FileScanner.cs`
5. Update `sample-config.json` with the new field

### Adding a new Core service
1. Define the interface in `FileAuditor.Core/Services/`
2. Implement it in the same folder (or in WPF if it has UI dependencies)
3. Register it in `App.xaml.cs` with the DI container
4. Inject it into the relevant ViewModel(s) via constructor

---

## Dependencies — Do Not Upgrade Without Testing

| Package | Version pinned at | Risk |
|---|---|---|
| `CsvHelper` | 33.1.0 | API surface changes between major versions |
| `Newtonsoft.Json` | 13.0.4 | Stable; avoid migrating to System.Text.Json without full audit |
| `CommunityToolkit.Mvvm` | 8.4.0 | Source generator behavior can change between minor versions |
| `System.CommandLine` | 2.0.2 | Pre-release API; breaking changes possible |

---

## What Not To Do

- Do not add WPF/UI references to `FileAuditor.Core` — it must remain UI-agnostic.
- Do not use `.Result` or `.Wait()` on Tasks anywhere in the WPF project — it will cause deadlocks on the UI thread.
- Do not write directly to `%LocalAppData%\FileAuditor\` outside of `ScanHistoryService`.
- Do not use `Console.WriteLine` in the WPF project.
- Do not add new value converters unless a binding genuinely cannot be expressed as a ViewModel property.
- Do not skip the two-phase Analyze → Execute pattern for cleanup operations. Users must see a preview before any files are deleted.
