using FileAuditor.Core.Enums;

namespace FileAuditor.Core.Models
{
    public class ScanError
    {
        public ErrorType ErrorType { get; set; }
        public string Path { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public Exception? Exception { get; set; }
    }
}