using System.Text.RegularExpressions;

using FileAuditor.Core.Enums;
using FileAuditor.Core.Helpers;
using FileAuditor.Core.Models;

using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;

namespace FileAuditor.Core.Services
{
    public class CleanupService : ICleanupService
    {
        private readonly ILogger<CleanupService>? _logger;

        public CleanupService(ILogger<CleanupService>? logger = null)
        {
            _logger = logger;
        }

        public async Task<List<CleanupResult>> AnalyzeMultiplePathsAsync(
            List<string> paths,
            CleanupConfiguration config,
            CancellationToken cancellationToken,
            IProgress<CleanupProgress>? progress = null)
        {
            var results = new List<CleanupResult>();

            foreach (var path in paths)
            {
                var result = await AnalyzeAsync(path, config, cancellationToken, progress);
                results.Add(result);
            }

            return results;
        }

        public async Task<CleanupResult> AnalyzeAsync(
            string path,
            CleanupConfiguration config,
            CancellationToken cancellationToken,
            IProgress<CleanupProgress>? progress = null)
        {
            var result = new CleanupResult
            {
                Path = path,
                StartTime = DateTime.Now,
                Status = CleanupStatus.Scanning,
                WasDryRun = config.DryRun
            };

            try
            {
                _logger?.LogInformation("Analyzing path for cleanup: {Path}", path);

                await Task.Run(() =>
                {
                    ScanForCleanup(path, config, result, 0, cancellationToken, progress);
                }, cancellationToken);

                result.Status = CleanupStatus.ReadyToDelete;
                _logger?.LogInformation("Analysis complete. Found {FileCount} files, {FolderCount} folders",
                    result.FilesIdentified, result.FoldersIdentified);
            }
            catch (OperationCanceledException)
            {
                result.Status = CleanupStatus.Cancelled;
                _logger?.LogWarning("Cleanup analysis cancelled");
            }
            catch (Exception ex)
            {
                result.Status = CleanupStatus.Failed;
                result.Errors.Add(new ScanError
                {
                    ErrorType = ErrorType.UnknownError,
                    Path = path,
                    Message = ex.Message,
                    Exception = ex
                });
                _logger?.LogError(ex, "Error analyzing path: {Path}", path);
            }
            finally
            {
                result.EndTime = DateTime.Now;
            }

            return result;
        }

        public async Task<CleanupResult> ExecuteCleanupAsync(
            CleanupResult analysisResult,
            CleanupConfiguration config,
            CancellationToken cancellationToken,
            IProgress<CleanupProgress>? progress = null)
        {
            if (config.DryRun)
            {
                _logger?.LogWarning("DryRun mode is enabled. No files will be deleted.");
                return analysisResult;
            }

            analysisResult.Status = CleanupStatus.Deleting;
            analysisResult.StartTime = DateTime.Now;

            try
            {
                await Task.Run(() =>
                {
                    DeleteItems(analysisResult, config, cancellationToken, progress);
                }, cancellationToken);

                analysisResult.Status = CleanupStatus.Completed;
                _logger?.LogInformation("Cleanup complete. Deleted {FileCount} files, {FolderCount} folders",
                    analysisResult.FilesDeleted, analysisResult.FoldersDeleted);
            }
            catch (OperationCanceledException)
            {
                analysisResult.Status = CleanupStatus.Cancelled;
                _logger?.LogWarning("Cleanup cancelled");
            }
            catch (Exception ex)
            {
                analysisResult.Status = CleanupStatus.Failed;
                analysisResult.Errors.Add(new ScanError
                {
                    ErrorType = ErrorType.UnknownError,
                    Path = analysisResult.Path,
                    Message = ex.Message,
                    Exception = ex
                });
                _logger?.LogError(ex, "Error during cleanup");
            }
            finally
            {
                analysisResult.EndTime = DateTime.Now;
            }

            return analysisResult;
        }

        private void ScanForCleanup(
            string path,
            CleanupConfiguration config,
            CleanupResult result,
            int currentDepth,
            CancellationToken cancellationToken,
            IProgress<CleanupProgress>? progress)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (config.MaxDepth.HasValue && currentDepth >= config.MaxDepth.Value)
                return;

            var enumOptions = FileSystemHelper.GetEnumerationOptions(config.IncludeHiddenFiles);

            try
            {
                // Scan files
                if (config.Target == CleanupTarget.FilesOnly || config.Target == CleanupTarget.Both)
                {
                    var files = Directory.EnumerateFiles(path, "*", enumOptions);

                    foreach (var file in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (ShouldCleanupItem(file, false, config))
                        {
                            var fileInfo = new FileInfo(file);
                            result.Items.Add(new CleanupItem
                            {
                                Path = file,
                                IsDirectory = false,
                                LastModified = fileInfo.LastWriteTime,
                                SizeBytes = fileInfo.Length
                            });
                            result.FilesIdentified++;
                            result.TotalSizeBytes += fileInfo.Length;
                        }
                    }
                }

                // Scan folders
                if (config.Target == CleanupTarget.FoldersOnly || config.Target == CleanupTarget.Both)
                {
                    var directories = Directory.EnumerateDirectories(path, "*", enumOptions).ToList();

                    foreach (var directory in directories)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (ShouldCleanupItem(directory, true, config))
                        {
                            var dirInfo = new DirectoryInfo(directory);
                            result.Items.Add(new CleanupItem
                            {
                                Path = directory,
                                IsDirectory = true,
                                LastModified = dirInfo.LastWriteTime,
                                SizeBytes = 0 // Calculating folder size is expensive
                            });
                            result.FoldersIdentified++;
                        }
                    }
                }

                // Recurse
                if (config.IsRecursive)
                {
                    var subdirectories = Directory.EnumerateDirectories(path, "*", enumOptions);

                    foreach (var subdirectory in subdirectories)
                    {
                        try
                        {
                            ScanForCleanup(subdirectory, config, result, currentDepth + 1, cancellationToken, progress);
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            result.Errors.Add(new ScanError
                            {
                                ErrorType = ErrorType.AccessDenied,
                                Path = subdirectory,
                                Message = "Access denied",
                                Exception = ex
                            });
                        }
                    }
                }

                progress?.Report(new CleanupProgress
                {
                    CurrentPath = path,
                    CurrentOperation = $"Scanned: {result.FilesIdentified} files, {result.FoldersIdentified} folders",
                    ItemsIdentified = result.FilesIdentified + result.FoldersIdentified
                });
            }
            catch (Exception ex)
            {
                result.Errors.Add(new ScanError
                {
                    ErrorType = ErrorType.UnknownError,
                    Path = path,
                    Message = ex.Message,
                    Exception = ex
                });
            }
        }

        private bool ShouldCleanupItem(string itemPath, bool isDirectory, CleanupConfiguration config)
        {
            try
            {
                // Check file type filters (for files only)
                if (!isDirectory)
                {
                    var extension = Path.GetExtension(itemPath).ToLower();

                    if (config.IncludeFileTypes.Any() && !config.IncludeFileTypes.Contains(extension))
                        return false;

                    if (config.ExcludeFileTypes.Contains(extension))
                        return false;
                }

                // Check exclusion patterns
                foreach (var pattern in config.ExcludePatterns)
                {
                    if (Regex.IsMatch(itemPath, pattern, RegexOptions.IgnoreCase))
                        return false;
                }

                // Check date criteria
                var lastModified = isDirectory
                    ? new DirectoryInfo(itemPath).LastWriteTime
                    : new FileInfo(itemPath).LastWriteTime;

                return MatchesDateCriteria(lastModified, config);
            }
            catch
            {
                return false;
            }
        }


        private bool MatchesDateCriteria(DateTime lastModified, CleanupConfiguration config)
        {
            // AnyDate mode - no filtering
            if (config.DateMode == CleanupDateMode.AnyDate)
                return true;

            var now = DateTime.Now;

            // Apply time component if enabled
            DateTime effectiveStartDate = config.StartDate ?? now;
            DateTime effectiveEndDate = config.EndDate ?? now;

            if (config.IncludeTime)
            {
                if (config.StartTime.HasValue)
                {
                    effectiveStartDate = effectiveStartDate.Date.Add(config.StartTime.Value);
                }
                if (config.EndTime.HasValue)
                {
                    effectiveEndDate = effectiveEndDate.Date.Add(config.EndTime.Value);
                }
            }

            switch (config.DateMode)
            {
                case CleanupDateMode.OlderThan:
                    return (now - lastModified).TotalDays > config.DaysOld;

                case CleanupDateMode.NewerThan:
                    return (now - lastModified).TotalDays < config.DaysOld;

                case CleanupDateMode.DateRange:
                    if (!config.StartDate.HasValue || !config.EndDate.HasValue)
                        return false;

                    if (config.IncludeTime)
                    {
                        return lastModified >= effectiveStartDate && lastModified <= effectiveEndDate;
                    }
                    else
                    {
                        return lastModified.Date >= effectiveStartDate.Date &&
                               lastModified.Date <= effectiveEndDate.Date;
                    }

                case CleanupDateMode.ExactDate:
                    if (!config.StartDate.HasValue)
                        return false;

                    if (config.IncludeTime)
                    {
                        // Match exact date and time (within same hour/minute)
                        return lastModified.Date == effectiveStartDate.Date &&
                               lastModified.Hour == effectiveStartDate.Hour &&
                               lastModified.Minute == effectiveStartDate.Minute;
                    }
                    else
                    {
                        return lastModified.Date == effectiveStartDate.Date;
                    }

                default:
                    return false;
            }
        }


        private void DeleteItems(
            CleanupResult result,
            CleanupConfiguration config,
            CancellationToken cancellationToken,
            IProgress<CleanupProgress>? progress)
        {
            int processed = 0;
            int total = result.Items.Count;

            foreach (var item in result.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (item.IsDirectory)
                    {
                        DeleteDirectory(item, config);
                        result.FoldersDeleted++;
                    }
                    else
                    {
                        DeleteFile(item, config);
                        result.FilesDeleted++;
                    }

                    item.WasDeleted = true;
                    _logger?.LogInformation("Deleted: {Path}", item.Path);
                }
                catch (Exception ex)
                {
                    item.DeletionError = ex.Message;
                    result.Errors.Add(new ScanError
                    {
                        ErrorType = ErrorType.UnknownError,
                        Path = item.Path,
                        Message = ex.Message,
                        Exception = ex
                    });
                    _logger?.LogError(ex, "Failed to delete: {Path}", item.Path);
                }

                processed++;
                progress?.Report(new CleanupProgress
                {
                    CurrentPath = item.Path,
                    CurrentOperation = $"Deleting... {processed}/{total}",
                    PercentComplete = (int)((processed / (double)total) * 100),
                    ItemsProcessed = processed
                });
            }
        }

        private void DeleteFile(CleanupItem item, CleanupConfiguration config)
        {
            if (config.MoveToRecycleBin)
            {
                FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            else
            {
                File.Delete(item.Path);
            }
        }

        private void DeleteDirectory(CleanupItem item, CleanupConfiguration config)
        {
            if (config.MoveToRecycleBin)
            {
                FileSystem.DeleteDirectory(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            else
            {
                Directory.Delete(item.Path, recursive: true);
            }
        }
    }
}