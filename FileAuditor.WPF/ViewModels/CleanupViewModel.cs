using System.Collections.ObjectModel;
using System.IO;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using FileAuditor.Core.Enums;
using FileAuditor.Core.Helpers;
using FileAuditor.Core.Models;
using FileAuditor.Core.Services;

using Microsoft.Extensions.Logging;

using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace FileAuditor.WPF.ViewModels
{
    public partial class CleanupViewModel : ObservableObject
    {
        private readonly ICleanupService _cleanupService;
        private readonly ILogger<CleanupViewModel> _logger;

        // The mode this VM instance is locked to (Delete or MoveToFolder).
        // Set once at construction; LoadConfiguration() restores it after loading.
        private readonly CleanupOperationMode _initialMode;

        // Separate CTS per operation so Analyze and ExecuteCleanup never share one.
        private CancellationTokenSource? _analyzeCancellationTokenSource;
        private CancellationTokenSource? _executeCancellationTokenSource;

        // Snapshot of the configuration used during the most recent Analyze run.
        // ExecuteCleanup re-uses this so it executes exactly what was previewed.
        private CleanupConfiguration? _lastAnalysisConfig;

        [ObservableProperty]
        private string _pathsText = string.Empty;

        [ObservableProperty]
        private CleanupTarget _selectedTarget = CleanupTarget.FilesOnly;

        [ObservableProperty]
        private CleanupDateMode _selectedDateMode = CleanupDateMode.AnyDate;



        [ObservableProperty]
        private DateTime? _startDate = DateTime.Now;

        [ObservableProperty]
        private DateTime? _endDate = DateTime.Now;

        [ObservableProperty]
        private bool _isRecursive = true;

        [ObservableProperty]
        private string _maxDepthText = "1";

        [ObservableProperty]
        private bool _includeHiddenFiles = false;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ExecuteButtonLabel))]
        private bool _dryRun = true;

        [ObservableProperty]
        private bool _requireConfirmation = true;

        [ObservableProperty]
        private bool _moveToRecycleBin = true;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDeleteMode))]
        [NotifyPropertyChangedFor(nameof(IsMoveMode))]
        [NotifyPropertyChangedFor(nameof(ExecuteButtonLabel))]
        [NotifyPropertyChangedFor(nameof(PathsHintText))]
        private CleanupOperationMode _operationMode = CleanupOperationMode.Delete;

        public bool IsDeleteMode => OperationMode == CleanupOperationMode.Delete;
        public bool IsMoveMode => OperationMode == CleanupOperationMode.MoveToFolder;

        /// <summary>
        /// Label for the Execute button. Reflects both the operation mode and whether
        /// Dry Run is enabled so the button itself signals what clicking it will do:
        ///   Dry Run ON  → "Preview Delete" / "Preview Move"
        ///   Dry Run OFF → "Execute Cleanup" / "Execute Move"
        /// </summary>
        public string ExecuteButtonLabel =>
            DryRun
                ? (OperationMode == CleanupOperationMode.MoveToFolder ? "Preview Move"    : "Preview Delete")
                : (OperationMode == CleanupOperationMode.MoveToFolder ? "Execute Move"    : "Execute Cleanup");

        /// <summary>
        /// True after a DryRun simulation completes. Drives the "PREVIEW ONLY" banner
        /// visibility in both the Delete and Move to Folder tabs.
        /// </summary>
        [ObservableProperty]
        private bool _showDryRunBanner = false;

        /// <summary>
        /// Hint text shown above the Paths to Clean text box.
        /// Changes to explain the two-column format when in MoveToFolder mode.
        /// </summary>
        public string PathsHintText => OperationMode == CleanupOperationMode.MoveToFolder
            ? "Enter paths (source,destination per line — one pair per line):"
            : "Enter paths (one per line):";

        [ObservableProperty]
        private string _includeFileTypes = string.Empty;

        [ObservableProperty]
        private string _excludeFileTypes = string.Empty;

        [ObservableProperty]
        private string _excludePatterns = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotAnalyzing))]
        private bool _isAnalyzing = false;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotDeleting))]
        private bool _isDeleting = false;

        public bool IsNotAnalyzing => !IsAnalyzing;
        public bool IsNotDeleting  => !IsDeleting;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private ObservableCollection<CleanupResult> _analysisResults = new();

        [ObservableProperty]
        private CleanupResult? _selectedResult;


        [ObservableProperty]
        private bool _includeTime = false;

        [ObservableProperty]
        private TimeSpan _startTime = new TimeSpan(0, 0, 0);

        [ObservableProperty]
        private TimeSpan _endTime = new TimeSpan(23, 59, 0);

        [ObservableProperty]
        private ObservableCollection<CleanupConfiguration> _savedConfigurations = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedConfiguration))]
        private CleanupConfiguration? _selectedConfiguration;

        public bool HasSelectedConfiguration => SelectedConfiguration != null;

        // ── 12-hour time helpers ─────────────────────────────────────────────────
        // Cutoff time (OlderThan / NewerThan)
        [ObservableProperty] private string _cutoffTimeHour = "12";
        [ObservableProperty] private string _cutoffTimeMinute = "00";
        [ObservableProperty] private string _cutoffTimeAmPm = "AM";

        // Start time (DateRange start / ExactDate)
        [ObservableProperty] private string _startTimeHour = "12";
        [ObservableProperty] private string _startTimeMinute = "00";
        [ObservableProperty] private string _startTimeAmPm = "AM";

        // End time (DateRange end)
        [ObservableProperty] private string _endTimeHour = "11";
        [ObservableProperty] private string _endTimeMinute = "59";
        [ObservableProperty] private string _endTimeAmPm = "PM";

        [ObservableProperty]
        private DateTime? _cutoffDate = DateTime.Now;

        [ObservableProperty]
        private TimeSpan _cutoffTime = new TimeSpan(0, 0, 0);

        // ── AM/PM options ────────────────────────────────────────────────────────
        public ObservableCollection<string> AmPmOptions { get; } = new() { "AM", "PM" };

        // ── Partial method callbacks: rebuild TimeSpan when any 12h field changes ─

        partial void OnCutoffTimeHourChanged(string value) => RebuildCutoffTime();
        partial void OnCutoffTimeMinuteChanged(string value) => RebuildCutoffTime();
        partial void OnCutoffTimeAmPmChanged(string value) => RebuildCutoffTime();

        partial void OnStartTimeHourChanged(string value) => RebuildStartTime();
        partial void OnStartTimeMinuteChanged(string value) => RebuildStartTime();
        partial void OnStartTimeAmPmChanged(string value) => RebuildStartTime();

        partial void OnEndTimeHourChanged(string value) => RebuildEndTime();
        partial void OnEndTimeMinuteChanged(string value) => RebuildEndTime();
        partial void OnEndTimeAmPmChanged(string value) => RebuildEndTime();

        // ── Private helpers that convert 12-hour fields → TimeSpan ───────────────

        private void RebuildCutoffTime()
        {
            if (TryParse12HourTime(CutoffTimeHour, CutoffTimeMinute, CutoffTimeAmPm, out var ts))
                CutoffTime = ts;
        }

        private void RebuildStartTime()
        {
            if (TryParse12HourTime(StartTimeHour, StartTimeMinute, StartTimeAmPm, out var ts))
                StartTime = ts;
        }

        private void RebuildEndTime()
        {
            if (TryParse12HourTime(EndTimeHour, EndTimeMinute, EndTimeAmPm, out var ts))
                EndTime = ts;
        }

        /// <summary>
        /// Parses 12-hour hour/minute/ampm strings into a <see cref="TimeSpan"/>.
        /// Returns false (and leaves <paramref name="result"/> at default) if any field
        /// is out of range so the TimeSpan is not updated mid-typing.
        /// </summary>
        private static bool TryParse12HourTime(string hourStr, string minuteStr, string ampm, out TimeSpan result)
        {
            result = default;

            if (!int.TryParse(hourStr, out int hour) || hour < 1 || hour > 12)
                return false;

            if (!int.TryParse(minuteStr, out int minute) || minute < 0 || minute > 59)
                return false;

            // Convert 12-hour clock to 24-hour
            int hour24 = hour;
            if (ampm == "AM")
            {
                if (hour == 12) hour24 = 0;   // 12 AM = midnight
            }
            else // PM
            {
                if (hour != 12) hour24 = hour + 12;  // 12 PM stays 12, 1–11 PM → 13–23
            }

            result = new TimeSpan(hour24, minute, 0);
            return true;
        }

        /// <summary>
        /// Converts a 24-hour <see cref="TimeSpan"/> back into 12-hour field strings.
        /// </summary>
        private static (string Hour, string Minute, string AmPm) To12Hour(TimeSpan ts)
        {
            int h24 = ts.Hours % 24;
            string ampm = h24 < 12 ? "AM" : "PM";

            int h12 = h24 % 12;
            if (h12 == 0) h12 = 12;

            return (h12.ToString(), ts.Minutes.ToString("D2"), ampm);
        }

        public ObservableCollection<CleanupTarget> CleanupTargets { get; } = new()
        {
            CleanupTarget.FilesOnly,
            CleanupTarget.FoldersOnly,
            CleanupTarget.Both
        };

        public ObservableCollection<CleanupDateMode> DateModes { get; } = new()
        {
            CleanupDateMode.AnyDate,
            CleanupDateMode.OlderThan,
            CleanupDateMode.NewerThan,
            CleanupDateMode.DateRange,
            CleanupDateMode.ExactDate
        };

        public ObservableCollection<CleanupOperationMode> OperationModes { get; } = new()
        {
            CleanupOperationMode.Delete,
            CleanupOperationMode.MoveToFolder
        };

        // ── Move tab: Single vs Batch input mode ─────────────────────────────────
        public ObservableCollection<MoveInputMode> MoveInputModes { get; } = new()
        {
            MoveInputMode.Single,
            MoveInputMode.Batch
        };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSingleMoveMode))]
        [NotifyPropertyChangedFor(nameof(IsBatchMoveMode))]
        private MoveInputMode _moveInputMode = MoveInputMode.Single;

        public bool IsSingleMoveMode => MoveInputMode == MoveInputMode.Single;
        public bool IsBatchMoveMode  => MoveInputMode == MoveInputMode.Batch;

        /// <summary>Single-move source folder/file path.</summary>
        [ObservableProperty]
        private string _singleSourcePath = string.Empty;

        /// <summary>Single-move destination folder path.</summary>
        [ObservableProperty]
        private string _singleDestinationPath = string.Empty;

        // Add to constructor injection
        private readonly IScanHistoryService _historyService;
        private readonly IExportService _exportService;

        public CleanupViewModel(
            ICleanupService cleanupService,
            IScanHistoryService historyService,
            IExportService exportService,
            ILogger<CleanupViewModel> logger,
            CleanupOperationMode initialMode = CleanupOperationMode.Delete)
        {
            _cleanupService = cleanupService;
            _historyService = historyService;
            _exportService = exportService;
            _logger = logger;
            _initialMode = initialMode;
            _operationMode = initialMode;

            SetCurrentTimeDefaults();
            _ = LoadSavedConfigurations();
        }

        /// <summary>
        /// Sets all six time string fields (cutoff, start, end) to the current wall-clock time.
        /// Called on startup and whenever ClearAll resets the form.
        /// </summary>
        private void SetCurrentTimeDefaults()
        {
            var now = DateTime.Now;
            var (h, m, ap) = To12Hour(new TimeSpan(now.Hour, now.Minute, 0));
            CutoffTimeHour = h;
            CutoffTimeMinute = m;
            CutoffTimeAmPm = ap;
            StartTimeHour = h;
            StartTimeMinute = m;
            StartTimeAmPm = ap;
            EndTimeHour = h;
            EndTimeMinute = m;
            EndTimeAmPm = ap;
        }

        /// <summary>
        /// Sets the Cutoff Date and Cutoff Time fields to the current date/time.
        /// </summary>
        [RelayCommand]
        private void SetCutoffToNow()
        {
            var now = DateTime.Now;
            CutoffDate = now;
            var (h, m, ap) = To12Hour(new TimeSpan(now.Hour, now.Minute, 0));
            CutoffTimeHour = h;
            CutoffTimeMinute = m;
            CutoffTimeAmPm = ap;
        }

        /// <summary>
        /// Sets the Start Date and Start Time fields to the current date/time.
        /// </summary>
        [RelayCommand]
        private void SetStartToNow()
        {
            var now = DateTime.Now;
            StartDate = now;
            var (h, m, ap) = To12Hour(new TimeSpan(now.Hour, now.Minute, 0));
            StartTimeHour = h;
            StartTimeMinute = m;
            StartTimeAmPm = ap;
        }

        /// <summary>
        /// Sets the End Date and End Time fields to the current date/time.
        /// </summary>
        [RelayCommand]
        private void SetEndToNow()
        {
            var now = DateTime.Now;
            EndDate = now;
            var (h, m, ap) = To12Hour(new TimeSpan(now.Hour, now.Minute, 0));
            EndTimeHour = h;
            EndTimeMinute = m;
            EndTimeAmPm = ap;
        }

        /// <summary>
        /// Resets every field on the Cleanup tab back to its default state.
        /// Blocked while Analyze or Execute is in progress.
        /// </summary>
        [RelayCommand]
        private void ClearAll()
        {
            if (IsAnalyzing || IsDeleting)
            {
                MessageBox.Show("An operation is currently in progress. Please cancel it before clearing.",
                    "Operation In Progress", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PathsText = string.Empty;
            SingleSourcePath = string.Empty;
            SingleDestinationPath = string.Empty;
            MoveInputMode = MoveInputMode.Single;
            SelectedTarget = CleanupTarget.FilesOnly;
            SelectedDateMode = CleanupDateMode.AnyDate;
            CutoffDate = DateTime.Now;
            StartDate = DateTime.Now;
            EndDate = DateTime.Now;
            IncludeTime = false;
            IsRecursive = true;
            MaxDepthText = "1";
            IncludeHiddenFiles = false;
            DryRun = true;
            RequireConfirmation = true;
            MoveToRecycleBin = true;
            OperationMode = _initialMode;
            IncludeFileTypes = string.Empty;
            ExcludeFileTypes = string.Empty;
            ExcludePatterns = string.Empty;
            AnalysisResults.Clear();
            SelectedResult = null;
            _lastAnalysisConfig = null;
            SelectedConfiguration = null;
            SetCurrentTimeDefaults();
            StatusMessage = "Ready";
        }

        [RelayCommand]
        private void BrowseFolder()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select a folder to clean up",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (!string.IsNullOrWhiteSpace(PathsText))
                    PathsText += Environment.NewLine;

                PathsText += dialog.SelectedPath;
            }
        }

        [RelayCommand]
        private void BrowseSingleSource()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select the source folder to move from",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                SingleSourcePath = dialog.SelectedPath;
        }

        [RelayCommand]
        private void BrowseSingleDestination()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select the destination folder to move to",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                SingleDestinationPath = dialog.SelectedPath;
        }

        [RelayCommand]
        private async Task ImportFromCsv()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                Title = "Import Paths from CSV"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var paths = await _exportService.ImportPathsFromCsvAsync(dialog.FileName);

                    if (!string.IsNullOrWhiteSpace(PathsText))
                        PathsText += Environment.NewLine;

                    PathsText += string.Join(Environment.NewLine, paths.Select(p =>
                        string.IsNullOrWhiteSpace(p.DestinationPath)
                            ? p.Path
                            : $"{p.Path},{p.DestinationPath}"));

                    StatusMessage = $"Imported {paths.Count} path(s) from CSV";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error importing CSV:\n\n{ex.Message}",
                        "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private async Task Analyze()
        {
            if (IsAnalyzing || IsDeleting)
                return;

            try
            {
                IsAnalyzing = true;
                StatusMessage = "Analyzing paths...";
                AnalysisResults.Clear();
                _lastAnalysisConfig = null;

                List<PathItem> parsedPaths;

                // Single move mode: build one PathItem from the dedicated source/dest fields.
                if (OperationMode == CleanupOperationMode.MoveToFolder && IsSingleMoveMode)
                {
                    if (string.IsNullOrWhiteSpace(SingleSourcePath))
                    {
                        MessageBox.Show("Please enter a source path.", "Source Required",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(SingleDestinationPath))
                    {
                        MessageBox.Show("Please enter a destination path.", "Destination Required",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var pathItem = PathValidator.ValidatePath(SingleSourcePath);
                    if (!pathItem.IsValid)
                    {
                        MessageBox.Show(
                            $"Source path is not accessible:\n{pathItem.ValidationError ?? SingleSourcePath}",
                            "Invalid Source", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    pathItem.DestinationPath = SingleDestinationPath;
                    parsedPaths = new List<PathItem> { pathItem };
                }
                else
                {
                    // Batch mode (Delete or MoveToFolder): parse the two-column text area.
                    if (string.IsNullOrWhiteSpace(PathsText))
                    {
                        MessageBox.Show("Please enter at least one path to analyze.", "No Paths",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    parsedPaths = PathValidator.ParseCleanupPathsFromText(PathsText);

                    if (!parsedPaths.Any(p => p.IsValid))
                    {
                        MessageBox.Show("No valid paths found. Please check the entered paths.",
                            "No Valid Paths", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // In MoveToFolder batch mode every valid path must have a destination.
                    if (OperationMode == CleanupOperationMode.MoveToFolder)
                    {
                        var missingDest = parsedPaths
                            .Where(p => p.IsValid && string.IsNullOrWhiteSpace(p.DestinationPath))
                            .ToList();

                        if (missingDest.Any())
                        {
                            MessageBox.Show(
                                "Move to Folder mode requires a destination for each path.\n\n" +
                                "Use the format:  source,destination  (one pair per line).\n\n" +
                                "Paths missing a destination:\n" +
                                string.Join("\n", missingDest.Select(p => "  • " + p.Path)),
                                "Destination Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }
                }

                // Snapshot config now so Execute uses exactly what was analyzed.
                var config = CreateConfiguration(parsedPaths);
                _lastAnalysisConfig = config;

                var validPaths = parsedPaths.Where(p => p.IsValid).ToList();

                _analyzeCancellationTokenSource = new CancellationTokenSource();

                var progress = new Progress<CleanupProgress>(p =>
                {
                    StatusMessage = p.CurrentOperation;
                });

                var results = await _cleanupService.AnalyzeMultiplePathsAsync(
                    validPaths.Select(p => p.Path).ToList(),
                    config,
                    _analyzeCancellationTokenSource.Token,
                    progress);

                // Stamp the per-path destination onto each CleanupResult.
                var destLookup = validPaths
                    .Where(p => !string.IsNullOrWhiteSpace(p.DestinationPath))
                    .ToDictionary(p => p.Path, p => p.DestinationPath!, StringComparer.OrdinalIgnoreCase);

                int totalExcluded = 0;

                foreach (var result in results)
                {
                    if (destLookup.TryGetValue(result.Path, out var dest))
                        result.DestinationPath = dest;

                    // In MoveToFolder mode, exclude items that are already inside the
                    // destination folder (e.g. when the destination is a subfolder of
                    // the source). Items equal to or descending from the destination
                    // path are removed so they are never moved into themselves.
                    if (OperationMode == CleanupOperationMode.MoveToFolder
                        && !string.IsNullOrEmpty(result.DestinationPath))
                    {
                        var destNorm = Path.GetFullPath(result.DestinationPath)
                                           .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                       + Path.DirectorySeparatorChar;

                        var excluded = result.Items
                            .Where(item =>
                            {
                                var itemNorm = Path.GetFullPath(item.Path)
                                                   .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                                // Exclude items equal to the destination dir itself or nested inside it.
                                return itemNorm.StartsWith(destNorm, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(itemNorm,
                                           destNorm.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                           StringComparison.OrdinalIgnoreCase);
                            })
                            .ToList();

                        foreach (var item in excluded)
                        {
                            result.Items.Remove(item);
                            if (item.IsDirectory)
                                result.FoldersIdentified--;
                            else
                                result.FilesIdentified--;
                            result.TotalSizeBytes -= item.SizeBytes;
                            totalExcluded++;
                        }
                    }

                    AnalysisResults.Add(result);
                }

                var totalFiles = results.Sum(r => r.FilesIdentified);
                var totalFolders = results.Sum(r => r.FoldersIdentified);
                var totalSize = results.Sum(r => r.TotalSizeBytes);

                var statusMsg = $"Analysis complete: {totalFiles} files, {totalFolders} folders ({FormatSize(totalSize)}) identified";
                if (totalExcluded > 0)
                    statusMsg += $" — {totalExcluded} item(s) excluded (already inside destination folder)";

                StatusMessage = statusMsg;

                _logger.LogInformation("Cleanup analysis complete. {Files} files, {Folders} folders identified",
                    totalFiles, totalFolders);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Analysis cancelled";
                _logger.LogWarning("Analysis cancelled by user");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"An error occurred during analysis:\n\n{ex.Message}",
                    "Analysis Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _logger.LogError(ex, "Error during cleanup analysis");
            }
            finally
            {
                IsAnalyzing = false;
                var cts = _analyzeCancellationTokenSource;
                _analyzeCancellationTokenSource = null;
                cts?.Dispose();
            }
        }

        [RelayCommand]
        private async Task ExecuteCleanup()
        {
            if (IsAnalyzing || IsDeleting)
                return;

            if (!AnalysisResults.Any() || _lastAnalysisConfig == null)
            {
                MessageBox.Show("Please run an analysis first.", "No Analysis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var totalFiles = AnalysisResults.Sum(r => r.FilesIdentified);
            var totalFolders = AnalysisResults.Sum(r => r.FoldersIdentified);
            var totalSize = AnalysisResults.Sum(r => r.TotalSizeBytes);

            // DryRun: skip the confirmation dialog — the user clicked "Preview Delete/Move"
            // knowing it is a simulation. Execute falls through to the service which
            // simulates without touching any files.
            if (!DryRun && RequireConfirmation)
            {
                string action;
                if (OperationMode == CleanupOperationMode.MoveToFolder)
                    action = "move to per-path destinations";
                else
                    action = MoveToRecycleBin ? "move to recycle bin" : "permanently delete";

                var message = $"Are you sure you want to {action}:\n\n" +
                              $"• {totalFiles} file(s)\n" +
                              $"• {totalFolders} folder(s)\n" +
                              $"• Total size: {FormatSize(totalSize)}\n\n" +
                              $"This action cannot be easily undone!";

                var result = MessageBox.Show(message, "Confirm Cleanup",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                    return;
            }

            try
            {
                IsDeleting = true;
                ShowDryRunBanner = false; // reset banner while operation is in progress
                StatusMessage = DryRun
                    ? (OperationMode == CleanupOperationMode.MoveToFolder ? "Previewing move..." : "Previewing deletion...")
                    : (OperationMode == CleanupOperationMode.MoveToFolder ? "Moving items..."    : "Deleting items...");

                // Use the config snapshot from Analyze so Execute is consistent with the preview.
                var config = _lastAnalysisConfig!;
                _executeCancellationTokenSource = new CancellationTokenSource();

                var progress = new Progress<CleanupProgress>(p =>
                {
                    StatusMessage = p.CurrentOperation;
                });

                foreach (var analysisResult in AnalysisResults)
                {
                    await _cleanupService.ExecuteCleanupAsync(
                        analysisResult,
                        config,
                        _executeCancellationTokenSource.Token,
                        progress);
                }

                // CleanupItem is a plain POCO (no INotifyPropertyChanged). Force the
                // DataGrid to re-bind by clearing and re-adding every result so that
                // the updated WasMoved / MovedToPath / WasDeleted / WouldBe* columns are visible.
                var resultsSnapshot = AnalysisResults.ToList();
                AnalysisResults.Clear();
                foreach (var r in resultsSnapshot)
                    AnalysisResults.Add(r);

                // Show the "PREVIEW ONLY" banner when any result was a DryRun simulation.
                ShowDryRunBanner = AnalysisResults.Any(r => r.WasDryRun);

                bool isMoveMode = OperationMode == CleanupOperationMode.MoveToFolder;
                bool wasDryRun  = AnalysisResults.Any(r => r.WasDryRun);

                // Use simulation counters (WouldDelete/WouldMove) for DryRun summary;
                // real counters (Deleted/Moved) for actual execution summary.
                var processedFiles = wasDryRun
                    ? (isMoveMode ? AnalysisResults.Sum(r => r.FilesWouldMove)   : AnalysisResults.Sum(r => r.FilesWouldDelete))
                    : (isMoveMode ? AnalysisResults.Sum(r => r.FilesMoved)        : AnalysisResults.Sum(r => r.FilesDeleted));
                var processedFolders = wasDryRun
                    ? (isMoveMode ? AnalysisResults.Sum(r => r.FoldersWouldMove)  : AnalysisResults.Sum(r => r.FoldersWouldDelete))
                    : (isMoveMode ? AnalysisResults.Sum(r => r.FoldersMoved)      : AnalysisResults.Sum(r => r.FoldersDeleted));

                // Auto-export to logs folder
                var logsFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FileAuditor", "Logs");

                Directory.CreateDirectory(logsFolder);

                // CSV filename includes a "preview_" prefix for DryRun exports so they
                // are instantly distinguishable from real operation logs.
                var opLabel = wasDryRun
                    ? (isMoveMode ? "preview_move" : "preview_delete")
                    : (isMoveMode ? "move"         : "delete");
                var autoExportPath = Path.Combine(logsFolder,
                    $"{opLabel}_results_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                try
                {
                    using var writer = new StreamWriter(autoExportPath);

                    // DryRun exports include "Would Move To" instead of "Moved To"
                    if (isMoveMode)
                        await writer.WriteLineAsync(wasDryRun
                            ? "Path,Type,Last Modified,Size (bytes),Action,Would Move To"
                            : "Path,Type,Last Modified,Size (bytes),Action,Moved To,Error");
                    else
                        await writer.WriteLineAsync(wasDryRun
                            ? "Path,Type,Last Modified,Size (bytes),Action"
                            : "Path,Type,Last Modified,Size (bytes),Action,Error");

                    foreach (var analysisResult in AnalysisResults)
                    {
                        foreach (var item in analysisResult.Items)
                        {
                            var type = item.IsDirectory ? "Folder" : "File";

                            if (isMoveMode)
                            {
                                // Use ActionLabel (WouldBeMoved/WasMoved/Error) and the
                                // DisplayMovedToPath which covers both real and simulated moves.
                                var dest  = item.DisplayMovedToPath ?? "";
                                var error = item.MoveError ?? "";
                                await writer.WriteLineAsync(wasDryRun
                                    ? $"\"{item.Path}\",{type},{item.LastModified:yyyy-MM-dd HH:mm:ss},{item.SizeBytes},{item.MoveActionLabel},\"{dest}\""
                                    : $"\"{item.Path}\",{type},{item.LastModified:yyyy-MM-dd HH:mm:ss},{item.SizeBytes},{item.MoveActionLabel},\"{dest}\",\"{error}\"");
                            }
                            else
                            {
                                var error = item.DeletionError ?? "";
                                await writer.WriteLineAsync(wasDryRun
                                    ? $"\"{item.Path}\",{type},{item.LastModified:yyyy-MM-dd HH:mm:ss},{item.SizeBytes},{item.DeleteActionLabel}"
                                    : $"\"{item.Path}\",{type},{item.LastModified:yyyy-MM-dd HH:mm:ss},{item.SizeBytes},{item.DeleteActionLabel},\"{error}\"");
                            }
                        }
                    }

                    // Status message and completion dialog use different wording for DryRun.
                    string statusVerb, title, summary;
                    if (wasDryRun)
                    {
                        statusVerb = isMoveMode ? "would be moved" : "would be deleted";
                        title      = isMoveMode ? "Preview Complete" : "Preview Complete";
                        summary    = isMoveMode
                            ? $"Would Move: {processedFiles} files, {processedFolders} folders"
                            : $"Would Delete: {processedFiles} files, {processedFolders} folders";
                    }
                    else
                    {
                        statusVerb = isMoveMode ? "moved" : "deleted";
                        title      = isMoveMode ? "Move Complete" : "Cleanup Complete";
                        summary    = isMoveMode
                            ? $"Moved: {processedFiles} files, {processedFolders} folders"
                            : $"Deleted: {processedFiles} files, {processedFolders} folders";
                    }

                    StatusMessage = $"{(wasDryRun ? "Preview" : "Operation")} complete: {processedFiles} files, {processedFolders} folders {statusVerb}";

                    var completionNote = wasDryRun
                        ? "⚠ This was a DRY RUN — no files were modified.\n\nDisable Dry Run and click the button again to perform the actual operation.\n\n"
                        : "Operation completed successfully!\n\n";

                    MessageBox.Show($"{completionNote}{summary}\n\n" +
                                   $"Results exported to:\n{autoExportPath}\n\n" +
                                   $"Logs folder: {logsFolder}",
                        title, MessageBoxButton.OK,
                        wasDryRun ? MessageBoxImage.Information : MessageBoxImage.Information);

                    _logger.LogInformation(
                        "{Mode} complete. {Files} files, {Folders} folders {Verb}. Results: {Path}",
                        wasDryRun ? "DryRun simulation" : "Operation",
                        processedFiles, processedFolders, statusVerb, autoExportPath);
                }
                catch (Exception exportEx)
                {
                    _logger.LogError(exportEx, "Error auto-exporting results");

                    var verb2 = wasDryRun
                        ? (isMoveMode ? "would be moved" : "would be deleted")
                        : (isMoveMode ? "moved" : "deleted");
                    MessageBox.Show($"{(wasDryRun ? "Preview" : "Operation")} completed!\n\n" +
                                   $"Processed: {processedFiles} files, {processedFolders} folders {verb2}\n\n" +
                                   $"Warning: Could not export results:\n{exportEx.Message}",
                        "Operation Complete", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Cleanup cancelled";
                _logger.LogWarning("Cleanup cancelled by user");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"An error occurred during cleanup:\n\n{ex.Message}",
                    "Cleanup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _logger.LogError(ex, "Error during cleanup execution");
            }
            finally
            {
                IsDeleting = false;
                var cts = _executeCancellationTokenSource;
                _executeCancellationTokenSource = null;
                cts?.Dispose();
            }
        }


        [RelayCommand]
        private void Cancel()
        {
            // Cancel whichever operation is currently running.
            _analyzeCancellationTokenSource?.Cancel();
            _executeCancellationTokenSource?.Cancel();
            StatusMessage = "Cancelling operation...";
        }

        [RelayCommand]
        private void ClearResults()
        {
            AnalysisResults.Clear();
            ShowDryRunBanner = false;
            StatusMessage = "Results cleared";
        }

        [RelayCommand]
        private async Task ExportResults()
        {
            if (!AnalysisResults.Any())
            {
                MessageBox.Show("No results to export.", "No Results",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|JSON Files (*.json)|*.json",
                DefaultExt = "csv",
                FileName = $"{(OperationMode == CleanupOperationMode.MoveToFolder ? "MoveAnalysis" : "DeleteAnalysis")}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    bool isMoveExport = OperationMode == CleanupOperationMode.MoveToFolder;

                    using var writer = new StreamWriter(dialog.FileName);
                    if (isMoveExport)
                        await writer.WriteLineAsync("Path,Type,Last Modified,Size,Action,Destination");
                    else
                        await writer.WriteLineAsync("Path,Type,Last Modified,Size,Action");

                    foreach (var result in AnalysisResults)
                    {
                        foreach (var item in result.Items)
                        {
                            var type = item.IsDirectory ? "Folder" : "File";
                            if (isMoveExport)
                            {
                                // DisplayMovedToPath covers both real moves (MovedToPath) and
                                // DryRun simulations (WouldMovedToPath).
                                await writer.WriteLineAsync(
                                    $"\"{item.Path}\",{type},{item.LastModified:yyyy-MM-dd HH:mm:ss},{item.SizeBytes},{item.MoveActionLabel},\"{item.DisplayMovedToPath ?? ""}\"");
                            }
                            else
                            {
                                await writer.WriteLineAsync(
                                    $"\"{item.Path}\",{type},{item.LastModified:yyyy-MM-dd HH:mm:ss},{item.SizeBytes},{item.DeleteActionLabel}");
                            }
                        }
                    }

                    StatusMessage = $"Results exported to {Path.GetFileName(dialog.FileName)}";
                    MessageBox.Show("Results exported successfully!", "Export Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting results:\n\n{ex.Message}",
                        "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }


        [RelayCommand]
        private async Task SaveConfiguration()
        {
            // Validate first
            if (!ValidateConfiguration())
                return;

            var configName = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter a name for this cleanup configuration:",
                "Save Cleanup Configuration",
                $"CleanupConfig_{DateTime.Now:yyyyMMdd}");

            if (string.IsNullOrWhiteSpace(configName))
                return;

            try
            {
                // In single move mode, build the TargetPaths from the dedicated fields.
                var targetPaths = (OperationMode == CleanupOperationMode.MoveToFolder && IsSingleMoveMode
                    && !string.IsNullOrWhiteSpace(SingleSourcePath))
                    ? new List<PathItem> { new PathItem(SingleSourcePath) { DestinationPath = SingleDestinationPath } }
                    : PathValidator.ParseCleanupPathsFromText(PathsText);

                var config = new CleanupConfiguration
                {
                    Name = configName,
                    TargetPaths = targetPaths,
                    Target = SelectedTarget,
                    DateMode = SelectedDateMode,
                    CutoffDate = CutoffDate,
                    CutoffTime = CutoffTime,
                    StartDate = StartDate,
                    EndDate = EndDate,
                    IncludeTime = IncludeTime,
                    StartTime = StartTime,
                    EndTime = EndTime,
                    IsRecursive = IsRecursive,
                    MaxDepth = string.IsNullOrWhiteSpace(MaxDepthText) ? null : int.TryParse(MaxDepthText, out var md1) ? md1 : (int?)null,
                    IncludeHiddenFiles = IncludeHiddenFiles,
                    DryRun = DryRun,
                    RequireConfirmation = RequireConfirmation,
                    MoveToRecycleBin = MoveToRecycleBin,
                    OperationMode = OperationMode,
                    IncludeFileTypes = ParseList(IncludeFileTypes),
                    ExcludeFileTypes = ParseList(ExcludeFileTypes),
                    ExcludePatterns = ParseList(ExcludePatterns)
                };

                await _historyService.SaveCleanupConfigurationAsync(config);
                await LoadSavedConfigurations();

                var configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FileAuditor", "CleanupConfigurations", $"{SanitizeFileName(configName)}.json");

                StatusMessage = $"Configuration '{configName}' saved";

                MessageBox.Show(
                    $"Configuration saved successfully!\n\n" +
                    $"Name: {configName}\n" +
                    $"Location: {configPath}\n\n" +
                    $"This configuration can be used with:\n" +
                    $"FileAuditor.CLI cleanup --config \"{configPath}\"",
                    "Configuration Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving configuration:\n\n{ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ValidateConfiguration()
        {
            var errors = new List<string>();

            if (OperationMode == CleanupOperationMode.MoveToFolder && IsSingleMoveMode)
            {
                if (string.IsNullOrWhiteSpace(SingleSourcePath))
                    errors.Add("Source path is required");
                if (string.IsNullOrWhiteSpace(SingleDestinationPath))
                    errors.Add("Destination path is required");
            }
            else if (string.IsNullOrWhiteSpace(PathsText))
            {
                errors.Add("At least one path must be specified");
            }
            else
            {
                var pathItems = PathValidator.ParseCleanupPathsFromText(PathsText);
                if (!pathItems.Any(p => p.IsValid))
                {
                    errors.Add("No valid paths found");
                }
            }

            switch (SelectedDateMode)
            {
                case CleanupDateMode.OlderThan:
                case CleanupDateMode.NewerThan:
                    if (!CutoffDate.HasValue)
                        errors.Add("Cutoff date is required for this mode");
                    break;

                case CleanupDateMode.DateRange:
                    if (!StartDate.HasValue || !EndDate.HasValue)
                        errors.Add("Start and End dates are required for Date Range mode");
                    else if (StartDate > EndDate)
                        errors.Add("Start date must be before or equal to End date");
                    break;

                case CleanupDateMode.ExactDate:
                    if (!StartDate.HasValue)
                        errors.Add("Date is required for Exact Date mode");
                    break;
            }

            if (errors.Any())
            {
                MessageBox.Show(
                    "Configuration validation failed:\n\n" + string.Join("\n", errors),
                    "Validation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private string SanitizeFileName(string fileName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        }

        [RelayCommand]
        private void LoadConfiguration()
        {
            if (SelectedConfiguration == null)
                return;

            // Reconstruct the two-column text format so DestinationPath round-trips correctly.
            PathsText = string.Join(Environment.NewLine,
                SelectedConfiguration.TargetPaths.Select(p =>
                    string.IsNullOrWhiteSpace(p.DestinationPath)
                        ? p.Path
                        : $"{p.Path},{p.DestinationPath}"));
            SelectedTarget = SelectedConfiguration.Target;
            SelectedDateMode = SelectedConfiguration.DateMode;

            CutoffDate = SelectedConfiguration.CutoffDate;
            if (SelectedConfiguration.CutoffTime.HasValue)
            {
                var (h, m, ap) = To12Hour(SelectedConfiguration.CutoffTime.Value);
                CutoffTimeHour = h;
                CutoffTimeMinute = m;
                CutoffTimeAmPm = ap;
                CutoffTime = SelectedConfiguration.CutoffTime.Value;
            }

            StartDate = SelectedConfiguration.StartDate;
            EndDate = SelectedConfiguration.EndDate;
            IncludeTime = SelectedConfiguration.IncludeTime;

            if (SelectedConfiguration.StartTime.HasValue)
            {
                var (h, m, ap) = To12Hour(SelectedConfiguration.StartTime.Value);
                StartTimeHour = h;
                StartTimeMinute = m;
                StartTimeAmPm = ap;
                StartTime = SelectedConfiguration.StartTime.Value;
            }

            if (SelectedConfiguration.EndTime.HasValue)
            {
                var (h, m, ap) = To12Hour(SelectedConfiguration.EndTime.Value);
                EndTimeHour = h;
                EndTimeMinute = m;
                EndTimeAmPm = ap;
                EndTime = SelectedConfiguration.EndTime.Value;
            }

            IsRecursive = SelectedConfiguration.IsRecursive;
            MaxDepthText = SelectedConfiguration.MaxDepth?.ToString() ?? string.Empty;
            IncludeHiddenFiles = SelectedConfiguration.IncludeHiddenFiles;
            DryRun = SelectedConfiguration.DryRun;
            RequireConfirmation = SelectedConfiguration.RequireConfirmation;
            MoveToRecycleBin = SelectedConfiguration.MoveToRecycleBin;
            // Always restore the tab's locked mode regardless of what was saved in the config.
            OperationMode = _initialMode;
            IncludeFileTypes = string.Join(", ", SelectedConfiguration.IncludeFileTypes);
            ExcludeFileTypes = string.Join(", ", SelectedConfiguration.ExcludeFileTypes);
            ExcludePatterns = string.Join("\n", SelectedConfiguration.ExcludePatterns);
            // Configs always use batch format (TargetPaths list), so switch to Batch input mode.
            MoveInputMode = MoveInputMode.Batch;

            StatusMessage = $"Loaded configuration '{SelectedConfiguration.Name}'";
        }


        private async Task LoadSavedConfigurations()
        {
            try
            {
                var configs = await _historyService.GetSavedCleanupConfigurationsAsync();
                SavedConfigurations.Clear();
                foreach (var config in configs)
                {
                    SavedConfigurations.Add(config);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading saved cleanup configurations");
            }
        }

        /// <summary>
        /// Builds a <see cref="CleanupConfiguration"/> from current ViewModel state.
        /// Pass pre-parsed <paramref name="pathItems"/> from <see cref="Analyze"/> to avoid
        /// parsing the text twice; when <c>null</c> the text is re-parsed internally (e.g. for SaveConfiguration).
        /// </summary>
        public CleanupConfiguration CreateConfiguration(List<PathItem>? pathItems = null)
        {
            // Always recompute TimeSpans from the 12-hour string fields immediately before
            // building the config.  This guarantees the correct 24-hour value is captured
            // even if a ComboBox selection fired a change notification that the source
            // generator hadn't yet propagated through the property-change chain.
            if (TryParse12HourTime(CutoffTimeHour, CutoffTimeMinute, CutoffTimeAmPm, out var cutoffTs))
                CutoffTime = cutoffTs;
            if (TryParse12HourTime(StartTimeHour, StartTimeMinute, StartTimeAmPm, out var startTs))
                StartTime = startTs;
            if (TryParse12HourTime(EndTimeHour, EndTimeMinute, EndTimeAmPm, out var endTs))
                EndTime = endTs;

            var config = new CleanupConfiguration
            {
                TargetPaths = pathItems ?? PathValidator.ParseCleanupPathsFromText(PathsText),
                Target = SelectedTarget,
                DateMode = SelectedDateMode,
                CutoffDate = CutoffDate,
                CutoffTime = CutoffTime,
                StartDate = StartDate,
                EndDate = EndDate,
                IncludeTime = IncludeTime,
                StartTime = StartTime,
                EndTime = EndTime,
                IsRecursive = IsRecursive,
                MaxDepth = string.IsNullOrWhiteSpace(MaxDepthText) ? null : int.TryParse(MaxDepthText, out var md2) ? md2 : (int?)null,
                IncludeHiddenFiles = IncludeHiddenFiles,
                DryRun = DryRun,
                RequireConfirmation = RequireConfirmation,
                MoveToRecycleBin = MoveToRecycleBin,
                OperationMode = OperationMode,
                IncludeFileTypes = ParseList(IncludeFileTypes),
                ExcludeFileTypes = ParseList(ExcludeFileTypes),
                ExcludePatterns = ParseList(ExcludePatterns)
            };

            return config;
        }


        private List<string> ParseList(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new List<string>();

            return input.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }

        private string FormatSize(long bytes)
        {
            const long KB = 1024;
            const long MB = KB * 1024;
            const long GB = MB * 1024;
            const long TB = GB * 1024;

            if (bytes >= TB)
                return $"{bytes / (double)TB:F2} TB";
            if (bytes >= GB)
                return $"{bytes / (double)GB:F2} GB";
            if (bytes >= MB)
                return $"{bytes / (double)MB:F2} MB";
            if (bytes >= KB)
                return $"{bytes / (double)KB:F2} KB";

            return $"{bytes} bytes";
        }
    }
}
