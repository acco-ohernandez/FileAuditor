using FileAuditor.Core.Enums;

namespace FileAuditor.Core.Models
{
    public class CleanupResult
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Path { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan Duration => EndTime.HasValue ? EndTime.Value - StartTime : TimeSpan.Zero;

        public CleanupStatus Status { get; set; } = CleanupStatus.Pending;
        public bool WasDryRun { get; set; }

        // Results
        public long FilesIdentified { get; set; }
        public long FilesDeleted { get; set; }
        public long FoldersIdentified { get; set; }
        public long FoldersDeleted { get; set; }
        public long TotalSizeBytes { get; set; }

        // Details
        public List<CleanupItem> Items { get; set; } = new();
        public List<ScanError> Errors { get; set; } = new();
        public bool HasErrors => Errors.Any();

        public string GetSizeFormatted()
        {
            const long KB = 1024;
            const long MB = KB * 1024;
            const long GB = MB * 1024;
            const long TB = GB * 1024;

            if (TotalSizeBytes >= TB)
                return $"{TotalSizeBytes / (double)TB:F2} TB";
            if (TotalSizeBytes >= GB)
                return $"{TotalSizeBytes / (double)GB:F2} GB";
            if (TotalSizeBytes >= MB)
                return $"{TotalSizeBytes / (double)MB:F2} MB";
            if (TotalSizeBytes >= KB)
                return $"{TotalSizeBytes / (double)KB:F2} KB";

            return $"{TotalSizeBytes} bytes";
        }
    }

    public class CleanupItem
    {
        public string Path { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public DateTime LastModified { get; set; }
        public long SizeBytes { get; set; }
        public bool WasDeleted { get; set; }
        public string? DeletionError { get; set; }
    }
}