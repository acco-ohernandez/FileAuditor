using System.Collections.Concurrent;

using FileAuditor.Core.Enums;
using FileAuditor.Core.Helpers;
using FileAuditor.Core.Models;

using Microsoft.Extensions.Logging;

namespace FileAuditor.Core.Services
{
    public class FileScanner : IFileScanner
    {
        private readonly IBoxDriveHandler _boxDriveHandler;
        private readonly ILogger<FileScanner>? _logger;

        public FileScanner(IBoxDriveHandler boxDriveHandler, ILogger<FileScanner>? logger = null)
        {
            _boxDriveHandler = boxDriveHandler;
            _logger = logger;
        }

        public async Task<List<ScanResult>> ScanMultiplePathsAsync(
            List<PathItem> paths,
            ScanConfiguration config,
            CancellationToken cancellationToken,
            IProgress<ScanProgress>? progress = null)
        {
            var results = new ConcurrentBag<ScanResult>();
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = config.ParallelThreadCount,
                CancellationToken = cancellationToken
            };

            try
            {
                await Parallel.ForEachAsync(paths, options, async (pathItem, ct) =>
                {
                    var result = await ScanPathAsync(pathItem, config, ct, progress);
                    results.Add(result);
                });
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning("Scan operation was cancelled");
            }

            return results.OrderBy(r => r.Path).ToList();
        }

        public async Task<ScanResult> ScanPathAsync(
            PathItem pathItem,
            ScanConfiguration config,
            CancellationToken cancellationToken,
            IProgress<ScanProgress>? progress = null)
        {
            var result = new ScanResult
            {
                Path = pathItem.Path,
                StartTime = DateTime.Now,
                Status = ScanStatus.Running,
                IsBoxDrivePath = pathItem.IsBoxDrivePath,
                IsNetworkPath = pathItem.IsNetworkPath,
                CountMode = config.CountMode,
                WasRecursive = config.IsRecursive,
                MaxDepthUsed = config.MaxDepth
            };

            if (config.EnableFileTypeBreakdown && config.CountMode != CountMode.FoldersOnly)
            {
                result.FileTypeBreakdown = new FileTypeBreakdown();
            }

            try
            {
                // Validate path
                if (!pathItem.IsValid)
                {
                    result.Status = ScanStatus.Failed;
                    result.AddError(new ScanError
                    {
                        ErrorType = ErrorType.InvalidPath,
                        Path = pathItem.Path,
                        Message = pathItem.ValidationError ?? "Invalid path"
                    });
                    result.EndTime = DateTime.Now;
                    return result;
                }

                // Check network path availability
                if (pathItem.IsNetworkPath && !PathValidator.IsNetworkPathAvailable(pathItem.Path))
                {
                    result.Status = ScanStatus.Failed;
                    result.AddError(new ScanError
                    {
                        ErrorType = ErrorType.NetworkPathOffline,
                        Path = pathItem.Path,
                        Message = "Network path is not available"
                    });
                    result.EndTime = DateTime.Now;
                    return result;
                }

                // Handle Box Drive refresh
                if (pathItem.IsBoxDrivePath && config.BoxDriveRefreshEnabled)
                {
                    _logger?.LogInformation("Refreshing Box Drive path: {Path}", pathItem.Path);

                    var refreshed = await _boxDriveHandler.RefreshDirectoryAsync(
                        pathItem.Path,
                        config.BoxDriveRefreshTimeoutSeconds,
                        cancellationToken);

                    if (!refreshed)
                    {
                        result.AddError(new ScanError
                        {
                            ErrorType = ErrorType.BoxDriveError,
                            Path = pathItem.Path,
                            Message = "Box Drive refresh timed out or failed"
                        });
                    }
                }

                // Perform the actual scan
                await ScanDirectoryRecursiveAsync(
                    pathItem.Path,
                    config,
                    result,
                    0,
                    cancellationToken,
                    progress);

                result.Status = ScanStatus.Completed;
            }
            catch (OperationCanceledException)
            {
                result.Status = ScanStatus.Cancelled;
                result.WasCancelled = true;
                _logger?.LogWarning("Scan cancelled for path: {Path}", pathItem.Path);
            }
            catch (UnauthorizedAccessException ex)
            {
                result.Status = ScanStatus.Failed;
                result.AddError(new ScanError
                {
                    ErrorType = ErrorType.AccessDenied,
                    Path = pathItem.Path,
                    Message = "Access denied",
                    Exception = ex
                });
                _logger?.LogError(ex, "Access denied for path: {Path}", pathItem.Path);
            }
            catch (Exception ex)
            {
                result.Status = ScanStatus.Failed;
                result.AddError(new ScanError
                {
                    ErrorType = ErrorType.UnknownError,
                    Path = pathItem.Path,
                    Message = ex.Message,
                    Exception = ex
                });
                _logger?.LogError(ex, "Error scanning path: {Path}", pathItem.Path);
            }
            finally
            {
                result.EndTime = DateTime.Now;
            }

            return result;
        }

        private async Task ScanDirectoryRecursiveAsync(
            string path,
            ScanConfiguration config,
            ScanResult result,
            int currentDepth,
            CancellationToken cancellationToken,
            IProgress<ScanProgress>? progress)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check depth limit
            if (config.MaxDepth.HasValue && currentDepth >= config.MaxDepth.Value)
                return;

            var enumOptions = FileSystemHelper.GetEnumerationOptions(config.IncludeHiddenFiles);

            try
            {
                // Enumerate subdirectories once and reuse for both counting and recursion (M-2 fix).
                // A single enumeration avoids double filesystem I/O and ensures the folder count
                // and the recursion list are consistent.
                List<string>? subdirectories = null;

                bool needSubdirs = config.IsRecursive
                    || config.CountMode == CountMode.FoldersOnly
                    || config.CountMode == CountMode.Both;

                if (needSubdirs)
                {
                    // Note: Directory.EnumerateDirectories is synchronous I/O. Task.Run offloads it
                    // to a thread pool thread to keep the calling context free (intentional pattern).
                    subdirectories = await Task.Run(
                        () => Directory.EnumerateDirectories(path, "*", enumOptions).ToList(),
                        cancellationToken);
                }

                // Count files if needed
                if (config.CountMode == CountMode.FilesOnly || config.CountMode == CountMode.Both)
                {
                    await Task.Run(() =>
                    {
                        // Note: Directory.EnumerateFiles is synchronous I/O offloaded via Task.Run
                        // to avoid blocking the UI/calling thread (intentional — not true async I/O).
                        var files = Directory.EnumerateFiles(path, "*", enumOptions);

                        foreach (var file in files)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            // Apply file type filters
                            var extension = Path.GetExtension(file).ToLower();

                            if (config.IncludeFileTypes.Any() && !config.IncludeFileTypes.Contains(extension))
                                continue;

                            if (config.ExcludeFileTypes.Contains(extension))
                                continue;

                            // Thread-safe increment via Interlocked (C-1 fix)
                            result.IncrementFiles();

                            // Thread-safe AddFileType via ConcurrentDictionary (C-2 fix)
                            result.FileTypeBreakdown?.AddFileType(extension);

                            // Add file size atomically
                            try
                            {
                                var fileInfo = new FileInfo(file);
                                result.AddBytes(fileInfo.Length);
                            }
                            catch
                            {
                                // Ignore errors getting file size
                            }
                        }
                    }, cancellationToken);
                }

                // Count folders using the already-enumerated list (avoids second enumeration)
                if (config.CountMode == CountMode.FoldersOnly || config.CountMode == CountMode.Both)
                {
                    if (subdirectories != null)
                    {
                        foreach (var _ in subdirectories)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            result.IncrementFolders();
                        }
                    }
                }

                // Report progress
                progress?.Report(new ScanProgress
                {
                    Path = result.Path,
                    CurrentOperation = $"Scanning: {path}",
                    FoldersProcessed = result.TotalFolders,
                    FilesProcessed = result.TotalFiles
                });

                // Recurse into subdirectories if enabled
                if (config.IsRecursive && subdirectories != null)
                {
                    foreach (var subdirectory in subdirectories)
                    {
                        try
                        {
                            await ScanDirectoryRecursiveAsync(
                                subdirectory,
                                config,
                                result,
                                currentDepth + 1,
                                cancellationToken,
                                progress);
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            result.AddError(new ScanError
                            {
                                ErrorType = ErrorType.AccessDenied,
                                Path = subdirectory,
                                Message = "Access denied to subdirectory",
                                Exception = ex
                            });
                        }
                        catch (DirectoryNotFoundException ex)
                        {
                            result.AddError(new ScanError
                            {
                                ErrorType = ErrorType.PathNotFound,
                                Path = subdirectory,
                                Message = "Directory not found (may have been deleted)",
                                Exception = ex
                            });
                        }
                        catch (Exception ex)
                        {
                            result.AddError(new ScanError
                            {
                                ErrorType = ErrorType.UnknownError,
                                Path = subdirectory,
                                Message = ex.Message,
                                Exception = ex
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.AddError(new ScanError
                {
                    ErrorType = ErrorType.UnknownError,
                    Path = path,
                    Message = ex.Message,
                    Exception = ex
                });
            }
        }
    }
}
