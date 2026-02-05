using FileAuditor.Core.Enums;

namespace FileAuditor.Core.Models
{
    public class CleanupConfiguration
    {
        public string? Name { get; set; }
        public List<string> TargetPaths { get; set; } = new();

        // What to clean
        public CleanupTarget Target { get; set; } = CleanupTarget.FilesOnly;

        // Age criteria
        public CleanupDateMode DateMode { get; set; } = CleanupDateMode.OlderThan;
        public int DaysOld { get; set; } = 30; // For OlderThan mode
        public DateTime? StartDate { get; set; } // For DateRange mode
        public DateTime? EndDate { get; set; } // For DateRange mode

        // Options
        public bool IsRecursive { get; set; } = true;
        public int? MaxDepth { get; set; } = null;
        public bool IncludeHiddenFiles { get; set; } = false;
        public bool DryRun { get; set; } = true; // Safety: default to dry run

        // Filters
        public List<string> IncludeFileTypes { get; set; } = new(); // Only delete these types
        public List<string> ExcludeFileTypes { get; set; } = new(); // Don't delete these types
        public List<string> ExcludePatterns { get; set; } = new(); // Regex patterns to exclude

        // Safety
        public bool RequireConfirmation { get; set; } = true;
        public bool MoveToRecycleBin { get; set; } = true; // vs permanent delete

        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}