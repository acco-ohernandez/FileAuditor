using System.CommandLine;
using System.Text.Json;

using FileAuditor.CLI.Models;
using FileAuditor.Core.Enums;
using FileAuditor.Core.Helpers;
using FileAuditor.Core.Models;
using FileAuditor.Core.Services;

using Microsoft.Extensions.Logging;

using Serilog;

namespace FileAuditor.CLI
{
    class Program
    {

        static async Task<int> Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File(
                    path: Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "FileAuditor", "Logs", "cli-log-.txt"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7)
                .CreateLogger();

            try
            {
                var rootCommand = new RootCommand("File Auditor CLI - Scan file systems and generate reports");

                // Argument (positional)
                var configArgument = new Argument<FileInfo>("config")
                {
                    Description = "Path to the JSON configuration file"
                };
                rootCommand.Arguments.Add(configArgument);

                // Options (aliases come from constructor in 2.0.2)
                var outputOption = new Option<string?>("--output", "-o")
                {
                    Description = "Override output file path"
                };
                rootCommand.Options.Add(outputOption);

                var verboseOption = new Option<bool>("--verbose", "-v")
                {
                    Description = "Enable verbose logging"
                };
                rootCommand.Options.Add(verboseOption);

                // Action/handler (2.0.2 uses SetAction + ParseResult)
                rootCommand.SetAction(async (parseResult, cancellationToken) =>
                {
                    var config = parseResult.GetValue(configArgument);
                    var output = parseResult.GetValue(outputOption);
                    var verbose = parseResult.GetValue(verboseOption);

                    if (config is null)
                    {
                        Console.Error.WriteLine("Missing required argument: config");
                        return 1;
                    }

                    await RunScanAsync(config, output, verbose);
                    return 0;
                });

                // Invoke (2.0.2 pattern)
                return rootCommand.Parse(args).Invoke();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly");
                return 1;
            }
            finally
            {
                await Log.CloseAndFlushAsync();
            }
        }

        static async Task RunScanAsync(FileInfo configFile, string? outputPath, bool verbose)
        {
            try
            {
                if (verbose)
                {
                    Log.Logger = new LoggerConfiguration()
                        .MinimumLevel.Debug()
                        .WriteTo.Console()
                        .WriteTo.File(
                            path: Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "FileAuditor", "Logs", "cli-log-.txt"),
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 7)
                        .CreateLogger();
                }

                Log.Information("File Auditor CLI Starting...");
                Log.Information("Config file: {ConfigFile}", configFile.FullName);

                // Read configuration
                if (!configFile.Exists)
                {
                    Log.Error("Configuration file not found: {ConfigFile}", configFile.FullName);
                    return;
                }

                var configJson = await File.ReadAllTextAsync(configFile.FullName);
                var config = JsonSerializer.Deserialize<CliConfiguration>(configJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (config == null)
                {
                    Log.Error("Failed to parse configuration file");
                    return;
                }

                Log.Information("Configuration loaded successfully");
                Log.Information("Paths to scan: {PathCount}", config.Paths.Count);

                // Create services
                using var loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder.AddSerilog(dispose: true);
                });

                var boxDriveHandler = new BoxDriveHandler(loggerFactory.CreateLogger<BoxDriveHandler>());
                var fileScanner = new FileScanner(boxDriveHandler, loggerFactory.CreateLogger<FileScanner>());
                var exportService = new ExportService(loggerFactory.CreateLogger<ExportService>());
                var historyService = new ScanHistoryService(loggerFactory.CreateLogger<ScanHistoryService>());

                // Validate paths
                var pathItems = PathValidator.ValidateMultiplePaths(config.Paths);
                var invalidPaths = pathItems.Where(p => !p.IsValid).ToList();

                if (invalidPaths.Any())
                {
                    Log.Warning("Found {Count} invalid path(s):", invalidPaths.Count);
                    foreach (var invalidPath in invalidPaths)
                    {
                        Log.Warning("  - {Path}: {Error}", invalidPath.Path, invalidPath.ValidationError);
                    }
                }

                pathItems = pathItems.Where(p => p.IsValid).ToList();

                if (!pathItems.Any())
                {
                    Log.Error("No valid paths to scan");
                    return;
                }

                // Parse count mode
                var countMode = config.CountMode.ToLower() switch
                {
                    "foldersonly" => CountMode.FoldersOnly,
                    "filesonly" => CountMode.FilesOnly,
                    _ => CountMode.Both
                };

                // Create scan configuration
                var scanConfig = new ScanConfiguration
                {
                    Paths = pathItems,
                    CountMode = countMode,
                    IsRecursive = config.Recursive,
                    MaxDepth = config.MaxDepth,
                    ParallelThreadCount = config.ParallelThreads,
                    IncludeHiddenFiles = config.IncludeHiddenFiles,
                    BoxDriveRefreshEnabled = config.BoxDriveRefreshEnabled,
                    BoxDriveRefreshTimeoutSeconds = config.BoxDriveRefreshTimeoutSeconds,
                    EnableFileTypeBreakdown = config.EnableFileTypeBreakdown,
                    IncludeFileTypes = config.IncludeFileTypes,
                    ExcludeFileTypes = config.ExcludeFileTypes
                };

                // Execute scan
                Log.Information("Starting scan of {Count} path(s)...", pathItems.Count);

                var progress = new Progress<ScanProgress>(p =>
                {
                    if (verbose)
                    {
                        Log.Debug("Progress: {Operation}", p.CurrentOperation);
                    }
                });

                var results = await fileScanner.ScanMultiplePathsAsync(
                    pathItems,
                    scanConfig,
                    CancellationToken.None,
                    progress);

                Log.Information("Scan completed");

                // Display results summary
                var completed = results.Count(r => r.Status == ScanStatus.Completed);
                var failed = results.Count(r => r.Status == ScanStatus.Failed);

                Log.Information("Results: {Completed} completed, {Failed} failed", completed, failed);

                foreach (var result in results)
                {
                    if (result.Status == ScanStatus.Completed)
                    {
                        Log.Information("  ✓ {Path}: {Folders} folders, {Files} files, {Size}",
                            result.Path,
                            result.TotalFolders,
                            result.TotalFiles,
                            result.GetSizeFormatted());
                    }
                    else
                    {
                        Log.Error("  ✗ {Path}: {Status}",
                            result.Path,
                            result.GetStatusDescription());

                        if (result.HasErrors)
                        {
                            foreach (var error in result.Errors.Take(3))
                            {
                                Log.Error("      - {ErrorType}: {Message}",
                                    error.ErrorType,
                                    error.Message);
                            }
                        }
                    }
                }

                // Save to history if enabled
                if (config.EnableHistory)
                {
                    Log.Information("Saving results to history...");
                    foreach (var result in results)
                    {
                        await historyService.SaveScanResultAsync(result);
                    }

                    // Clean up old scans
                    await historyService.DeleteOldScansAsync(config.ComparisonRetentionDays);
                }

                // Export results
                var finalOutputPath = outputPath ?? config.OutputPath;
                finalOutputPath = finalOutputPath.Replace("{timestamp}", DateTime.Now.ToString("yyyyMMdd_HHmmss"));

                // Ensure output directory exists
                var outputDir = Path.GetDirectoryName(finalOutputPath);
                if (!string.IsNullOrEmpty(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                Log.Information("Exporting results to: {OutputPath}", finalOutputPath);

                if (config.OutputFormat.ToLower() == "json")
                {
                    await exportService.ExportToJsonAsync(results, finalOutputPath);
                }
                else
                {
                    await exportService.ExportToCsvAsync(results, finalOutputPath);
                }

                Log.Information("Export completed successfully");
                Log.Information("File Auditor CLI completed successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occurred during scan execution");
                throw;
            }
        }
    }
}