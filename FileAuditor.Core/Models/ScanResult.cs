using FileAuditor.Core.Enums;

namespace FileAuditor.Core.Models
{
    public class ScanResult
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Path { get; set; } = string.Empty;
        public ScanStatus Status { get; set; } = ScanStatus.Pending;

        // Counts
        public long TotalFolders { get; set; }
        public long TotalFiles { get; set; }
        public long TotalSizeBytes { get; set; }

        // File type breakdown
        public FileTypeBreakdown? FileTypeBreakdown { get; set; }

        // Timing
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan Duration => EndTime.HasValue ? EndTime.Value - StartTime : TimeSpan.Zero;

        // Errors
        public List<ScanError> Errors { get; set; } = new();
        public bool HasErrors => Errors.Any();

        // Path information
        public bool IsBoxDrivePath { get; set; }
        public bool IsNetworkPath { get; set; }

        // Configuration used
        public CountMode CountMode { get; set; }
        public bool WasRecursive { get; set; }
        public int? MaxDepthUsed { get; set; }

        // Cancellation
        public bool WasCancelled { get; set; }

        // Progress tracking
        public int ProgressPercentage { get; set; }
        public string? CurrentOperation { get; set; }

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

        public string GetStatusDescription()
        {
            return Status switch
            {
                ScanStatus.Pending => "Waiting to start",
                ScanStatus.Running => CurrentOperation ?? "Scanning...",
                ScanStatus.Completed => $"Completed in {Duration.TotalSeconds:F1}s",
                ScanStatus.Failed => $"Failed: {Errors.FirstOrDefault()?.Message ?? "Unknown error"}",
                ScanStatus.Cancelled => "Cancelled by user",
                _ => "Unknown"
            };
        }
    }
}