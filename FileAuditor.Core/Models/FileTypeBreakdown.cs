using System.Collections.Concurrent;

namespace FileAuditor.Core.Models
{
    public class FileTypeBreakdown
    {
        // ConcurrentDictionary so AddFileType() is safe when called from concurrent scan threads.
        private readonly ConcurrentDictionary<string, int> _fileTypeCounts = new(StringComparer.OrdinalIgnoreCase);
        private long _totalFiles;

        /// <summary>
        /// Read-only snapshot of extension → count. Setter kept for JSON deserialization.
        /// </summary>
        public Dictionary<string, int> FileTypeCounts
        {
            get => new(_fileTypeCounts);
            set
            {
                _fileTypeCounts.Clear();
                foreach (var kv in value)
                    _fileTypeCounts[kv.Key] = kv.Value;
            }
        }

        public long TotalFiles
        {
            get => Interlocked.Read(ref _totalFiles);
            set => Interlocked.Exchange(ref _totalFiles, value);
        }

        /// <summary>Thread-safe: records one file with the given extension.</summary>
        public void AddFileType(string extension)
        {
            extension = string.IsNullOrEmpty(extension) ? "[No Extension]" : extension.ToLower();
            _fileTypeCounts.AddOrUpdate(extension, 1, (_, count) => count + 1);
            Interlocked.Increment(ref _totalFiles);
        }

        public List<KeyValuePair<string, int>> GetTopFileTypes(int count = 10)
        {
            return _fileTypeCounts
                .OrderByDescending(x => x.Value)
                .Take(count)
                .ToList();
        }
    }
}
