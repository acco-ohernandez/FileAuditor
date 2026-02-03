namespace FileAuditor.CLI.Models
{
    public class CliConfiguration
    {
        public List<string> Paths { get; set; } = new();
        public string CountMode { get; set; } = "Both"; // "Both", "FoldersOnly", "FilesOnly"
        public bool Recursive { get; set; } = true;
        public int? MaxDepth { get; set; } = 1;
        public int ParallelThreads { get; set; } = 4;
        public bool IncludeHiddenFiles { get; set; } = false;
        public bool BoxDriveRefreshEnabled { get; set; } = true;
        public int BoxDriveRefreshTimeoutSeconds { get; set; } = 30;
        public bool EnableFileTypeBreakdown { get; set; } = true;
        public List<string> IncludeFileTypes { get; set; } = new();
        public List<string> ExcludeFileTypes { get; set; } = new();

        // Output options
        public string OutputFormat { get; set; } = "csv"; // "csv" or "json"
        public string OutputPath { get; set; } = "scan_results_{timestamp}.csv";

        // History options
        public bool EnableHistory { get; set; } = true;
        public bool EnableComparison { get; set; } = false;
        public int ComparisonRetentionDays { get; set; } = 90;
    }
}