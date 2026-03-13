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
│   ├── ViewModels/            # MainViewModel (File Counter tab), CleanupViewModel (Delete + Move tabs)
│   │   └── MoveInputMode.cs   # WPF-only enum: Single | Batch (used by CleanupViewModel in MoveToFolder mode)
│   ├── Views/                 # HelpWindow.xaml (7-tab in-app help)
│   ├── Converters/            # Value converters for XAML bindings
│   ├── App.xaml.cs            # DI container setup + Serilog configuration
│   └── MainWindow.xaml        # Three-tab main window (File Counter + Delete + Move to Folder)
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
- **Two separate CleanupViewModel instances**: `MainWindow` holds two independent `CleanupViewModel` instances — `DeleteViewModel` (locked to `CleanupOperationMode.Delete`) and `MoveViewModel` (locked to `CleanupOperationMode.MoveToFolder`). Both are constructed manually in `MainWindow`'s constructor using `IServiceProvider` (not registered in the DI container) so that the `initialMode` constructor parameter can be passed. Each has its own state, cancellation tokens, and configuration. The Delete tab sets its `DataContext` to `DeleteViewModel`; the Move to Folder tab sets its `DataContext` to `MoveViewModel`.
- **`MoveInputMode` enum**: Defined in `FileAuditor.WPF/ViewModels/MoveInputMode.cs` (WPF-only, not in Core). Values: `Single` (one source + one destination entered directly in the UI) and `Batch` (CSV file import of multiple source,destination pairs). Used only in `CleanupViewModel` when `_initialMode == MoveToFolder`.
- **Cancellation**: All long-running operations accept a `CancellationToken`. `MainViewModel` manages one `CancellationTokenSource` for the scan. `CleanupViewModel` manages two separate sources — `_analyzeCancellationTokenSource` and `_executeCancellationTokenSource` — so Analyze and ExecuteCleanup never share a token. The Cancel command cancels both.
- **Thread safety in Core**: `ScanResult` counters (`TotalFiles`, `TotalFolders`, `TotalSizeBytes`) are backed by `Interlocked` operations. `ScanResult.Errors` uses `ConcurrentBag<ScanError>`. `FileTypeBreakdown` uses `ConcurrentDictionary`. Use the `IncrementFiles()`, `IncrementFolders()`, `AddBytes()`, and `AddError()` helpers — do not assign to the counter properties directly from concurrent code.
- **MaxDepth semantics**: Both `MainViewModel` and `CleanupViewModel` default `MaxDepth` to `1`. With the corrected greater-than guard (`currentDepth > config.MaxDepth.Value`), `MaxDepth=0` scans only the root itself (no subdirectories), `MaxDepth=1` scans the root plus one level of subdirectories (the default), and `null` means unlimited. `ScanConfiguration.MaxDepth` defaults to `null` for CLI/programmatic use.
- **12-hour time fields in `CleanupViewModel`**: Cutoff, Start, and End times are each represented by three `string` observable properties (`*Hour`, `*Minute`, `*AmPm`) rather than a single `int`. `TryParse12HourTime()` converts them to a 24-hour `TimeSpan`; `To12Hour()` converts back when loading a saved config. All three `TimeSpan` fields (`CutoffTime`, `StartTime`, `EndTime`) are recomputed at the top of `CreateConfiguration()` to guarantee the correct value even if a ComboBox change notification fires after the other fields have already propagated.
- **Date defaults in `CleanupViewModel`**: `CutoffDate`, `StartDate`, and `EndDate` all default to `DateTime.Now` (not a fixed offset). All six time string fields (cutoff/start/end hour, minute, AM/PM) are initialized to the current wall-clock time in the constructor via `SetCurrentTimeDefaults()`. The same helper is called by `ClearAll()` to restore current time after a reset.
- **File type filter normalisation**: Both `FileScanner` and `CleanupService` use a local `ExtMatches`/`ExtensionMatches` helper that normalises each list entry to lowercase with a leading dot before comparing, so user input like `TXT`, `.txt`, or `.TXT` all match correctly. `MainViewModel.ParseFileTypes()` also lowercases and adds a leading dot at parse time so entries stored in `ScanConfiguration.IncludeFileTypes`/`ExcludeFileTypes` are pre-normalised. `CleanupViewModel.ParseList()` is NOT lowercased — it is also used for regex `ExcludePatterns` which must preserve case. Normalisation happens only at the comparison site in `ShouldCleanupItem`.

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

### `CleanupOperationMode`
```csharp
Delete        // Delete items (to Recycle Bin or permanently)
MoveToFolder  // Move items to a per-path destination folder
```

### `MoveInputMode` (WPF-only, in `FileAuditor.WPF/ViewModels/`)
```csharp
Single   // User enters one source path + one destination path in the UI
Batch    // User imports a CSV file with multiple source,destination pairs
```

### `CleanupStatus`
```csharp
Pending
Scanning
ReadyToDelete   // After Analyze in Delete mode
ReadyToMove     // After Analyze in MoveToFolder mode
Deleting
Moving
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
- **Tab DataContext binding**: The Delete and Move to Folder `<TabItem>` elements each set their own `DataContext` via `<TabItem.DataContext><Binding Path="DeleteViewModel" RelativeSource="{RelativeSource AncestorType=Window}"/></TabItem.DataContext>`. Controls inside these tabs use direct `{Binding Prop}` (inheriting the TabItem DataContext). Controls that need to reference the _other_ VM (rare) must use `RelativeSource AncestorType=Window` explicitly.
- **Help tab indices** (HelpWindow.xaml): 0=Getting Started, 1=File Counter, 2=Delete Help, 3=Move to Folder, 4=CLI Usage, 5=Box Drive, 6=FAQ. Code-behind click handlers (`ScanHelp_Click` → 1, `DeleteHelp_Click` → 2, `MoveHelp_Click` → 3, `CliHelp_Click` → 4) must match these indices.

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
- The registry path cache expires automatically after **5 minutes**, so a Box Drive installation that occurs mid-session is detected on the next scan without needing an app restart. Call `FileSystemHelper.ClearBoxDrivePathCache()` to force an immediate re-read.
- Hydration uses `TryHydrateFileAsync` / `TryHydrateDirectoryAsync` (async, `Task.Delay`-based polling). Synchronous wrappers `TryHydrateFile` / `TryHydrateDirectory` are kept for backwards compatibility but should not be called from async contexts.

---

## CLI Commands

```
FileAuditor.CLI scan    --config <path> [--output <path>] [--verbose]
FileAuditor.CLI cleanup --config <path> [--dry-run | --execute] [--verbose] [--move <dest>]
FileAuditor.CLI help    [scan | cleanup]
FileAuditor.CLI version
```

Exit codes: `0` = success, `1` = error.

Configs saved from the WPF app are valid CLI configs (same JSON schema for both `ScanConfiguration` and `CleanupConfiguration`).

### `--move <dest>` flag
Switches the cleanup command to MoveToFolder mode and stamps `<dest>` as the `DestinationPath` on **every** `PathItem` in the config. Useful when all paths should be moved to the same destination. For per-path destinations, set `DestinationPath` on each `PathItem` in the JSON config instead.

---

## Cleanup Paths — Two-Column Format

The **Paths to Clean** text area in the Cleanup tab (and in JSON configs) supports two formats:

| Format | When to use |
|---|---|
| `C:\Source` | Delete mode, or Move mode with a global `--move` destination via CLI |
| `C:\Source,D:\Destination` | MoveToFolder mode — per-path destination |

**Rules:**
- Split is performed on the **first comma only**, so paths containing commas are handled correctly.
- Destination existence is not validated at entry time — it is created automatically (`Directory.CreateDirectory`) at execution time.
- In MoveToFolder mode, `Analyze` enforces that every valid source path has a non-empty destination column.
- Duplicate source paths are deduplicated (first occurrence wins).
- The hint label above the text area changes dynamically based on the selected Operation Mode.

**`PathValidator.ParseCleanupPathsFromText`** is the correct parser for cleanup paths (supports the two-column format). **`PathValidator.ParsePathsFromText`** is the legacy single-column parser used by the File Counter tab — do not use it for cleanup paths.

**`CleanupResult.DestinationPath`** carries the per-path destination from Analyze through to Execute. It is stamped by the ViewModel after `AnalyzeMultiplePathsAsync` returns, using the `DestinationPath` from each matching `PathItem`.

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

### Adding a new date/time field to the Cleanup tab
1. Add backing `[ObservableProperty]` string fields for `*Hour`, `*Minute`, `*AmPm` in `CleanupViewModel`
2. Add `partial void On*Changed` callbacks that call a `Rebuild*Time()` helper
3. Add the `Rebuild*Time()` helper (calls `TryParse12HourTime` → assigns `TimeSpan` property)
4. Initialize the new fields in `SetCurrentTimeDefaults()` so they start at current time
5. Reset them in `ClearAll()` via `SetCurrentTimeDefaults()`
6. Add corresponding XAML controls in `MainWindow.xaml` with a "📅 Today" button bound to a `Set*ToNow` command
7. Update `HelpWindow.xaml` to document the new fields

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
- Do not use `result.TotalFiles++` or `result.Errors.Add(...)` directly in `FileScanner` — use `result.IncrementFiles()`, `result.IncrementFolders()`, `result.AddBytes()`, and `result.AddError()` to maintain thread safety.
- Do not call `Clone()` on `ScanConfiguration` or `CleanupConfiguration` and then mutate a `PathItem` inside — `Clone()` deep-copies `PathItem` instances via `PathItem.Clone()`.
- Do not enumerate subdirectories twice in `FileScanner` — the single enumeration result is reused for both counting and recursion.
- Do not use `async Task` on methods that have no `await` — use synchronous signatures instead (see `BrowseFolder`, `LoadConfiguration` in both ViewModels).
- Do not call `GetScanHistoryAsync` with both a `path` filter and a `limit` expecting `limit` total records — the limit is applied after the path filter, so `limit` controls matching records, not total records read.
- Do not read `CutoffTime`, `StartTime`, or `EndTime` directly in `CreateConfiguration()` without first calling `TryParse12HourTime()` to recompute them — the TimeSpan properties may lag behind the string fields if the AM/PM ComboBox fires its change notification out of order.
- Do not display `Last Modified` timestamps in the Cleanup results DataGrid using `HH:mm:ss` (24-hour) — use `hh:mm:ss tt` (12-hour AM/PM) to be consistent with the time entry controls. CSV exports may retain ISO 24-hour format for data portability.
- Do not use `PathValidator.ParsePathsFromText` for cleanup paths — use `PathValidator.ParseCleanupPathsFromText` which supports the two-column `source,destination` format. Using the wrong parser loses destination path data.
- Do not set `CleanupConfiguration.MoveDestinationPath` — this property no longer exists. Destinations are per-path on `PathItem.DestinationPath` and carried through `CleanupResult.DestinationPath` at runtime.
- Do not call `CleanupService.MoveItems` without first stamping `CleanupResult.DestinationPath` — the service returns an error (not an exception) and skips all items if the destination is null or empty.
- Do not use `>=` in the MaxDepth guard (`currentDepth >= config.MaxDepth.Value`) — the correct check is `>` so that MaxDepth=1 scans the root plus one level of subdirectories. The `>=` form was a bug that caused files at depth 1 to never be found.
- Do not use `List<string>.Contains(extension)` for file type comparisons — use the `ExtensionMatches` / `ExtMatches` local helper that normalises both the list entry and the extension to lowercase with a leading dot. Raw `Contains` silently misses entries like `"TXT"`, `"txt"`, or `".TXT"`.
- Do not lowercase entries in `CleanupViewModel.ParseList()` — that method is also used for `ExcludePatterns` (regex), which must preserve case. Normalisation belongs at the comparison site (`ShouldCleanupItem` in `CleanupService`).
- Do not call `ClearAll()` on either ViewModel while an operation is in progress — both guard against this and show a warning. The UI's "Clear All" handler in `MainWindow.xaml.cs` delegates to both VMs and relies on their guards.
- Do not initialise `CleanupViewModel` date fields to fixed offsets (e.g. `DateTime.Now.AddDays(-30)`) — all date fields now default to `DateTime.Now`, and time fields are set by `SetCurrentTimeDefaults()` which is called in the constructor.
- Do not call `[RelayCommand]`-decorated ViewModel methods directly from code-behind (e.g. `MainViewModel.ClearAll()`) — the source generator keeps the backing method `private`. Always call the generated `*Command` property instead: `MainViewModel.ClearAllCommand.Execute(null)`.
- Do not use `CsvHelper` with `HasHeaderRecord = true` for importing cleanup paths — CsvHelper silently consumes the first data row as column headers when no header row is present. Use `ExportService.ImportPathsFromCsvAsync` which reads lines directly, detects headers via Windows path prefix (`X:` or `\\`), and handles the two-column `source,destination` format correctly.
- Do not register `CleanupViewModel` in the DI container (`App.xaml.cs`) — it is constructed manually in `MainWindow`'s constructor (two instances: `DeleteViewModel` and `MoveViewModel`) so the `initialMode` parameter can be passed per-instance. Registering it in DI would create a third, unused singleton.
- Do not add an Operation Mode ComboBox to the Delete tab or Move to Folder tab — each tab's `CleanupViewModel` is locked to its mode via `_initialMode` set in the constructor. The mode cannot be changed at runtime from the UI.
- Do not share a `CleanupViewModel` instance between the Delete and Move to Folder tabs — they are intentionally separate instances with independent state (separate cancellation tokens, separate scan results, separate configurations).
