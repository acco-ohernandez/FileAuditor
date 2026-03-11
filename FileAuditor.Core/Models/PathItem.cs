namespace FileAuditor.Core.Models
{
    public class PathItem
    {
        public string Path { get; set; } = string.Empty;
        public bool IsBoxDrivePath { get; set; }
        public bool IsNetworkPath { get; set; }
        public bool IsValid { get; set; }
        public string? ValidationError { get; set; }

        /// <summary>
        /// Optional destination path used in MoveToFolder cleanup mode.
        /// Populated when the user supplies a two-column entry (source,destination).
        /// Null in Delete mode or when no destination is specified.
        /// </summary>
        public string? DestinationPath { get; set; }

        public PathItem() { }

        public PathItem(string path)
        {
            Path = path;
            DeterminePathType();
        }

        /// <summary>
        /// Creates a deep copy of this PathItem. Use instead of sharing references across cloned collections.
        /// </summary>
        public PathItem Clone()
        {
            return new PathItem
            {
                Path = Path,
                IsBoxDrivePath = IsBoxDrivePath,
                IsNetworkPath = IsNetworkPath,
                IsValid = IsValid,
                ValidationError = ValidationError,
                DestinationPath = DestinationPath
            };
        }

        /// <summary>
        /// Populates IsBoxDrivePath and IsNetworkPath from the current Path value.
        /// Called by the string constructor. Not called during JSON deserialization —
        /// use <see cref="RefreshPathType"/> after deserializing if metadata is needed.
        /// </summary>
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

        /// <summary>
        /// Re-evaluates IsBoxDrivePath and IsNetworkPath. Call this after JSON deserialization
        /// when the metadata fields may be stale (e.g. Box Drive path was empty at save time).
        /// </summary>
        public void RefreshPathType()
        {
            if (string.IsNullOrWhiteSpace(Path))
                return;

            IsNetworkPath = Path.StartsWith(@"\\");
            IsBoxDrivePath = Helpers.FileSystemHelper.IsBoxDrivePath(Path);
        }
    }
}