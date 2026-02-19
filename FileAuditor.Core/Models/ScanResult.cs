using System.Collections.Concurrent;

using FileAuditor.Core.Enums;

namespace FileAuditor.Core.Models
{
    public class ScanResult
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Path { get; set; } = string.Empty;
        public ScanStatus Status { get; set; } = ScanStatus.Pending;

        // Counts — use Interlocked-safe long fields so concurrent recursive calls are safe.
        private long _totalFolders;
        private long _totalFiles;
        private long _totalSizeBytes;

        public long TotalFolders
        {
            get => Interlocked.Read(ref _totalFolders);
            set => Interlocked.Exchange(ref _totalFolders, value);
        }

        public long TotalFiles
        {
            get => Interlocked.Read(ref _totalFiles);
            set => Interlocked.Exchange(ref _totalFiles, value);
        }

        public long TotalSizeBytes
        {
            get => Interlocked.Read(ref _totalSizeBytes);
            set => Interlocked.Exchange(ref _totalSizeBytes, value);
        }

        /// <summary>Increments TotalFiles atomically.</summary>
        public void IncrementFiles() => Interlocked.Increment(ref _totalFiles);

        /// <summary>Adds <paramref name="bytes"/> to TotalSizeBytes atomically.</summary>
        public void AddBytes(long bytes) => Interlocked.Add(ref _totalSizeBytes, bytes);

        /// <summary>Increments TotalFolders atomically.</summary>
        public void IncrementFolders() => Interlocked.Increment(ref _totalFolders);

        // File type breakdown
        public FileTypeBreakdown? FileTypeBreakdown { get; set; }

        // Timing
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan Duration => EndTime.HasValue ? EndTime.Value - StartTime : TimeSpan.Zero;

        // Errors — ConcurrentBag is safe for concurrent Add() from multiple threads.
        private readonly ConcurrentBag<ScanError> _errors = new();
        public IReadOnlyCollection<ScanError> Errors => _errors;
        public bool HasErrors => !_errors.IsEmpty;

        /// <summary>Thread-safe error recording.</summary>
        public void AddError(ScanError error) => _errors.Add(error);

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

            var bytes = TotalSizeBytes;

            if (bytes >= TB)
                return $"{bytes / (double)TB:F2} TB";
            if (bytes >= GB)
                return $"{bytes / (double)GB:F2} GB";
            if (bytes >= MB)
                return $"{bytes / (double)MB:F2} MB";
            if (bytes >= KB)
                return $"{bytes / (double)KB:F2} KB";

            return $"{bytes} bytes";
        }

        public string GetStatusDescription()
        {
            return Status switch
            {
                ScanStatus.Pending => "Waiting to start",
                ScanStatus.Running => CurrentOperation ?? "Scanning...",
                ScanStatus.Completed => $"Completed in {Duration.TotalSeconds:F1}s",
                ScanStatus.Failed => $"Failed: {_errors.FirstOrDefault()?.Message ?? "Unknown error"}",
                ScanStatus.Cancelled => "Cancelled by user",
                _ => "Unknown"
            };
        }
    }
}
