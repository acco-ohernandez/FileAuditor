using FileAuditor.Core.Models;

namespace FileAuditor.Core.Services
{
    public interface IScanHistoryService
    {
        Task SaveScanResultAsync(ScanResult result);
        Task<List<ScanResult>> GetScanHistoryAsync(string? path = null, int? limit = null);
        Task<ScanResult?> GetScanByIdAsync(Guid id);
        Task DeleteScanAsync(Guid id);
        Task DeleteOldScansAsync(int daysToKeep);
        Task<ComparisonResult?> CompareScansByIdAsync(Guid previousId, Guid currentId);
        Task SaveConfigurationAsync(ScanConfiguration config);
        Task<List<ScanConfiguration>> GetSavedConfigurationsAsync();
        Task<ScanConfiguration?> GetConfigurationByNameAsync(string name);
        Task DeleteConfigurationAsync(string name);
    }
}