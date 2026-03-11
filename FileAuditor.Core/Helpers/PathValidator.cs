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

            // Check if we have access — use EnumerateDirectories().Any() instead of GetDirectories()
            // to avoid allocating a full array just for an access check (L-4 fix).
            try
            {
                _ = Directory.EnumerateDirectories(pathItem.Path).Any();
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

        /// <summary>
        /// Parses cleanup paths from text that may use a two-column CSV format
        /// (source,destination per line). The source column is validated for existence;
        /// the destination column is stored as-is (format-only, existence is not checked
        /// because it will be auto-created at execution time).
        /// Single-column lines are treated as source-only entries.
        /// Duplicate source paths are deduplicated (first occurrence wins).
        /// </summary>
        public static List<PathItem> ParseCleanupPathsFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<PathItem>();

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<PathItem>();
            var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                // Split on the first comma only so paths that contain commas still work.
                var commaIdx = trimmed.IndexOf(',');
                string sourcePart;
                string? destPart;

                if (commaIdx >= 0)
                {
                    sourcePart = trimmed.Substring(0, commaIdx).Trim();
                    var rawDest = trimmed.Substring(commaIdx + 1).Trim();
                    destPart = string.IsNullOrWhiteSpace(rawDest) ? null : rawDest;
                }
                else
                {
                    sourcePart = trimmed;
                    destPart = null;
                }

                if (string.IsNullOrWhiteSpace(sourcePart))
                    continue;

                // Deduplicate by source path.
                if (!seenSources.Add(sourcePart))
                    continue;

                var pathItem = ValidatePath(sourcePart);
                pathItem.DestinationPath = destPart;
                result.Add(pathItem);
            }

            return result;
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