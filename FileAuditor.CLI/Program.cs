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
                if (args.Length == 0)
                {
                    ShowUsage();
                    return 0;
                }

                // Determine command
                var command = args[0].ToLower();

                switch (command)
                {
                    case "scan":
                        return await ExecuteScanCommand(args.Skip(1).ToArray());

                    case "cleanup":
                        return await ExecuteCleanupCommand(args.Skip(1).ToArray());

                    case "help":
                    case "--help":
                    case "-h":
                    case "/?":
                        ShowHelp(args.Length > 1 ? args[1] : null);
                        return 0;

                    case "version":
                    case "--version":
                    case "-v":
                        ShowVersion();
                        return 0;

                    default:
                        // Try to parse as scan command for backwards compatibility
                        return await ExecuteScanCommand(args);
                }
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

        static async Task<int> ExecuteScanCommand(string[] args)
        {
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
                        verbose = true;
                        break;
                    default:
                        if (string.IsNullOrEmpty(configPath))
                            configPath = args[i];
                        break;
                }
            }

            if (string.IsNullOrEmpty(configPath))
            {
                Log.Error("No configuration file specified");
                ShowHelp("scan");
                return 1;
            }

            return await RunScanAsync(new FileInfo(configPath), outputPath, verbose);
        }

        static async Task<int> ExecuteCleanupCommand(string[] args)
        {
            string? configPath = null;
            string? outputPath = null;
            bool verbose = false;
            bool? dryRun = null; // null means use config setting

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
                        verbose = true;
                        break;
                    case "--dry-run":
                        dryRun = true;
                        break;
                    case "--execute":
                        dryRun = false;
                        break;
                    default:
                        if (string.IsNullOrEmpty(configPath))
                            configPath = args[i];
                        break;
                }
            }

            if (string.IsNullOrEmpty(configPath))
            {
                Log.Error("No configuration file specified");
                ShowHelp("cleanup");
                return 1;
            }

            return await RunCleanupAsync(new FileInfo(configPath), outputPath, verbose, dryRun);
        }

        static async Task<int> RunCleanupAsync(FileInfo configFile, string? outputPath, bool verbose, bool? dryRunOverride)
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

                Log.Information("File Auditor CLI - Cleanup Mode");
                Log.Information("Config file: {ConfigFile}", configFile.FullName);

                if (!configFile.Exists)
                {
                    Log.Error("Configuration file not found: {ConfigFile}", configFile.FullName);
                    return 1;
                }

                var configJson = await File.ReadAllTextAsync(configFile.FullName);
                var config = JsonSerializer.Deserialize<CleanupConfiguration>(configJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (config == null)
                {
                    Log.Error("Failed to parse configuration file");
                    return 1;
                }

                // Override dry run if specified
                if (dryRunOverride.HasValue)
                {
                    config.DryRun = dryRunOverride.Value;
                }

                Log.Information("Configuration loaded successfully");
                Log.Information("Paths to clean: {PathCount}", config.TargetPaths.Count);
                Log.Information("Dry Run Mode: {DryRun}", config.DryRun);

                if (config.DryRun)
                {
                    Log.Warning("⚠️  DRY RUN MODE - No files will be deleted");
                }
                else
                {
                    Log.Warning("⚠️  EXECUTE MODE - Files WILL BE DELETED!");
                }

                // Create services
                using var loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder.AddSerilog(dispose: true);
                });

                var cleanupService = new CleanupService(loggerFactory.CreateLogger<CleanupService>());
                var exportService = new ExportService(loggerFactory.CreateLogger<ExportService>());

                // Validate paths
                var validPaths = config.TargetPaths.Where(p => p.IsValid).ToList();

                if (!validPaths.Any())
                {
                    Log.Error("No valid paths to clean");
                    return 1;
                }

                // Phase 1: Analyze
                Log.Information("Phase 1: Analyzing paths...");

                var progress = new Progress<CleanupProgress>(p =>
                {
                    if (verbose)
                    {
                        Log.Debug("Progress: {Operation}", p.CurrentOperation);
                    }
                });

                var results = new List<CleanupResult>();

                foreach (var pathItem in validPaths)
                {
                    var result = await cleanupService.AnalyzeAsync(
                        pathItem.Path,
                        config,
                        CancellationToken.None,
                        progress);

                    results.Add(result);
                }

                // Display analysis results
                var totalFiles = results.Sum(r => r.FilesIdentified);
                var totalFolders = results.Sum(r => r.FoldersIdentified);
                var totalSize = results.Sum(r => r.TotalSizeBytes);

                Log.Information("Analysis complete:");
                Log.Information("  Files identified: {Files:N0}", totalFiles);
                Log.Information("  Folders identified: {Folders:N0}", totalFolders);
                Log.Information("  Total size: {Size}", FormatSize(totalSize));

                foreach (var result in results)
                {
                    if (result.Status == CleanupStatus.ReadyToDelete)
                    {
                        Log.Information("  ✓ {Path}: {Files} files, {Folders} folders",
                            result.Path,
                            result.FilesIdentified,
                            result.FoldersIdentified);
                    }
                    else
                    {
                        Log.Error("  ✗ {Path}: {Status}",
                            result.Path,
                            result.Status);

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

                // Phase 2: Execute cleanup (if not dry run)
                if (!config.DryRun)
                {
                    Log.Information("Phase 2: Executing cleanup...");

                    // Confirmation check
                    if (config.RequireConfirmation)
                    {
                        Log.Warning("About to delete {Files} files and {Folders} folders. Type 'YES' to confirm:",
                            totalFiles, totalFolders);

                        var confirmation = Console.ReadLine();
                        if (confirmation?.Trim().ToUpper() != "YES")
                        {
                            Log.Information("Cleanup cancelled by user");
                            return 0;
                        }
                    }

                    foreach (var result in results)
                    {
                        await cleanupService.ExecuteCleanupAsync(
                            result,
                            config,
                            CancellationToken.None,
                            progress);
                    }

                    var deletedFiles = results.Sum(r => r.FilesDeleted);
                    var deletedFolders = results.Sum(r => r.FoldersDeleted);

                    Log.Information("Cleanup complete:");
                    Log.Information("  Files deleted: {Files:N0}", deletedFiles);
                    Log.Information("  Folders deleted: {Folders:N0}", deletedFolders);
                }

                // Export results
                var finalOutputPath = outputPath ?? $"cleanup_results_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                finalOutputPath = finalOutputPath.Replace("{timestamp}", DateTime.Now.ToString("yyyyMMdd_HHmmss"));

                var outputDir = Path.GetDirectoryName(finalOutputPath);
                if (!string.IsNullOrEmpty(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                Log.Information("Exporting results to: {OutputPath}", finalOutputPath);

                // Export to CSV
                using var writer = new StreamWriter(finalOutputPath);
                await writer.WriteLineAsync("Path,Type,Last Modified,Size (bytes),Status,Error");

                foreach (var result in results)
                {
                    foreach (var item in result.Items)
                    {
                        var type = item.IsDirectory ? "Folder" : "File";
                        var status = item.WasDeleted ? "Deleted" : "Identified";
                        var error = item.DeletionError ?? "";

                        await writer.WriteLineAsync(
                            $"\"{item.Path}\",{type},{item.LastModified:yyyy-MM-dd HH:mm:ss},{item.SizeBytes},{status},\"{error}\"");
                    }
                }

                Log.Information("Export completed successfully");
                Log.Information("File Auditor CLI completed successfully");
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occurred during cleanup");
                return 1;
            }
        }

        static async Task<int> RunScanAsync(FileInfo configFile, string? outputPath, bool verbose)
        {
            // Keep existing scan implementation...
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

        static void ShowUsage()
        {
            Console.WriteLine("File Auditor CLI");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  FileAuditor.CLI <command> [options]");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  scan             Scan file systems and generate reports");
            Console.WriteLine("  cleanup          Clean up old files and folders");
            Console.WriteLine("  help [command]   Show detailed help for a command");
            Console.WriteLine("  version          Show version information");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  FileAuditor.CLI scan --config scan-config.json");
            Console.WriteLine("  FileAuditor.CLI cleanup --config cleanup-config.json --dry-run");
            Console.WriteLine("  FileAuditor.CLI help cleanup");
            Console.WriteLine();
            Console.WriteLine("For detailed help on a specific command, use:");
            Console.WriteLine("  FileAuditor.CLI help <command>");
        }

        static void ShowHelp(string? command)
        {
            if (string.IsNullOrEmpty(command))
            {
                ShowUsage();
                return;
            }

            switch (command.ToLower())
            {
                case "scan":
                    ShowScanHelp();
                    break;
                case "cleanup":
                    ShowCleanupHelp();
                    break;
                default:
                    Console.WriteLine($"Unknown command: {command}");
                    ShowUsage();
                    break;
            }
        }

        static void ShowScanHelp()
        {
            Console.WriteLine("File Auditor CLI - Scan Command");
            Console.WriteLine();
            Console.WriteLine("DESCRIPTION:");
            Console.WriteLine("  Scans file systems (local, network, Box Drive) and generates detailed reports");
            Console.WriteLine("  about folder/file counts, sizes, and file type breakdowns.");
            Console.WriteLine();
            Console.WriteLine("USAGE:");
            Console.WriteLine("  FileAuditor.CLI scan [options] <config-file>");
            Console.WriteLine("  FileAuditor.CLI scan --config <config-file> [options]");
            Console.WriteLine();
            Console.WriteLine("OPTIONS:");
            Console.WriteLine("  -c, --config <file>    Path to the JSON configuration file (required)");
            Console.WriteLine("  -o, --output <file>    Override output file path");
            Console.WriteLine("  --verbose              Enable verbose logging");
            Console.WriteLine();
            Console.WriteLine("CONFIGURATION FILE:");
            Console.WriteLine("  The configuration file is a JSON file that defines:");
            Console.WriteLine("  • Paths to scan (local drives, network shares, Box Drive)");
            Console.WriteLine("  • Count mode (files only, folders only, or both)");
            Console.WriteLine("  • Recursive scanning with optional depth limit");
            Console.WriteLine("  • Parallel processing settings");
            Console.WriteLine("  • File type filters (include/exclude)");
            Console.WriteLine("  • Box Drive refresh options");
            Console.WriteLine("  • Output format and path");
            Console.WriteLine("  • History and comparison settings");
            Console.WriteLine();
            Console.WriteLine("  You can create and export configurations using the File Auditor WPF application.");
            Console.WriteLine();
            Console.WriteLine("EXAMPLES:");
            Console.WriteLine("  # Basic scan");
            Console.WriteLine("  FileAuditor.CLI scan --config weekly-scan.json");
            Console.WriteLine();
            Console.WriteLine("  # Scan with custom output and verbose logging");
            Console.WriteLine("  FileAuditor.CLI scan -c scan.json -o results.csv --verbose");
            Console.WriteLine();
            Console.WriteLine("  # For use in scheduled tasks (Windows Task Scheduler)");
            Console.WriteLine("  FileAuditor.CLI scan C:\\Configs\\monthly-audit.json");
            Console.WriteLine();
            Console.WriteLine("CONFIGURATION LOCATION:");
            Console.WriteLine("  WPF-created configs: %LocalAppData%\\FileAuditor\\Configurations\\");
            Console.WriteLine();
            Console.WriteLine("OUTPUT:");
            Console.WriteLine("  • CSV or JSON file with scan results");
            Console.WriteLine("  • Log files in: %LocalAppData%\\FileAuditor\\Logs\\");
            Console.WriteLine("  • Scan history (if enabled) in: %LocalAppData%\\FileAuditor\\History\\");
        }

        static void ShowCleanupHelp()
        {
            Console.WriteLine("File Auditor CLI - Cleanup Command");
            Console.WriteLine();
            Console.WriteLine("DESCRIPTION:");
            Console.WriteLine("  Identifies and optionally deletes old files and folders based on date criteria.");
            Console.WriteLine("  Supports multiple date modes, file type filtering, and safety features.");
            Console.WriteLine();
            Console.WriteLine("USAGE:");
            Console.WriteLine("  FileAuditor.CLI cleanup [options] <config-file>");
            Console.WriteLine("  FileAuditor.CLI cleanup --config <config-file> [options]");
            Console.WriteLine();
            Console.WriteLine("OPTIONS:");
            Console.WriteLine("  -c, --config <file>    Path to the JSON cleanup configuration file (required)");
            Console.WriteLine("  -o, --output <file>    Override output file path");
            Console.WriteLine("  --verbose              Enable verbose logging");
            Console.WriteLine("  --dry-run              Preview only, do not delete (overrides config)");
            Console.WriteLine("  --execute              Execute deletion (overrides config dry-run setting)");
            Console.WriteLine();
            Console.WriteLine("CONFIGURATION FILE:");
            Console.WriteLine("  The cleanup configuration file defines:");
            Console.WriteLine("  • Target paths to clean");
            Console.WriteLine("  • What to clean (files only, folders only, or both)");
            Console.WriteLine("  • Date criteria:");
            Console.WriteLine("    - AnyDate: No date filtering");
            Console.WriteLine("    - OlderThan: Files/folders older than X days");
            Console.WriteLine("    - NewerThan: Files/folders newer than X days");
            Console.WriteLine("    - DateRange: Files/folders within a specific date range");
            Console.WriteLine("    - ExactDate: Files/folders from a specific date");
            Console.WriteLine("  • File type filters (include/exclude)");
            Console.WriteLine("  • Exclusion patterns (regex)");
            Console.WriteLine("  • Safety options:");
            Console.WriteLine("    - Dry run mode (preview only)");
            Console.WriteLine("    - Confirmation required");
            Console.WriteLine("    - Move to Recycle Bin vs. permanent delete");
            Console.WriteLine();
            Console.WriteLine("  You can create and export cleanup configurations using the File Auditor WPF");
            Console.WriteLine("  application's Cleanup tab.");
            Console.WriteLine();
            Console.WriteLine("SAFETY FEATURES:");
            Console.WriteLine("  ⚠️  IMPORTANT: Always test with --dry-run first!");
            Console.WriteLine();
            Console.WriteLine("  • Dry run mode: Preview what will be deleted without actually deleting");
            Console.WriteLine("  • Confirmation: Requires typing 'YES' before deletion");
            Console.WriteLine("  • Recycle Bin: Moves items to Recycle Bin instead of permanent deletion");
            Console.WriteLine("  • Detailed logging: All operations are logged");
            Console.WriteLine("  • Result export: CSV report of all identified/deleted items");
            Console.WriteLine();
            Console.WriteLine("EXAMPLES:");
            Console.WriteLine("  # Preview cleanup (dry run) - RECOMMENDED FIRST STEP");
            Console.WriteLine("  FileAuditor.CLI cleanup --config old-files-cleanup.json --dry-run");
            Console.WriteLine();
            Console.WriteLine("  # Execute cleanup after reviewing dry run results");
            Console.WriteLine("  FileAuditor.CLI cleanup --config old-files-cleanup.json --execute");
            Console.WriteLine();
            Console.WriteLine("  # Cleanup with verbose logging");
            Console.WriteLine("  FileAuditor.CLI cleanup -c cleanup.json --verbose --execute");
            Console.WriteLine();
            Console.WriteLine("  # Scheduled cleanup (use absolute paths in Task Scheduler)");
            Console.WriteLine("  FileAuditor.CLI cleanup C:\\Configs\\weekly-temp-cleanup.json");
            Console.WriteLine();
            Console.WriteLine("WORKFLOW:");
            Console.WriteLine("  1. Create cleanup configuration in WPF app");
            Console.WriteLine("  2. Export configuration to known location");
            Console.WriteLine("  3. Test with: FileAuditor.CLI cleanup config.json --dry-run");
            Console.WriteLine("  4. Review the output CSV to verify what will be deleted");
            Console.WriteLine("  5. Execute: FileAuditor.CLI cleanup config.json --execute");
            Console.WriteLine("  6. (Optional) Schedule in Windows Task Scheduler");
            Console.WriteLine();
            Console.WriteLine("CONFIGURATION LOCATION:");
            Console.WriteLine("  WPF-created configs: %LocalAppData%\\FileAuditor\\CleanupConfigurations\\");
            Console.WriteLine();
            Console.WriteLine("OUTPUT:");
            Console.WriteLine("  • CSV file with list of identified/deleted items");
            Console.WriteLine("  • Log files in: %LocalAppData%\\FileAuditor\\Logs\\");
        }

        static void ShowVersion()
        {
            Console.WriteLine("File Auditor CLI");
            Console.WriteLine("Version 1.0.0");
            Console.WriteLine();
            Console.WriteLine("A comprehensive tool for file system auditing and cleanup.");
            Console.WriteLine("Supports local drives, network shares, and Box Drive.");
            Console.WriteLine();
            Console.WriteLine("Copyright (c) 2026");
            Console.WriteLine("Built with .NET 8");
        }

        static string FormatSize(long bytes)
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