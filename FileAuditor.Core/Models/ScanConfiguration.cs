using FileAuditor.Core.Enums;

namespace FileAuditor.Core.Models
{
    public class ScanConfiguration
    {
        public List<PathItem> Paths { get; set; } = new();
        public CountMode CountMode { get; set; } = CountMode.Both;
        public bool IsRecursive { get; set; } = true;
        public int? MaxDepth { get; set; } = null;
        public int ParallelThreadCount { get; set; } = 4;
        public bool IncludeHiddenFiles { get; set; } = false;
        public bool BoxDriveRefreshEnabled { get; set; } = true;
        public int BoxDriveRefreshTimeoutSeconds { get; set; } = 30;

        // File type filters
        public List<string> IncludeFileTypes { get; set; } = new(); // e.g., [".docx", ".pdf"]
        public List<string> ExcludeFileTypes { get; set; } = new(); // e.g., [".tmp", ".log"]

        // File type breakdown
        public bool EnableFileTypeBreakdown { get; set; } = true;

        // Configuration name for saving/loading
        public string? Name { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public ScanConfiguration Clone()
        {
            return new ScanConfiguration
            {
                Paths = new List<PathItem>(Paths),
                CountMode = CountMode,
                IsRecursive = IsRecursive,
                MaxDepth = MaxDepth,
                ParallelThreadCount = ParallelThreadCount,
                IncludeHiddenFiles = IncludeHiddenFiles,
                BoxDriveRefreshEnabled = BoxDriveRefreshEnabled,
                BoxDriveRefreshTimeoutSeconds = BoxDriveRefreshTimeoutSeconds,
                IncludeFileTypes = new List<string>(IncludeFileTypes),
                ExcludeFileTypes = new List<string>(ExcludeFileTypes),
                EnableFileTypeBreakdown = EnableFileTypeBreakdown,
                Name = Name,
                CreatedDate = CreatedDate
            };
        }
    }
}