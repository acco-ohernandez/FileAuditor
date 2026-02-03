namespace FileAuditor.Core.Models
{
    public class PathItem
    {
        public string Path { get; set; } = string.Empty;
        public bool IsBoxDrivePath { get; set; }
        public bool IsNetworkPath { get; set; }
        public bool IsValid { get; set; }
        public string? ValidationError { get; set; }

        public PathItem() { }

        public PathItem(string path)
        {
            Path = path;
            DeterminePathType();
        }

        private void DeterminePathType()
        {
            if (string.IsNullOrWhiteSpace(Path))
            {
                IsValid = false;
                ValidationError = "Path cannot be empty";
                return;
            }

            // Check if network path (UNC path)
            IsNetworkPath = Path.StartsWith(@"\\");

            // Use FileSystemHelper for Box Drive detection
            IsBoxDrivePath = Helpers.FileSystemHelper.IsBoxDrivePath(Path);
        }
    }
}