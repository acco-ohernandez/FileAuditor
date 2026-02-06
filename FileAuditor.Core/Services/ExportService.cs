using System.Globalization;
using System.Text.Json;

using CsvHelper;
using CsvHelper.Configuration;

using FileAuditor.Core.Models;

using Microsoft.Extensions.Logging;

namespace FileAuditor.Core.Services
{
    public class ExportService : IExportService
    {
        private readonly ILogger<ExportService>? _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public ExportService(ILogger<ExportService>? logger = null)
        {
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        public async Task ExportToCsvAsync(List<ScanResult> results, string filePath)
        {
            try
            {
                _logger?.LogInformation("Exporting {Count} scan results to CSV: {FilePath}", results.Count, filePath);

                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                };

                using var writer = new StreamWriter(filePath);
                using var csv = new CsvWriter(writer, config);

                // Write headers
                csv.WriteField("Path");
                csv.WriteField("Status");
                csv.WriteField("Total Folders");
                csv.WriteField("Total Files");
                csv.WriteField("Total Size");
                csv.WriteField("Total Size (Bytes)");
                csv.WriteField("Duration (seconds)");
                csv.WriteField("Start Time");
                csv.WriteField("End Time");
                csv.WriteField("Is Box Drive");
                csv.WriteField("Is Network Path");
                csv.WriteField("Was Recursive");
                csv.WriteField("Max Depth");
                csv.WriteField("Error Count");
                csv.WriteField("Errors");
                await csv.NextRecordAsync();

                // Write data
                foreach (var result in results)
                {
                    csv.WriteField(result.Path);
                    csv.WriteField(result.Status.ToString());
                    csv.WriteField(result.TotalFolders);
                    csv.WriteField(result.TotalFiles);
                    csv.WriteField(result.GetSizeFormatted());
                    csv.WriteField(result.TotalSizeBytes);
                    csv.WriteField(result.Duration.TotalSeconds);
                    csv.WriteField(result.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
                    csv.WriteField(result.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "");
                    csv.WriteField(result.IsBoxDrivePath);
                    csv.WriteField(result.IsNetworkPath);
                    csv.WriteField(result.WasRecursive);
                    csv.WriteField(result.MaxDepthUsed?.ToString() ?? "Unlimited");
                    csv.WriteField(result.Errors.Count);
                    csv.WriteField(string.Join("; ", result.Errors.Select(e => $"{e.ErrorType}: {e.Message}")));
                    await csv.NextRecordAsync();
                }

                _logger?.LogInformation("Successfully exported to CSV");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error exporting to CSV: {FilePath}", filePath);
                throw;
            }
        }

        public async Task ExportToJsonAsync(List<ScanResult> results, string filePath)
        {
            try
            {
                _logger?.LogInformation("Exporting {Count} scan results to JSON: {FilePath}", results.Count, filePath);

                var json = JsonSerializer.Serialize(results, _jsonOptions);
                await File.WriteAllTextAsync(filePath, json);

                _logger?.LogInformation("Successfully exported to JSON");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error exporting to JSON: {FilePath}", filePath);
                throw;
            }
        }

        public async Task<List<PathItem>> ImportPathsFromCsvAsync(string filePath)
        {
            try
            {
                _logger?.LogInformation("Importing paths from CSV: {FilePath}", filePath);

                var paths = new List<PathItem>();
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    MissingFieldFound = null,
                    HeaderValidated = null
                };

                using var reader = new StreamReader(filePath);
                using var csv = new CsvReader(reader, config);

                await csv.ReadAsync();
                csv.ReadHeader();

                while (await csv.ReadAsync())
                {
                    // Try to read from "Path" column, fall back to first column
                    string? path = null;

                    var csvReader = csv.Context?.Reader;
                    var headerRecord = csvReader?.HeaderRecord;

                    if (headerRecord != null && headerRecord.Contains("Path"))
                    {
                        path = csv.GetField<string>("Path");
                    }
                    else if (headerRecord != null && headerRecord.Contains("path"))
                    {
                        path = csv.GetField<string>("path");
                    }
                    else
                    {
                        path = csv.GetField<string>(0);
                    }

                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        paths.Add(new PathItem(path));
                    }
                }

                _logger?.LogInformation("Successfully imported {Count} paths from CSV", paths.Count);
                return paths;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error importing paths from CSV: {FilePath}", filePath);
                throw;
            }
        }

        public async Task ExportComparisonToCsvAsync(ComparisonResult comparison, string filePath)
        {
            try
            {
                _logger?.LogInformation("Exporting comparison result to CSV: {FilePath}", filePath);

                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                };

                using var writer = new StreamWriter(filePath);
                using var csv = new CsvWriter(writer, config);

                // Summary section
                csv.WriteField("Comparison Summary");
                await csv.NextRecordAsync();

                csv.WriteField("Comparison Date");
                csv.WriteField(comparison.ComparisonDate.ToString("yyyy-MM-dd HH:mm:ss"));
                await csv.NextRecordAsync();

                csv.WriteField("Path");
                csv.WriteField(comparison.CurrentScan.Path);
                await csv.NextRecordAsync();
                await csv.NextRecordAsync();

                // Previous scan info
                csv.WriteField("Previous Scan");
                await csv.NextRecordAsync();
                csv.WriteField("Date");
                csv.WriteField(comparison.PreviousScan.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
                await csv.NextRecordAsync();
                csv.WriteField("Folders");
                csv.WriteField(comparison.PreviousScan.TotalFolders);
                await csv.NextRecordAsync();
                csv.WriteField("Files");
                csv.WriteField(comparison.PreviousScan.TotalFiles);
                await csv.NextRecordAsync();
                csv.WriteField("Size");
                csv.WriteField(comparison.PreviousScan.GetSizeFormatted());
                await csv.NextRecordAsync();
                await csv.NextRecordAsync();

                // Current scan info
                csv.WriteField("Current Scan");
                await csv.NextRecordAsync();
                csv.WriteField("Date");
                csv.WriteField(comparison.CurrentScan.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
                await csv.NextRecordAsync();
                csv.WriteField("Folders");
                csv.WriteField(comparison.CurrentScan.TotalFolders);
                await csv.NextRecordAsync();
                csv.WriteField("Files");
                csv.WriteField(comparison.CurrentScan.TotalFiles);
                await csv.NextRecordAsync();
                csv.WriteField("Size");
                csv.WriteField(comparison.CurrentScan.GetSizeFormatted());
                await csv.NextRecordAsync();
                await csv.NextRecordAsync();

                // Changes
                csv.WriteField("Changes");
                await csv.NextRecordAsync();
                csv.WriteField("Folder Delta");
                csv.WriteField(comparison.FolderDelta);
                await csv.NextRecordAsync();
                csv.WriteField("File Delta");
                csv.WriteField(comparison.FileDelta);
                await csv.NextRecordAsync();
                csv.WriteField("Size Delta");
                csv.WriteField(comparison.SizeDelta);
                await csv.NextRecordAsync();
                await csv.NextRecordAsync();

                // File type changes
                if (comparison.FileTypeChanges.Any())
                {
                    csv.WriteField("File Type Changes");
                    await csv.NextRecordAsync();
                    csv.WriteField("Extension");
                    csv.WriteField("Change");
                    await csv.NextRecordAsync();

                    foreach (var change in comparison.FileTypeChanges.OrderByDescending(x => Math.Abs(x.Value)))
                    {
                        csv.WriteField(change.Key);
                        csv.WriteField(change.Value);
                        await csv.NextRecordAsync();
                    }
                }

                _logger?.LogInformation("Successfully exported comparison to CSV");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error exporting comparison to CSV: {FilePath}", filePath);
                throw;
            }
        }
    }
}