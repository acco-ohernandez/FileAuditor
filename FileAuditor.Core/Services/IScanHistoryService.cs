using FileAuditor.Core.Models;

namespace FileAuditor.Core.Services
{
    public interface IScanHistoryService
    {
        // Existing scan methods...
        Task SaveScanResultAsync(ScanResult result);
        Task<List<ScanResult>> GetScanHistoryAsync(string? path = null, int? limit = null);
        Task<ScanResult?> GetScanByIdAsync(Guid id);
        Task DeleteScanAsync(Guid id);
        Task DeleteOldScansAsync(int daysToKeep);
        Task<ComparisonResult?> CompareScansByIdAsync(Guid previousId, Guid currentId);

        // Scan configuration methods
        Task SaveConfigurationAsync(ScanConfiguration config);
        Task<List<ScanConfiguration>> GetSavedConfigurationsAsync();
        Task<ScanConfiguration?> GetConfigurationByNameAsync(string name);
        Task DeleteConfigurationAsync(string name);

        // NEW: Cleanup configuration methods
        Task SaveCleanupConfigurationAsync(CleanupConfiguration config);
        Task<List<CleanupConfiguration>> GetSavedCleanupConfigurationsAsync();
        Task<CleanupConfiguration?> GetCleanupConfigurationByNameAsync(string name);
        Task DeleteCleanupConfigurationAsync(string name);
    }
}