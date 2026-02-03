using FileAuditor.Core.Models;

namespace FileAuditor.Core.Helpers
{
    public static class PathValidator
    {
        public static PathItem ValidatePath(string path)
        {
            var pathItem = new PathItem(path);

            if (string.IsNullOrWhiteSpace(path))
            {
                pathItem.IsValid = false;
                pathItem.ValidationError = "Path cannot be empty";
                return pathItem;
            }

            // Check for invalid characters
            var invalidChars = Path.GetInvalidPathChars();
            if (path.IndexOfAny(invalidChars) >= 0)
            {
                pathItem.IsValid = false;
                pathItem.ValidationError = "Path contains invalid characters";
                return pathItem;
            }

            // Try to get full path
            try
            {
                var fullPath = Path.GetFullPath(path);
                pathItem.Path = fullPath;
            }
            catch (Exception ex)
            {
                pathItem.IsValid = false;
                pathItem.ValidationError = $"Invalid path format: {ex.Message}";
                return pathItem;
            }

            // Check if path exists
            if (!Directory.Exists(pathItem.Path))
            {
                pathItem.IsValid = false;
                pathItem.ValidationError = "Path does not exist";
                return pathItem;
            }

            // Check if we have access
            try
            {
                _ = Directory.GetDirectories(pathItem.Path);
                pathItem.IsValid = true;
            }
            catch (UnauthorizedAccessException)
            {
                pathItem.IsValid = false;
                pathItem.ValidationError = "Access denied to this path";
            }
            catch (Exception ex)
            {
                pathItem.IsValid = false;
                pathItem.ValidationError = $"Cannot access path: {ex.Message}";
            }

            return pathItem;
        }

        public static List<PathItem> ValidateMultiplePaths(IEnumerable<string> paths)
        {
            return paths.Select(ValidatePath).ToList();
        }

        public static List<PathItem> ParsePathsFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<PathItem>();

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var paths = lines
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct()
                .ToList();

            return ValidateMultiplePaths(paths);
        }

        public static bool IsNetworkPathAvailable(string path)
        {
            if (!path.StartsWith(@"\\"))
                return true; // Not a network path

            try
            {
                return Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }
    }
}