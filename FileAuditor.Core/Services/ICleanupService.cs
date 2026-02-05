using FileAuditor.Core.Models;

namespace FileAuditor.Core.Services
{
    public interface ICleanupService
    {
        Task<CleanupResult> AnalyzeAsync(
            string path,
            CleanupConfiguration config,
            CancellationToken cancellationToken,
            IProgress<CleanupProgress>? progress = null);

        Task<CleanupResult> ExecuteCleanupAsync(
            CleanupResult analysisResult,
            CleanupConfiguration config,
            CancellationToken cancellationToken,
            IProgress<CleanupProgress>? progress = null);

        Task<List<CleanupResult>> AnalyzeMultiplePathsAsync(
            List<string> paths,
            CleanupConfiguration config,
            CancellationToken cancellationToken,
            IProgress<CleanupProgress>? progress = null);
    }

    public class CleanupProgress
    {
        public string CurrentPath { get; set; } = string.Empty;
        public string CurrentOperation { get; set; } = string.Empty;
        public int PercentComplete { get; set; }
        public long ItemsProcessed { get; set; }
        public long ItemsIdentified { get; set; }
    }
}