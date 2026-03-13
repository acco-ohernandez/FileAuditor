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

                result.Status = config.OperationMode == CleanupOperationMode.MoveToFolder
                    ? CleanupStatus.ReadyToMove
                    : CleanupStatus.ReadyToDelete;
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

            analysisResult.StartTime = DateTime.Now;

            try
            {
                if (config.OperationMode == CleanupOperationMode.MoveToFolder)
                {
                    analysisResult.Status = CleanupStatus.Moving;
                    await Task.Run(() =>
                    {
                        MoveItems(analysisResult, config, cancellationToken, progress);
                    }, cancellationToken);

                    analysisResult.Status = CleanupStatus.Completed;
                    _logger?.LogInformation("Move complete. Moved {FileCount} files, {FolderCount} folders",
                        analysisResult.FilesMoved, analysisResult.FoldersMoved);
                }
                else
                {
                    analysisResult.Status = CleanupStatus.Deleting;
                    await Task.Run(() =>
                    {
                        DeleteItems(analysisResult, config, cancellationToken, progress);
                    }, cancellationToken);

                    analysisResult.Status = CleanupStatus.Completed;
                    _logger?.LogInformation("Cleanup complete. Deleted {FileCount} files, {FolderCount} folders",
                        analysisResult.FilesDeleted, analysisResult.FoldersDeleted);
                }
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

            // MaxDepth=1 means "1 level deep from the root" (root + direct subdirs).
            // Using strict greater-than so MaxDepth=0 = root only, MaxDepth=1 = root + 1 sub-level.
            if (config.MaxDepth.HasValue && currentDepth > config.MaxDepth.Value)
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
                        // OperationCanceledException is intentionally not caught here —
                        // it propagates up through the call stack so AnalyzeAsync can
                        // set CleanupStatus.Cancelled and stop the scan immediately.
                    }
                }

                progress?.Report(new CleanupProgress
                {
                    CurrentPath = path,
                    CurrentOperation = $"Scanned: {result.FilesIdentified} files, {result.FoldersIdentified} folders",
                    ItemsIdentified = result.FilesIdentified + result.FoldersIdentified
                });
            }
            catch (OperationCanceledException)
            {
                // Re-throw so AnalyzeAsync's catch (OperationCanceledException) handles it
                // correctly, sets CleanupStatus.Cancelled, and stops the scan.
                // Without this re-throw the cancellation would be swallowed by catch (Exception),
                // recorded as an UnknownError, and the analysis would incorrectly appear to succeed.
                throw;
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

        // Case-insensitive, dot-normalizing extension matcher. Entries may arrive with or without
        // a leading dot (e.g. "txt", ".txt", ".TXT") — both sides are normalised to lowercase+dot.
        private static bool ExtensionMatches(string entry, string ext) =>
            (entry.StartsWith('.') ? entry : "." + entry).ToLowerInvariant() == ext;

        private bool ShouldCleanupItem(string itemPath, bool isDirectory, CleanupConfiguration config)
        {
            try
            {
                // Check file type filters (for files only, case-insensitive, dot-normalised)
                if (!isDirectory)
                {
                    var extension = Path.GetExtension(itemPath).ToLowerInvariant();

                    if (config.IncludeFileTypes.Any() && !config.IncludeFileTypes.Any(ft => ExtensionMatches(ft, extension)))
                        return false;

                    if (config.ExcludeFileTypes.Any(ft => ExtensionMatches(ft, extension)))
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

            switch (config.DateMode)
            {
                case CleanupDateMode.OlderThan:
                {
                    if (!config.CutoffDate.HasValue)
                        return false;

                    var cutoff = config.CutoffDate.Value.Date;
                    if (config.CutoffTime.HasValue)
                        cutoff = cutoff.Add(config.CutoffTime.Value);

                    return lastModified < cutoff;
                }

                case CleanupDateMode.NewerThan:
                {
                    if (!config.CutoffDate.HasValue)
                        return false;

                    var cutoff = config.CutoffDate.Value.Date;
                    if (config.CutoffTime.HasValue)
                        cutoff = cutoff.Add(config.CutoffTime.Value);

                    return lastModified > cutoff;
                }

                case CleanupDateMode.DateRange:
                {
                    if (!config.StartDate.HasValue || !config.EndDate.HasValue)
                        return false;

                    var effectiveStart = config.StartDate.Value.Date;
                    var effectiveEnd   = config.EndDate.Value.Date;

                    if (config.IncludeTime)
                    {
                        if (config.StartTime.HasValue)
                            effectiveStart = effectiveStart.Add(config.StartTime.Value);
                        if (config.EndTime.HasValue)
                            effectiveEnd = effectiveEnd.Add(config.EndTime.Value);

                        return lastModified >= effectiveStart && lastModified <= effectiveEnd;
                    }
                    else
                    {
                        return lastModified.Date >= effectiveStart && lastModified.Date <= effectiveEnd;
                    }
                }

                case CleanupDateMode.ExactDate:
                {
                    if (!config.StartDate.HasValue)
                        return false;

                    var exactDate = config.StartDate.Value.Date;

                    if (config.IncludeTime)
                    {
                        if (config.StartTime.HasValue)
                            exactDate = exactDate.Add(config.StartTime.Value);

                        // Match to the minute — seconds are not surfaced in the UI.
                        return lastModified.Date == exactDate.Date &&
                               lastModified.Hour == exactDate.Hour &&
                               lastModified.Minute == exactDate.Minute;
                    }
                    else
                    {
                        return lastModified.Date == exactDate.Date;
                    }
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
            // Sort deepest paths first so children are always deleted before their parent
            // directory. Without this, a recursive folder delete removes its contents, and
            // the individually-listed child items later fail with "Could not find".
            var orderedItems = result.Items
                .OrderByDescending(item =>
                    item.Path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
                .ToList();

            int processed = 0;
            int total = orderedItems.Count;

            foreach (var item in orderedItems)
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

        private void MoveItems(
            CleanupResult result,
            CleanupConfiguration config,
            CancellationToken cancellationToken,
            IProgress<CleanupProgress>? progress)
        {
            if (string.IsNullOrWhiteSpace(result.DestinationPath))
            {
                result.Errors.Add(new ScanError
                {
                    ErrorType = ErrorType.UnknownError,
                    Path = result.Path,
                    Message = "No destination path specified for this source. Cannot move items."
                });
                return;
            }

            // Ensure the destination root exists before processing items.
            Directory.CreateDirectory(result.DestinationPath);

            // Guard: if the destination is a subfolder of the source, items already
            // inside the destination would be moved into themselves. Skip them so that
            // a second Analyze+Execute cycle after a previous run doesn't corrupt data.
            bool destInsideSource = IsSubPath(result.Path, result.DestinationPath);

            // Sort deepest paths first so children are always moved before their parent
            // directory. Without this, MoveDirectoryRecursive moves all contents of a
            // folder, and then individually-listed child items later fail with "source not found".
            var orderedItems = result.Items
                .OrderByDescending(item =>
                    item.Path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
                .ToList();

            int processed = 0;
            int total = orderedItems.Count;

            foreach (var item in orderedItems)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (destInsideSource && IsSubPath(result.DestinationPath, item.Path))
                {
                    _logger?.LogWarning("Skipping {Path} — already inside destination folder {Dest}", item.Path, result.DestinationPath);
                    processed++;
                    continue;
                }

                try
                {
                    string destPath;
                    if (item.IsDirectory)
                    {
                        destPath = MoveDirectory(item, result.Path, result.DestinationPath);
                        result.FoldersMoved++;
                    }
                    else
                    {
                        destPath = MoveFile(item, result.Path, result.DestinationPath);
                        result.FilesMoved++;
                    }

                    item.WasMoved = true;
                    item.MovedToPath = destPath;
                    _logger?.LogInformation("Moved: {Path} -> {Dest}", item.Path, destPath);
                }
                catch (Exception ex)
                {
                    item.MoveError = ex.Message;
                    result.Errors.Add(new ScanError
                    {
                        ErrorType = ErrorType.UnknownError,
                        Path = item.Path,
                        Message = ex.Message,
                        Exception = ex
                    });
                    _logger?.LogError(ex, "Failed to move: {Path}", item.Path);
                }

                processed++;
                progress?.Report(new CleanupProgress
                {
                    CurrentPath = item.Path,
                    CurrentOperation = $"Moving... {processed}/{total}",
                    PercentComplete = (int)((processed / (double)total) * 100),
                    ItemsProcessed = processed
                });
            }
        }

        private string MoveFile(CleanupItem item, string sourceRoot, string destRoot)
        {
            string relativePath = Path.GetRelativePath(sourceRoot, item.Path);
            string destPath = Path.Combine(destRoot, relativePath);
            string? destDir = Path.GetDirectoryName(destPath);

            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            File.Move(item.Path, destPath, overwrite: true);
            return destPath;
        }

        private string MoveDirectory(CleanupItem item, string sourceRoot, string destRoot)
        {
            string relativePath = Path.GetRelativePath(sourceRoot, item.Path);
            string destPath = Path.Combine(destRoot, relativePath);

            MoveDirectoryRecursive(item.Path, destPath);
            return destPath;
        }

        private void MoveDirectoryRecursive(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.EnumerateFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Move(file, destFile, overwrite: true);
            }

            foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
                MoveDirectoryRecursive(subDir, destSubDir);
            }

            // Delete the now-empty source directory
            Directory.Delete(sourceDir, recursive: true);
        }

        // Returns true if child is the same path as parent or is nested inside it.
        private static bool IsSubPath(string parent, string child)
        {
            var parentFull = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var childFull  = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return childFull.StartsWith(parentFull, StringComparison.OrdinalIgnoreCase);
        }
    }
}