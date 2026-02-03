namespace FileAuditor.Core.Models
{
    public class FileTypeBreakdown
    {
        public Dictionary<string, int> FileTypeCounts { get; set; } = new();
        public int TotalFiles { get; set; }

        public void AddFileType(string extension)
        {
            extension = string.IsNullOrEmpty(extension) ? "[No Extension]" : extension.ToLower();

            if (FileTypeCounts.ContainsKey(extension))
                FileTypeCounts[extension]++;
            else
                FileTypeCounts[extension] = 1;

            TotalFiles++;
        }

        public List<KeyValuePair<string, int>> GetTopFileTypes(int count = 10)
        {
            return FileTypeCounts
                .OrderByDescending(x => x.Value)
                .Take(count)
                .ToList();
        }
    }
}