using FileAuditor.Core.Models;

namespace FileAuditor.Core.Services
{
    public interface IExportService
    {
        Task ExportToCsvAsync(List<ScanResult> results, string filePath);
        Task ExportToJsonAsync(List<ScanResult> results, string filePath);
        Task<List<PathItem>> ImportPathsFromCsvAsync(string filePath);
        Task ExportComparisonToCsvAsync(ComparisonResult comparison, string filePath);
    }
}