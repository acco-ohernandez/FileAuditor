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

        /// <summary>
        /// Destination root folder for this path when OperationMode is MoveToFolder.
        /// Stamped by the ViewModel after Analyze completes, from PathItem.DestinationPath.
        /// </summary>
        public string? DestinationPath { get; set; }

        // Results
        public long FilesIdentified { get; set; }
        public long FilesDeleted { get; set; }
        public long FilesMoved { get; set; }
        public long FoldersIdentified { get; set; }
        public long FoldersDeleted { get; set; }
        public long FoldersMoved { get; set; }
        public long TotalSizeBytes { get; set; }

        // DryRun simulation counters — populated when WasDryRun is true.
        // Show how many items *would* have been affected without touching any files.
        public long FilesWouldDelete { get; set; }
        public long FoldersWouldDelete { get; set; }
        public long FilesWouldMove { get; set; }
        public long FoldersWouldMove { get; set; }

        // Details
        public List<CleanupItem> Items { get; set; } = new();
        public List<ScanError> Errors { get; set; } = new();
        public bool HasErrors => Errors.Any();
        public string SizeFormatted => GetSizeFormatted();

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

        /// <summary>Human-readable type label for UI display ("File" or "Folder").</summary>
        public string TypeLabel => IsDirectory ? "Folder" : "File";

        public DateTime LastModified { get; set; }
        public long SizeBytes { get; set; }
        public bool WasDeleted { get; set; }
        public string? DeletionError { get; set; }
        public bool WasMoved { get; set; }
        public string? MovedToPath { get; set; }
        public string? MoveError { get; set; }

        // DryRun simulation flags — set by CleanupService.SimulateDryRun
        // when config.DryRun is true. No files are touched.
        public bool WouldBeDeleted { get; set; }
        public bool WouldBeMoved { get; set; }

        /// <summary>
        /// The path where this item would be (or was actually) moved.
        /// In DryRun mode this is the computed destination; after a real move it is
        /// the actual destination. Bind to this property in the DataGrid instead of
        /// <see cref="MovedToPath"/> so both modes show a populated column.
        /// </summary>
        public string? WouldMovedToPath { get; set; }
        public string? DisplayMovedToPath => MovedToPath ?? WouldMovedToPath;

        /// <summary>Human-readable action status for the Delete tab DataGrid.</summary>
        public string DeleteActionLabel =>
            DeletionError  != null    ? "Error"         :
            WasDeleted                ? "Deleted"        :
            WouldBeDeleted            ? "Would Delete"   :
                                        "Identified";

        /// <summary>Human-readable action status for the Move to Folder tab DataGrid.</summary>
        public string MoveActionLabel =>
            MoveError  != null ? "Error"       :
            WasMoved           ? "Moved"        :
            WouldBeMoved       ? "Would Move"   :
                                 "Identified";
    }
}