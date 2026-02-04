using System.Text.Json;

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
            // Configure Serilog
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
                // Simple argument parsing
                if (args.Length == 0)
                {
                    ShowUsage();
                    return 0;
                }

                string? configPath = null;
                string? outputPath = null;
                bool verbose = false;

                // Parse arguments
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i].ToLower())
                    {
                        case "--config":
                        case "-c":
                            if (i + 1 < args.Length)
                                configPath = args[++i];
                            break;
                        case "--output":
                        case "-o":
                            if (i + 1 < args.Length)
                                outputPath = args[++i];
                            break;
                        case "--verbose":
                        case "-v":
                            verbose = true;
                            break;
                        case "--help":
                        case "-h":
                        case "/?":
                            ShowUsage();
                            return 0;
                        default:
                            // Treat as config path if no option specified
                            if (string.IsNullOrEmpty(configPath))
                                configPath = args[i];
                            break;
                    }
                }

                if (string.IsNullOrEmpty(configPath))
                {
                    Log.Error("No configuration file specified");
                    ShowUsage();
                    return 1;
                }

                return await RunScanAsync(new FileInfo(configPath), outputPath, verbose);
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

        static void ShowUsage()
        {
            Console.WriteLine("File Auditor CLI - Scan file systems and generate reports");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  FileAuditor.CLI [options] <config-file>");
            Console.WriteLine();
            Console.WriteLine("Arguments:");
            Console.WriteLine("  <config-file>              Path to the JSON configuration file");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -c, --config <file>        Path to the JSON configuration file");
            Console.WriteLine("  -o, --output <file>        Override output file path");
            Console.WriteLine("  -v, --verbose              Enable verbose logging");
            Console.WriteLine("  -h, --help                 Show help information");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  FileAuditor.CLI config.json");
            Console.WriteLine("  FileAuditor.CLI --config config.json --verbose");
            Console.WriteLine("  FileAuditor.CLI -c config.json -o custom-output.csv");
        }

        static async Task<int> RunScanAsync(FileInfo configFile, string? outputPath, bool verbose)
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
                    return 1;
                }

                var configJson = await File.ReadAllTextAsync(configFile.FullName);

                // Deserialize to ScanConfiguration (same as WPF)
                var config = JsonSerializer.Deserialize<ScanConfiguration>(configJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (config == null)
                {
                    Log.Error("Failed to parse configuration file");
                    return 1;
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
                var pathItems = PathValidator.ValidateMultiplePaths(config.Paths.Select(p => p.Path));
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
                    return 1;
                }

                // Update config with validated paths
                config.Paths = pathItems;

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
                    config,
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
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occurred during scan execution");
                return 1;
            }
        }
    }
}