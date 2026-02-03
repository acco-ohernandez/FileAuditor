using FileAuditor.Core.Models;

namespace FileAuditor.Core.Services
{
    public interface IFileScanner
    {
        Task<ScanResult> ScanPathAsync(
            PathItem pathItem,
            ScanConfiguration config,
            CancellationToken cancellationToken,
            IProgress<ScanProgress>? progress = null);

        Task<List<ScanResult>> ScanMultiplePathsAsync(
            List<PathItem> paths,
            ScanConfiguration config,
            CancellationToken cancellationToken,
            IProgress<ScanProgress>? progress = null);
    }

    public class ScanProgress
    {
        public string Path { get; set; } = string.Empty;
        public string CurrentOperation { get; set; } = string.Empty;
        public int PercentComplete { get; set; }
        public long FoldersProcessed { get; set; }
        public long FilesProcessed { get; set; }
    }
}