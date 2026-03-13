using System.Collections.ObjectModel;
using System.IO;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using FileAuditor.Core.Enums;
using FileAuditor.Core.Helpers;
using FileAuditor.Core.Models;
using FileAuditor.Core.Services;

using Microsoft.Extensions.Logging;
// Aliases to avoid ambiguity with System.Windows.Forms
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace FileAuditor.WPF.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IFileScanner _fileScanner;
        private readonly IExportService _exportService;
        private readonly IScanHistoryService _historyService;
        private readonly ILogger<MainViewModel> _logger;

        private CancellationTokenSource? _scanCancellationTokenSource;

        [ObservableProperty]
        private string _pathsText = string.Empty;

        [ObservableProperty]
        private ObservableCollection<ScanResult> _scanResults = new();

        [ObservableProperty]
        private ScanResult? _selectedScanResult;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotScanning))]
        private bool _isScanning = false;

        public bool IsNotScanning => !IsScanning;

        [ObservableProperty]
        private bool _isRecursive = true;

        [ObservableProperty]
        private string _maxDepthText = "1";

        [ObservableProperty]
        private int _parallelThreadCount = 4;

        [ObservableProperty]
        private bool _includeHiddenFiles = false;

        [ObservableProperty]
        private bool _boxDriveRefreshEnabled = true;

        [ObservableProperty]
        private int _boxDriveRefreshTimeout = 30;

        [ObservableProperty]
        private CountMode _selectedCountMode = CountMode.Both;

        [ObservableProperty]
        private bool _enableFileTypeBreakdown = true;

        [ObservableProperty]
        private string _includeFileTypes = string.Empty;

        [ObservableProperty]
        private string _excludeFileTypes = string.Empty;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private int _progressPercentage = 0;

        [ObservableProperty]
        private ObservableCollection<ScanConfiguration> _savedConfigurations = new();

        [ObservableProperty]
        private ScanConfiguration? _selectedConfiguration;

        [ObservableProperty]
        private string _outputFormat = "csv";

        [ObservableProperty]
        private string _outputPath = "scan_results_{timestamp}.csv";

        [ObservableProperty]
        private bool _enableHistory = true;

        [ObservableProperty]
        private bool _enableComparison = false;

        [ObservableProperty]
        private int _comparisonRetentionDays = 90;

        public ObservableCollection<string> OutputFormats { get; } = new()
{
    "csv",
    "json"
};

        public ObservableCollection<CountMode> CountModes { get; } = new()
        {
            CountMode.Both,
            CountMode.FoldersOnly,
            CountMode.FilesOnly
        };

        public MainViewModel(
            IFileScanner fileScanner,
            IExportService exportService,
            IScanHistoryService historyService,
            ILogger<MainViewModel> logger)
        {
            _fileScanner = fileScanner;
            _exportService = exportService;
            _historyService = historyService;
            _logger = logger;

            _ = LoadSavedConfigurations(); // Fire-and-forget with discard
        }

        [RelayCommand]
        private async Task StartScan()
        {
            if (IsScanning)
                return;

            if (string.IsNullOrWhiteSpace(PathsText))
            {
                MessageBox.Show("Please enter at least one path to scan.", "No Paths",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                IsScanning = true;
                StatusMessage = "Validating paths...";
                ScanResults.Clear();

                // Parse and validate paths
                var pathItems = PathValidator.ParsePathsFromText(PathsText);

                if (!pathItems.Any())
                {
                    MessageBox.Show("No valid paths found.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Validate all paths
                pathItems = PathValidator.ValidateMultiplePaths(pathItems.Select(p => p.Path));

                var invalidPaths = pathItems.Where(p => !p.IsValid).ToList();
                if (invalidPaths.Any())
                {
                    var message = $"Found {invalidPaths.Count} invalid path(s):\n\n" +
                                  string.Join("\n", invalidPaths.Select(p => $"- {p.Path}: {p.ValidationError}"));

                    var result = MessageBox.Show(
                        $"{message}\n\nDo you want to continue with valid paths only?",
                        "Invalid Paths Found",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.No)
                        return;

                    pathItems = pathItems.Where(p => p.IsValid).ToList();
                }

                // Create scan configuration
                var config = new ScanConfiguration
                {
                    Paths = pathItems,
                    CountMode = SelectedCountMode,
                    IsRecursive = IsRecursive,
                    MaxDepth = string.IsNullOrWhiteSpace(MaxDepthText) ? null : int.TryParse(MaxDepthText, out var md) ? md : (int?)null,
                    ParallelThreadCount = ParallelThreadCount,
                    IncludeHiddenFiles = IncludeHiddenFiles,
                    BoxDriveRefreshEnabled = BoxDriveRefreshEnabled,
                    BoxDriveRefreshTimeoutSeconds = BoxDriveRefreshTimeout,
                    EnableFileTypeBreakdown = EnableFileTypeBreakdown,
                    IncludeFileTypes = ParseFileTypes(IncludeFileTypes),
                    ExcludeFileTypes = ParseFileTypes(ExcludeFileTypes)
                };

                // Create cancellation token
                _scanCancellationTokenSource = new CancellationTokenSource();

                StatusMessage = $"Scanning {pathItems.Count} path(s)...";

                // Perform scan
                var progress = new Progress<ScanProgress>(p =>
                {
                    StatusMessage = p.CurrentOperation;
                    ProgressPercentage = p.PercentComplete;
                });

                var results = await _fileScanner.ScanMultiplePathsAsync(
                    pathItems,
                    config,
                    _scanCancellationTokenSource.Token,
                    progress);

                // Add results to collection
                foreach (var result in results)
                {
                    ScanResults.Add(result);

                    // Save to history
                    await _historyService.SaveScanResultAsync(result);
                }

                var completedCount = results.Count(r => r.Status == ScanStatus.Completed);
                StatusMessage = $"Scan completed: {completedCount}/{results.Count} paths successful";

                _logger.LogInformation("Scan completed. {Completed}/{Total} paths successful",
                    completedCount, results.Count);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Scan cancelled by user";
                _logger.LogWarning("Scan cancelled by user");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"An error occurred during scanning:\n\n{ex.Message}",
                    "Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _logger.LogError(ex, "Error during scan operation");
            }
            finally
            {
                IsScanning = false;
                ProgressPercentage = 0;
                _scanCancellationTokenSource?.Dispose();
                _scanCancellationTokenSource = null;
            }
        }

        [RelayCommand]
        private void CancelScan()
        {
            _scanCancellationTokenSource?.Cancel();
            StatusMessage = "Cancelling scan...";
        }

        [RelayCommand]
        private void ClearResults()
        {
            ScanResults.Clear();
            StatusMessage = "Results cleared";
        }

        /// <summary>
        /// Resets every field on the File Counter tab back to its default state.
        /// Blocked while a scan is in progress.
        /// </summary>
        [RelayCommand]
        private void ClearAll()
        {
            if (IsScanning)
            {
                MessageBox.Show("A scan is currently in progress. Please cancel it before clearing.",
                    "Scan In Progress", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PathsText = string.Empty;
            ScanResults.Clear();
            SelectedScanResult = null;
            SelectedCountMode = CountMode.Both;
            IsRecursive = true;
            MaxDepthText = "1";
            ParallelThreadCount = 4;
            IncludeHiddenFiles = false;
            BoxDriveRefreshEnabled = true;
            BoxDriveRefreshTimeout = 30;
            EnableFileTypeBreakdown = true;
            IncludeFileTypes = string.Empty;
            ExcludeFileTypes = string.Empty;
            SelectedConfiguration = null;
            StatusMessage = "Ready";
            ProgressPercentage = 0;
        }

        [RelayCommand]
        private void BrowseFolder()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select a folder to scan",
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
            var dialog = new OpenFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                Title = "Import Paths from CSV"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var paths = await _exportService.ImportPathsFromCsvAsync(dialog.FileName);
                    PathsText = string.Join(Environment.NewLine, paths.Select(p => p.Path));
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
        private async Task ExportToCsv()
        {
            if (!ScanResults.Any())
            {
                MessageBox.Show("No results to export.", "No Results",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                DefaultExt = "csv",
                FileName = $"ScanResults_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    await _exportService.ExportToCsvAsync(ScanResults.ToList(), dialog.FileName);
                    StatusMessage = $"Exported to {Path.GetFileName(dialog.FileName)}";
                    MessageBox.Show("Results exported successfully!", "Export Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting to CSV:\n\n{ex.Message}",
                        "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private async Task ExportToJson()
        {
            if (!ScanResults.Any())
            {
                MessageBox.Show("No results to export.", "No Results",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json",
                DefaultExt = "json",
                FileName = $"ScanResults_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    await _exportService.ExportToJsonAsync(ScanResults.ToList(), dialog.FileName);
                    StatusMessage = $"Exported to {Path.GetFileName(dialog.FileName)}";
                    MessageBox.Show("Results exported successfully!", "Export Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting to JSON:\n\n{ex.Message}",
                        "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private async Task SaveConfiguration()
        {
            var configName = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter a name for this configuration:",
                "Save Configuration",
                $"Config_{DateTime.Now:yyyyMMdd}");

            if (string.IsNullOrWhiteSpace(configName))
                return;

            try
            {
                // Parse paths from text
                var pathItems = PathValidator.ParsePathsFromText(PathsText);

                var config = new ScanConfiguration
                {
                    Name = configName,
                    Paths = pathItems,  // Add the parsed paths
                    CountMode = SelectedCountMode,
                    IsRecursive = IsRecursive,
                    MaxDepth = string.IsNullOrWhiteSpace(MaxDepthText) ? null : int.TryParse(MaxDepthText, out var md) ? md : (int?)null,
                    ParallelThreadCount = ParallelThreadCount,
                    IncludeHiddenFiles = IncludeHiddenFiles,
                    BoxDriveRefreshEnabled = BoxDriveRefreshEnabled,
                    BoxDriveRefreshTimeoutSeconds = BoxDriveRefreshTimeout,
                    EnableFileTypeBreakdown = EnableFileTypeBreakdown,
                    IncludeFileTypes = ParseFileTypes(IncludeFileTypes),
                    ExcludeFileTypes = ParseFileTypes(ExcludeFileTypes),
                    OutputFormat = OutputFormat,
                    OutputPath = OutputPath,
                    EnableHistory = EnableHistory,
                    EnableComparison = EnableComparison,
                    ComparisonRetentionDays = ComparisonRetentionDays
                };

                await _historyService.SaveConfigurationAsync(config);
                await LoadSavedConfigurations();
                StatusMessage = $"Configuration '{configName}' saved";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving configuration:\n\n{ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void LoadConfiguration()
        {
            if (SelectedConfiguration == null)
                return;

            // Load paths into text box
            PathsText = string.Join(Environment.NewLine, SelectedConfiguration.Paths.Select(p => p.Path));

            SelectedCountMode = SelectedConfiguration.CountMode;
            IsRecursive = SelectedConfiguration.IsRecursive;
            MaxDepthText = SelectedConfiguration.MaxDepth?.ToString() ?? string.Empty;
            ParallelThreadCount = SelectedConfiguration.ParallelThreadCount;
            IncludeHiddenFiles = SelectedConfiguration.IncludeHiddenFiles;
            BoxDriveRefreshEnabled = SelectedConfiguration.BoxDriveRefreshEnabled;
            BoxDriveRefreshTimeout = SelectedConfiguration.BoxDriveRefreshTimeoutSeconds;
            EnableFileTypeBreakdown = SelectedConfiguration.EnableFileTypeBreakdown;
            IncludeFileTypes = string.Join(", ", SelectedConfiguration.IncludeFileTypes);
            ExcludeFileTypes = string.Join(", ", SelectedConfiguration.ExcludeFileTypes);
            OutputFormat = SelectedConfiguration.OutputFormat;
            OutputPath = SelectedConfiguration.OutputPath;
            EnableHistory = SelectedConfiguration.EnableHistory;
            EnableComparison = SelectedConfiguration.EnableComparison;
            ComparisonRetentionDays = SelectedConfiguration.ComparisonRetentionDays;

            StatusMessage = $"Loaded configuration '{SelectedConfiguration.Name}'";
        }

        private async Task LoadSavedConfigurations()
        {
            try
            {
                var configs = await _historyService.GetSavedConfigurationsAsync();
                SavedConfigurations.Clear();
                foreach (var config in configs)
                {
                    SavedConfigurations.Add(config);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading saved configurations");
            }
        }

        private List<string> ParseFileTypes(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new List<string>();

            return input.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => (s.StartsWith(".") ? s : "." + s).ToLowerInvariant())
                .ToList();
        }
    }
}