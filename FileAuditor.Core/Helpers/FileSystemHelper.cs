using System.Runtime.InteropServices;

namespace FileAuditor.Core.Helpers
{
    public static class FileSystemHelper
    {
        // Cache the Box Drive path so we don't hit registry repeatedly
        private static string? _cachedBoxDrivePath = null;
        private static readonly object _cacheLock = new object();

        // Win32 API for checking file attributes
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetFileAttributes(string lpFileName);

        private const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;
        private const uint FILE_ATTRIBUTE_OFFLINE = 0x1000;
        private const uint FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x00400000;

        public static bool IsFileOffline(string path)
        {
            try
            {
                uint attributes = GetFileAttributes(path);

                if (attributes == INVALID_FILE_ATTRIBUTES)
                    return false;

                // Check if file is offline (Box Drive online-only file)
                return (attributes & FILE_ATTRIBUTE_OFFLINE) != 0 ||
                       (attributes & FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS) != 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryHydrateFile(string path, int timeoutSeconds = 5)
        {
            try
            {
                // Opening the file will trigger Box Drive to download it
                using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                // Wait for file to become available
                var startTime = DateTime.Now;
                while (IsFileOffline(path))
                {
                    if ((DateTime.Now - startTime).TotalSeconds > timeoutSeconds)
                        return false;

                    Thread.Sleep(100);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryHydrateDirectory(string path, int timeoutSeconds = 30)
        {
            try
            {
                // Enumerate directory to trigger Box Drive sync
                var items = Directory.EnumerateFileSystemEntries(path).ToList();

                var startTime = DateTime.Now;
                var allHydrated = false;

                while (!allHydrated && (DateTime.Now - startTime).TotalSeconds < timeoutSeconds)
                {
                    allHydrated = true;

                    foreach (var item in items)
                    {
                        if (IsFileOffline(item))
                        {
                            allHydrated = false;
                            break;
                        }
                    }

                    if (!allHydrated)
                        Thread.Sleep(500);
                }

                return allHydrated;
            }
            catch
            {
                return false;
            }
        }

        public static EnumerationOptions GetEnumerationOptions(bool includeHidden, bool ignoreInaccessible = true)
        {
            return new EnumerationOptions
            {
                IgnoreInaccessible = ignoreInaccessible,
                AttributesToSkip = includeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System,
                RecurseSubdirectories = false // We'll handle recursion manually for depth control
            };
        }

        public static bool IsHiddenOrSystem(string path)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                return (attributes & FileAttributes.Hidden) != 0 ||
                       (attributes & FileAttributes.System) != 0;
            }
            catch
            {
                return false;
            }
        }

        public static long GetDirectorySize(string path, bool recursive = true)
        {
            try
            {
                var dirInfo = new DirectoryInfo(path);

                long size = 0;

                // Get files in current directory
                foreach (var file in dirInfo.EnumerateFiles())
                {
                    size += file.Length;
                }

                // Recurse if needed
                if (recursive)
                {
                    foreach (var dir in dirInfo.EnumerateDirectories())
                    {
                        size += GetDirectorySize(dir.FullName, true);
                    }
                }

                return size;
            }
            catch
            {
                return 0;
            }
        }

        public static string GetBoxDrivePathFromRegistry()
        {
            lock (_cacheLock)
            {
                // Return cached value if available
                if (_cachedBoxDrivePath != null)
                    return _cachedBoxDrivePath;

                try
                {
                    // Check the Box preferences for sync directory path
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Box\Box\preferences");
                    if (key != null)
                    {
                        var syncPath = key.GetValue("sync_directory_path") as string;
                        if (!string.IsNullOrEmpty(syncPath))
                        {
                            _cachedBoxDrivePath = syncPath;
                            return syncPath;
                        }
                    }

                    // Also check the main Box key for alternate installations
                    using var boxKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Box\Box");
                    if (boxKey != null)
                    {
                        var boxPath = boxKey.GetValue("BoxDrivePath") as string;
                        if (!string.IsNullOrEmpty(boxPath))
                        {
                            _cachedBoxDrivePath = boxPath;
                            return boxPath;
                        }
                    }
                }
                catch
                {
                    // Ignore registry errors - we'll fall back to hardcoded path
                }

                _cachedBoxDrivePath = string.Empty;
                return string.Empty;
            }
        }

        public static bool IsBoxDrivePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                // Normalize the path
                var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                // Check registry for Box Drive installation
                var boxPath = GetBoxDrivePathFromRegistry();
                if (!string.IsNullOrEmpty(boxPath))
                {
                    var normalizedBoxPath = Path.GetFullPath(boxPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    if (normalizedPath.StartsWith(normalizedBoxPath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                // Fallback: Check common Box Drive paths
                if (normalizedPath.StartsWith(@"C:\CORP BOX", StringComparison.OrdinalIgnoreCase))
                    return true;

                // Some installations use B:\ drive
                if (normalizedPath.StartsWith(@"B:\", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
                // If path normalization fails, do simple string check
                if (path.StartsWith(@"C:\CORP BOX", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Clears the cached Box Drive path, forcing a registry re-read on next check
        /// </summary>
        public static void ClearBoxDrivePathCache()
        {
            lock (_cacheLock)
            {
                _cachedBoxDrivePath = null;
            }
        }
    }
}