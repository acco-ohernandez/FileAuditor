using FileAuditor.Core.Enums;

namespace FileAuditor.Core.Models
{
    public class CleanupConfiguration
    {
        public string? Name { get; set; }
        public List<PathItem> TargetPaths { get; set; } = new(); // Changed from List<string>

        // What to clean
        public CleanupTarget Target { get; set; } = CleanupTarget.FilesOnly;

        // Age criteria
        public CleanupDateMode DateMode { get; set; } = CleanupDateMode.OlderThan;
        public int DaysOld { get; set; } = 30; // For OlderThan/NewerThan mode
        public DateTime? StartDate { get; set; } // For DateRange/ExactDate mode
        public DateTime? EndDate { get; set; } // For DateRange mode

        // NEW: Time options
        public bool IncludeTime { get; set; } = false;
        public TimeSpan? StartTime { get; set; } // Time component for dates
        public TimeSpan? EndTime { get; set; } // Time component for end date

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

        public CleanupConfiguration Clone()
        {
            return new CleanupConfiguration
            {
                Name = Name,
                TargetPaths = new List<PathItem>(TargetPaths),
                Target = Target,
                DateMode = DateMode,
                DaysOld = DaysOld,
                StartDate = StartDate,
                EndDate = EndDate,
                IncludeTime = IncludeTime,
                StartTime = StartTime,
                EndTime = EndTime,
                IsRecursive = IsRecursive,
                MaxDepth = MaxDepth,
                IncludeHiddenFiles = IncludeHiddenFiles,
                DryRun = DryRun,
                IncludeFileTypes = new List<string>(IncludeFileTypes),
                ExcludeFileTypes = new List<string>(ExcludeFileTypes),
                ExcludePatterns = new List<string>(ExcludePatterns),
                RequireConfirmation = RequireConfirmation,
                MoveToRecycleBin = MoveToRecycleBin,
                CreatedDate = CreatedDate
            };
        }
    }
}