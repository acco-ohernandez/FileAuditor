namespace FileAuditor.Core.Models
{
    public class ComparisonResult
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime ComparisonDate { get; set; } = DateTime.Now;

        // Reference scans
        public ScanResult PreviousScan { get; set; } = null!;
        public ScanResult CurrentScan { get; set; } = null!;

        // Deltas
        public long FolderDelta { get; set; }
        public long FileDelta { get; set; }
        public long SizeDelta { get; set; }

        // File type changes
        public Dictionary<string, int> FileTypeChanges { get; set; } = new();

        // Summary
        public bool HasChanges => FolderDelta != 0 || FileDelta != 0 || SizeDelta != 0;

        public string GetChangesSummary()
        {
            if (!HasChanges)
                return "No changes detected";

            var parts = new List<string>();

            if (FolderDelta != 0)
                parts.Add($"{(FolderDelta > 0 ? "+" : "")}{FolderDelta} folders");

            if (FileDelta != 0)
                parts.Add($"{(FileDelta > 0 ? "+" : "")}{FileDelta} files");

            if (SizeDelta != 0)
            {
                var sizeChange = FormatSizeDelta(SizeDelta);
                parts.Add($"{(SizeDelta > 0 ? "+" : "")}{sizeChange}");
            }

            return string.Join(", ", parts);
        }

        private string FormatSizeDelta(long bytes)
        {
            const long KB = 1024;
            const long MB = KB * 1024;
            const long GB = MB * 1024;

            var absBytes = Math.Abs(bytes);

            if (absBytes >= GB)
                return $"{bytes / (double)GB:F2} GB";
            if (absBytes >= MB)
                return $"{bytes / (double)MB:F2} MB";
            if (absBytes >= KB)
                return $"{bytes / (double)KB:F2} KB";

            return $"{bytes} bytes";
        }

        public static ComparisonResult Compare(ScanResult previous, ScanResult current)
        {
            var result = new ComparisonResult
            {
                PreviousScan = previous,
                CurrentScan = current,
                FolderDelta = current.TotalFolders - previous.TotalFolders,
                FileDelta = current.TotalFiles - previous.TotalFiles,
                SizeDelta = current.TotalSizeBytes - previous.TotalSizeBytes
            };

            // Calculate file type changes
            if (previous.FileTypeBreakdown != null && current.FileTypeBreakdown != null)
            {
                var allExtensions = previous.FileTypeBreakdown.FileTypeCounts.Keys
                    .Union(current.FileTypeBreakdown.FileTypeCounts.Keys)
                    .Distinct();

                foreach (var ext in allExtensions)
                {
                    var prevCount = previous.FileTypeBreakdown.FileTypeCounts.GetValueOrDefault(ext, 0);
                    var currCount = current.FileTypeBreakdown.FileTypeCounts.GetValueOrDefault(ext, 0);
                    var delta = currCount - prevCount;

                    if (delta != 0)
                        result.FileTypeChanges[ext] = delta;
                }
            }

            return result;
        }
    }
}