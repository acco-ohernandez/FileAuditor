using System.Text.Json;

using FileAuditor.Core.Models;

using Microsoft.Extensions.Logging;

namespace FileAuditor.Core.Services
{
    public class ScanHistoryService : IScanHistoryService
    {
        private readonly string _historyDirectory;
        private readonly string _configDirectory;
        private readonly ILogger<ScanHistoryService>? _logger;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly string _cleanupConfigDirectory;

        public ScanHistoryService(ILogger<ScanHistoryService>? logger = null)
        {
            _logger = logger;

            // Store in AppData/Local
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appDataPath, "FileAuditor");

            _historyDirectory = Path.Combine(appFolder, "History");
            _configDirectory = Path.Combine(appFolder, "Configurations");
            _cleanupConfigDirectory = Path.Combine(appFolder, "CleanupConfigurations"); // NEW

            // Create directories if they don't exist
            Directory.CreateDirectory(_historyDirectory);
            Directory.CreateDirectory(_configDirectory);
            Directory.CreateDirectory(_cleanupConfigDirectory); // NEW

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        public async Task SaveScanResultAsync(ScanResult result)
        {
            try
            {
                var fileName = $"scan_{result.Id}_{result.StartTime:yyyyMMdd_HHmmss}.json";
                var filePath = Path.Combine(_historyDirectory, fileName);

                var json = JsonSerializer.Serialize(result, _jsonOptions);
                await File.WriteAllTextAsync(filePath, json);

                _logger?.LogInformation("Saved scan result: {Id} to {FilePath}", result.Id, filePath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error saving scan result: {Id}", result.Id);
                throw;
            }
        }

        public async Task<List<ScanResult>> GetScanHistoryAsync(string? path = null, int? limit = null)
        {
            try
            {
                var files = Directory.GetFiles(_historyDirectory, "scan_*.json")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .ToList();

                if (limit.HasValue)
                    files = files.Take(limit.Value).ToList();

                var results = new List<ScanResult>();

                foreach (var file in files)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var result = JsonSerializer.Deserialize<ScanResult>(json, _jsonOptions);

                        if (result != null)
                        {
                            // Filter by path if specified
                            if (string.IsNullOrEmpty(path) ||
                                result.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
                            {
                                results.Add(result);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Error reading scan history file: {File}", file);
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting scan history");
                throw;
            }
        }

        public async Task<ScanResult?> GetScanByIdAsync(Guid id)
        {
            try
            {
                var files = Directory.GetFiles(_historyDirectory, $"scan_{id}_*.json");

                if (files.Length == 0)
                    return null;

                var json = await File.ReadAllTextAsync(files[0]);
                return JsonSerializer.Deserialize<ScanResult>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting scan by ID: {Id}", id);
                return null;
            }
        }

        public async Task DeleteScanAsync(Guid id)
        {
            try
            {
                var files = Directory.GetFiles(_historyDirectory, $"scan_{id}_*.json");

                foreach (var file in files)
                {
                    File.Delete(file);
                    _logger?.LogInformation("Deleted scan: {Id}", id);
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting scan: {Id}", id);
                throw;
            }
        }

        public async Task DeleteOldScansAsync(int daysToKeep)
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                var files = Directory.GetFiles(_historyDirectory, "scan_*.json");
                var deletedCount = 0;

                foreach (var file in files)
                {
                    var creationTime = File.GetCreationTime(file);
                    if (creationTime < cutoffDate)
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                }

                _logger?.LogInformation("Deleted {Count} old scan(s) older than {Days} days", deletedCount, daysToKeep);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting old scans");
                throw;
            }
        }

        public async Task<ComparisonResult?> CompareScansByIdAsync(Guid previousId, Guid currentId)
        {
            try
            {
                var previousScan = await GetScanByIdAsync(previousId);
                var currentScan = await GetScanByIdAsync(currentId);

                if (previousScan == null || currentScan == null)
                    return null;

                return ComparisonResult.Compare(previousScan, currentScan);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error comparing scans: {PreviousId} vs {CurrentId}", previousId, currentId);
                throw;
            }
        }

        public async Task SaveConfigurationAsync(ScanConfiguration config)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(config.Name))
                    throw new ArgumentException("Configuration name cannot be empty");

                var fileName = $"{SanitizeFileName(config.Name)}.json";
                var filePath = Path.Combine(_configDirectory, fileName);

                var json = JsonSerializer.Serialize(config, _jsonOptions);
                await File.WriteAllTextAsync(filePath, json);

                _logger?.LogInformation("Saved configuration: {Name}", config.Name);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error saving configuration: {Name}", config.Name);
                throw;
            }
        }

        public async Task<List<ScanConfiguration>> GetSavedConfigurationsAsync()
        {
            try
            {
                var files = Directory.GetFiles(_configDirectory, "*.json");
                var configurations = new List<ScanConfiguration>();

                foreach (var file in files)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var config = JsonSerializer.Deserialize<ScanConfiguration>(json, _jsonOptions);

                        if (config != null)
                            configurations.Add(config);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Error reading configuration file: {File}", file);
                    }
                }

                return configurations.OrderBy(c => c.Name).ToList();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting saved configurations");
                throw;
            }
        }

        public async Task<ScanConfiguration?> GetConfigurationByNameAsync(string name)
        {
            try
            {
                var fileName = $"{SanitizeFileName(name)}.json";
                var filePath = Path.Combine(_configDirectory, fileName);

                if (!File.Exists(filePath))
                    return null;

                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<ScanConfiguration>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting configuration by name: {Name}", name);
                return null;
            }
        }

        public async Task DeleteConfigurationAsync(string name)
        {
            try
            {
                var fileName = $"{SanitizeFileName(name)}.json";
                var filePath = Path.Combine(_configDirectory, fileName);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger?.LogInformation("Deleted configuration: {Name}", name);
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting configuration: {Name}", name);
                throw;
            }
        }

        private string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        }

        // New methods
        public async Task SaveCleanupConfigurationAsync(CleanupConfiguration config)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(config.Name))
                    throw new ArgumentException("Configuration name cannot be empty");

                var fileName = $"{SanitizeFileName(config.Name)}.json";
                var filePath = Path.Combine(_cleanupConfigDirectory, fileName);

                var json = JsonSerializer.Serialize(config, _jsonOptions);
                await File.WriteAllTextAsync(filePath, json);

                _logger?.LogInformation("Saved cleanup configuration: {Name}", config.Name);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error saving cleanup configuration: {Name}", config.Name);
                throw;
            }
        }

        public async Task<List<CleanupConfiguration>> GetSavedCleanupConfigurationsAsync()
        {
            try
            {
                var files = Directory.GetFiles(_cleanupConfigDirectory, "*.json");
                var configurations = new List<CleanupConfiguration>();

                foreach (var file in files)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var config = JsonSerializer.Deserialize<CleanupConfiguration>(json, _jsonOptions);

                        if (config != null)
                            configurations.Add(config);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Error reading cleanup configuration file: {File}", file);
                    }
                }

                return configurations.OrderBy(c => c.Name).ToList();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting saved cleanup configurations");
                throw;
            }
        }

        public async Task<CleanupConfiguration?> GetCleanupConfigurationByNameAsync(string name)
        {
            try
            {
                var fileName = $"{SanitizeFileName(name)}.json";
                var filePath = Path.Combine(_cleanupConfigDirectory, fileName);

                if (!File.Exists(filePath))
                    return null;

                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<CleanupConfiguration>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting cleanup configuration by name: {Name}", name);
                return null;
            }
        }

        public async Task DeleteCleanupConfigurationAsync(string name)
        {
            try
            {
                var fileName = $"{SanitizeFileName(name)}.json";
                var filePath = Path.Combine(_cleanupConfigDirectory, fileName);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger?.LogInformation("Deleted cleanup configuration: {Name}", name);
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting cleanup configuration: {Name}", name);
                throw;
            }
        }

    }
}