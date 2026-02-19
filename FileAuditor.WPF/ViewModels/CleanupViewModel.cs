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
        private DateTime? _startDate = null;

        [ObservableProperty]
        private DateTime? _endDate = null;

        [ObservableProperty]
        private bool _isRecursive = true;

        [ObservableProperty]
        private int? _maxDepth = null;

        [ObservableProperty]
        private bool _includeHiddenFiles = false;

        [ObservableProperty]
        private bool _dryRun = true;

        [ObservableProperty]
        private bool _requireConfirmation = true;

        [ObservableProperty]
        private bool _moveToRecycleBin = true;

        [ObservableProperty]
        private string _includeFileTypes = string.Empty;

        [ObservableProperty]
        private string _excludeFileTypes = string.Empty;

        [ObservableProperty]
        private string _excludePatterns = string.Empty;

        [ObservableProperty]
        private bool _isAnalyzing = false;

        [ObservableProperty]
        private bool _isDeleting = false;

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
        private TimeSpan _endTime = new TimeSpan(23, 59, 59);

        [ObservableProperty]
        private ObservableCollection<CleanupConfiguration> _savedConfigurations = new();

        [ObservableProperty]
        private CleanupConfiguration? _selectedConfiguration;




        // Helper properties for time input
        [ObservableProperty]
        private int _startTimeHour = 0;

        [ObservableProperty]
        private int _startTimeMinute = 0;

        [ObservableProperty]
        private int _endTimeHour = 23;

        [ObservableProperty]
        private int _endTimeMinute = 59;

        [ObservableProperty]
        private DateTime? _cutoffDate = DateTime.Now.AddDays(-30);

        [ObservableProperty]
        private int _cutoffTimeHour = 0;

        [ObservableProperty]
        private int _cutoffTimeMinute = 0;

        [ObservableProperty]
        private TimeSpan _cutoffTime = new TimeSpan(0, 0, 0);

        // Update cutoff time when hour/minute changes
        partial void OnCutoffTimeHourChanged(int value)
        {
            if (value >= 0 && value <= 23)
            {
                CutoffTime = new TimeSpan(value, CutoffTimeMinute, 0);
            }
        }

        partial void OnCutoffTimeMinuteChanged(int value)
        {
            if (value >= 0 && value <= 59)
            {
                CutoffTime = new TimeSpan(CutoffTimeHour, value, 0);
            }
        }

        // Update StartTime when hour/minute changes
        partial void OnStartTimeHourChanged(int value)
        {
            if (value >= 0 && value <= 23)
            {
                StartTime = new TimeSpan(value, StartTimeMinute, 0);
            }
        }

        partial void OnStartTimeMinuteChanged(int value)
        {
            if (value >= 0 && value <= 59)
            {
                StartTime = new TimeSpan(StartTimeHour, value, 0);
            }
        }

        partial void OnEndTimeHourChanged(int value)
        {
            if (value >= 0 && value <= 23)
            {
                EndTime = new TimeSpan(value, EndTimeMinute, 0);
            }
        }

        partial void OnEndTimeMinuteChanged(int value)
        {
            if (value >= 0 && value <= 59)
            {
                EndTime = new TimeSpan(EndTimeHour, value, 0);
            }
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

        // Add to constructor injection
        private readonly IScanHistoryService _historyService;
        private readonly IExportService _exportService;  // ADD THIS

        public CleanupViewModel(
            ICleanupService cleanupService,
            IScanHistoryService historyService,
            IExportService exportService,  // ADD THIS PARAMETER
            ILogger<CleanupViewModel> logger)
        {
            _cleanupService = cleanupService;
            _historyService = historyService;
            _exportService = exportService;  // ADD THIS
            _logger = logger;

            _ = LoadSavedConfigurations();
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
        private async Task ImportFromCsv()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog  // Use fully qualified name
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                Title = "Import Paths from CSV"
            };

            if (dialog.ShowDialog() == true)  // This works with Microsoft.Win32.OpenFileDialog
            {
                try
                {
                    var paths = await _exportService.ImportPathsFromCsvAsync(dialog.FileName);  // Fixed: _exportService

                    if (!string.IsNullOrWhiteSpace(PathsText))
                        PathsText += Environment.NewLine;

                    PathsText += string.Join(Environment.NewLine, paths.Select(p => p.Path));

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

            if (string.IsNullOrWhiteSpace(PathsText))
            {
                MessageBox.Show("Please enter at least one path to analyze.", "No Paths",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                IsAnalyzing = true;
                StatusMessage = "Analyzing paths...";
                AnalysisResults.Clear();
                _lastAnalysisConfig = null;

                var paths = PathsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct()
                    .ToList();

                // Snapshot config now so Execute uses exactly what was analyzed.
                var config = CreateConfiguration();
                _lastAnalysisConfig = config;

                _analyzeCancellationTokenSource = new CancellationTokenSource();

                var progress = new Progress<CleanupProgress>(p =>
                {
                    StatusMessage = p.CurrentOperation;
                });

                var results = await _cleanupService.AnalyzeMultiplePathsAsync(
                    paths,
                    config,
                    _analyzeCancellationTokenSource.Token,
                    progress);

                foreach (var result in results)
                {
                    AnalysisResults.Add(result);
                }

                var totalFiles = results.Sum(r => r.FilesIdentified);
                var totalFolders = results.Sum(r => r.FoldersIdentified);
                var totalSize = results.Sum(r => r.TotalSizeBytes);

                StatusMessage = $"Analysis complete: {totalFiles} files, {totalFolders} folders ({FormatSize(totalSize)}) identified";

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

            if (DryRun)
            {
                MessageBox.Show("Dry Run mode is enabled. No files will be deleted.\n\nDisable Dry Run to perform actual cleanup.",
                    "Dry Run Mode", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var totalFiles = AnalysisResults.Sum(r => r.FilesIdentified);
            var totalFolders = AnalysisResults.Sum(r => r.FoldersIdentified);
            var totalSize = AnalysisResults.Sum(r => r.TotalSizeBytes);

            var action = MoveToRecycleBin ? "move to recycle bin" : "permanently delete";
            var message = $"Are you sure you want to {action}:\n\n" +
                          $"• {totalFiles} file(s)\n" +
                          $"• {totalFolders} folder(s)\n" +
                          $"• Total size: {FormatSize(totalSize)}\n\n" +
                          $"This action cannot be easily undone!";

            if (RequireConfirmation)
            {
                var result = MessageBox.Show(message, "Confirm Cleanup",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                    return;
            }

            try
            {
                IsDeleting = true;
                StatusMessage = "Deleting items...";

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

                var deletedFiles = AnalysisResults.Sum(r => r.FilesDeleted);
                var deletedFolders = AnalysisResults.Sum(r => r.FoldersDeleted);

                // Auto-export to logs folder
                var logsFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FileAuditor", "Logs");

                Directory.CreateDirectory(logsFolder);

                var autoExportPath = Path.Combine(logsFolder,
                    $"cleanup_results_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                try
                {
                    using var writer = new StreamWriter(autoExportPath);
                    await writer.WriteLineAsync("Path,Type,Last Modified,Size (bytes),Status,Error");

                    foreach (var analysisResult in AnalysisResults)
                    {
                        foreach (var item in analysisResult.Items)
                        {
                            var type = item.IsDirectory ? "Folder" : "File";
                            var status = item.WasDeleted ? "Deleted" : "Identified";
                            var error = item.DeletionError ?? "";

                            await writer.WriteLineAsync(
                                $"\"{item.Path}\",{type},{item.LastModified:yyyy-MM-dd HH:mm:ss},{item.SizeBytes},{status},\"{error}\"");
                        }
                    }

                    StatusMessage = $"Cleanup complete: {deletedFiles} files, {deletedFolders} folders deleted";

                    MessageBox.Show($"Cleanup completed successfully!\n\n" +
                                   $"Deleted: {deletedFiles} files, {deletedFolders} folders\n\n" +
                                   $"Results exported to:\n{autoExportPath}\n\n" +
                                   $"Logs folder: {logsFolder}",
                        "Cleanup Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                    _logger.LogInformation("Cleanup completed. {Files} files, {Folders} folders deleted. Results: {Path}",
                        deletedFiles, deletedFolders, autoExportPath);
                }
                catch (Exception exportEx)
                {
                    _logger.LogError(exportEx, "Error auto-exporting cleanup results");

                    MessageBox.Show($"Cleanup completed successfully!\n\n" +
                                   $"Deleted: {deletedFiles} files, {deletedFolders} folders\n\n" +
                                   $"Warning: Could not export results:\n{exportEx.Message}",
                        "Cleanup Complete", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            //var dialog = new SaveFileDialog // commented out to avoid ambiguity
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|JSON Files (*.json)|*.json",
                DefaultExt = "csv",
                FileName = $"CleanupAnalysis_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    using var writer = new StreamWriter(dialog.FileName);
                    await writer.WriteLineAsync("Path,Type,Last Modified,Size,Status");

                    foreach (var result in AnalysisResults)
                    {
                        foreach (var item in result.Items)
                        {
                            var type = item.IsDirectory ? "Folder" : "File";
                            var status = item.WasDeleted ? "Deleted" : "Identified";
                            await writer.WriteLineAsync(
                                $"\"{item.Path}\",{type},{item.LastModified:yyyy-MM-dd HH:mm:ss},{item.SizeBytes},{status}");
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


        // Add these new commands
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
                var pathItems = PathValidator.ParsePathsFromText(PathsText);

                var config = new CleanupConfiguration
                {
                    Name = configName,
                    TargetPaths = pathItems,
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
                    MaxDepth = MaxDepth,
                    IncludeHiddenFiles = IncludeHiddenFiles,
                    DryRun = DryRun,
                    RequireConfirmation = RequireConfirmation,
                    MoveToRecycleBin = MoveToRecycleBin,
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

            if (string.IsNullOrWhiteSpace(PathsText))
            {
                errors.Add("At least one path must be specified");
            }
            else
            {
                var pathItems = PathValidator.ParsePathsFromText(PathsText);
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

            PathsText = string.Join(Environment.NewLine, SelectedConfiguration.TargetPaths.Select(p => p.Path));
            SelectedTarget = SelectedConfiguration.Target;
            SelectedDateMode = SelectedConfiguration.DateMode;

            CutoffDate = SelectedConfiguration.CutoffDate;
            if (SelectedConfiguration.CutoffTime.HasValue)
            {
                CutoffTimeHour = SelectedConfiguration.CutoffTime.Value.Hours;
                CutoffTimeMinute = SelectedConfiguration.CutoffTime.Value.Minutes;
                CutoffTime = SelectedConfiguration.CutoffTime.Value;
            }

            StartDate = SelectedConfiguration.StartDate;
            EndDate = SelectedConfiguration.EndDate;
            IncludeTime = SelectedConfiguration.IncludeTime;

            if (SelectedConfiguration.StartTime.HasValue)
            {
                StartTimeHour = SelectedConfiguration.StartTime.Value.Hours;
                StartTimeMinute = SelectedConfiguration.StartTime.Value.Minutes;
                StartTime = SelectedConfiguration.StartTime.Value;
            }

            if (SelectedConfiguration.EndTime.HasValue)
            {
                EndTimeHour = SelectedConfiguration.EndTime.Value.Hours;
                EndTimeMinute = SelectedConfiguration.EndTime.Value.Minutes;
                EndTime = SelectedConfiguration.EndTime.Value;
            }

            IsRecursive = SelectedConfiguration.IsRecursive;
            MaxDepth = SelectedConfiguration.MaxDepth;
            IncludeHiddenFiles = SelectedConfiguration.IncludeHiddenFiles;
            DryRun = SelectedConfiguration.DryRun;
            RequireConfirmation = SelectedConfiguration.RequireConfirmation;
            MoveToRecycleBin = SelectedConfiguration.MoveToRecycleBin;
            IncludeFileTypes = string.Join(", ", SelectedConfiguration.IncludeFileTypes);
            ExcludeFileTypes = string.Join(", ", SelectedConfiguration.ExcludeFileTypes);
            ExcludePatterns = string.Join("\n", SelectedConfiguration.ExcludePatterns);

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

        // Update CreateConfiguration to include new properties
        public CleanupConfiguration CreateConfiguration()
        {
            var config = new CleanupConfiguration
            {
                TargetPaths = PathValidator.ParsePathsFromText(PathsText),
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
                MaxDepth = MaxDepth,
                IncludeHiddenFiles = IncludeHiddenFiles,
                DryRun = DryRun,
                RequireConfirmation = RequireConfirmation,
                MoveToRecycleBin = MoveToRecycleBin,
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