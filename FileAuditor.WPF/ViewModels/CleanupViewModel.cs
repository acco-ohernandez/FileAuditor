using System.Collections.ObjectModel;
using System.IO;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using FileAuditor.Core.Enums;
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
        private CancellationTokenSource? _cancellationTokenSource;

        [ObservableProperty]
        private string _pathsText = string.Empty;

        [ObservableProperty]
        private CleanupTarget _selectedTarget = CleanupTarget.FilesOnly;

        [ObservableProperty]
        private CleanupDateMode _selectedDateMode = CleanupDateMode.OlderThan;

        [ObservableProperty]
        private int _daysOld = 30;

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

        public ObservableCollection<CleanupTarget> CleanupTargets { get; } = new()
        {
            CleanupTarget.FilesOnly,
            CleanupTarget.FoldersOnly,
            CleanupTarget.Both
        };

        public ObservableCollection<CleanupDateMode> DateModes { get; } = new()
        {
            CleanupDateMode.OlderThan,
            CleanupDateMode.NewerThan,
            CleanupDateMode.DateRange,
            CleanupDateMode.ExactDate
        };

        public CleanupViewModel(ICleanupService cleanupService, ILogger<CleanupViewModel> logger)
        {
            _cleanupService = cleanupService;
            _logger = logger;
        }

        [RelayCommand]
        private async Task BrowseFolder()
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

            await Task.CompletedTask;
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

                var paths = PathsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct()
                    .ToList();

                var config = CreateConfiguration();

                _cancellationTokenSource = new CancellationTokenSource();

                var progress = new Progress<CleanupProgress>(p =>
                {
                    StatusMessage = p.CurrentOperation;
                });

                var results = await _cleanupService.AnalyzeMultiplePathsAsync(
                    paths,
                    config,
                    _cancellationTokenSource.Token,
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
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        [RelayCommand]
        private async Task ExecuteCleanup()
        {
            if (!AnalysisResults.Any())
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

                var config = CreateConfiguration();
                _cancellationTokenSource = new CancellationTokenSource();

                var progress = new Progress<CleanupProgress>(p =>
                {
                    StatusMessage = p.CurrentOperation;
                });

                foreach (var analysisResult in AnalysisResults)
                {
                    await _cleanupService.ExecuteCleanupAsync(
                        analysisResult,
                        config,
                        _cancellationTokenSource.Token,
                        progress);
                }

                var deletedFiles = AnalysisResults.Sum(r => r.FilesDeleted);
                var deletedFolders = AnalysisResults.Sum(r => r.FoldersDeleted);

                StatusMessage = $"Cleanup complete: {deletedFiles} files, {deletedFolders} folders deleted";

                MessageBox.Show($"Cleanup completed successfully!\n\n" +
                               $"Deleted: {deletedFiles} files, {deletedFolders} folders",
                    "Cleanup Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                _logger.LogInformation("Cleanup completed. {Files} files, {Folders} folders deleted",
                    deletedFiles, deletedFolders);
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
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        [RelayCommand]
        private void Cancel()
        {
            _cancellationTokenSource?.Cancel();
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

        private CleanupConfiguration CreateConfiguration()
        {
            return new CleanupConfiguration
            {
                TargetPaths = PathsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList(),
                Target = SelectedTarget,
                DateMode = SelectedDateMode,
                DaysOld = DaysOld,
                StartDate = StartDate,
                EndDate = EndDate,
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