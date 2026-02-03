using FileAuditor.Core.Helpers;

using Microsoft.Extensions.Logging;

namespace FileAuditor.Core.Services
{
    public class BoxDriveHandler : IBoxDriveHandler
    {
        private readonly ILogger<BoxDriveHandler>? _logger;

        public BoxDriveHandler(ILogger<BoxDriveHandler>? logger = null)
        {
            _logger = logger;
        }

        public bool IsBoxDrivePath(string path)
        {
            return FileSystemHelper.IsBoxDrivePath(path);
        }

        public async Task<bool> RefreshDirectoryAsync(string path, int timeoutSeconds, CancellationToken cancellationToken)
        {
            if (!IsBoxDrivePath(path))
                return true; // Not a Box Drive path, nothing to refresh

            _logger?.LogInformation("Refreshing Box Drive directory: {Path}", path);

            try
            {
                return await Task.Run(() =>
                {
                    return FileSystemHelper.TryHydrateDirectory(path, timeoutSeconds);
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning("Box Drive refresh cancelled for: {Path}", path);
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error refreshing Box Drive directory: {Path}", path);
                return false;
            }
        }

        public async Task<bool> EnsureDirectoryAvailableAsync(string path, CancellationToken cancellationToken)
        {
            if (!IsBoxDrivePath(path))
                return true;

            var status = GetDirectoryStatus(path);

            if (status == BoxDriveStatus.Available)
                return true;

            if (status == BoxDriveStatus.OnlineOnly || status == BoxDriveStatus.Syncing)
            {
                return await RefreshDirectoryAsync(path, 30, cancellationToken);
            }

            return false;
        }

        public BoxDriveStatus GetDirectoryStatus(string path)
        {
            if (!IsBoxDrivePath(path))
                return BoxDriveStatus.NotBoxDrive;

            try
            {
                if (!Directory.Exists(path))
                    return BoxDriveStatus.Error;

                if (FileSystemHelper.IsFileOffline(path))
                    return BoxDriveStatus.OnlineOnly;

                return BoxDriveStatus.Available;
            }
            catch
            {
                return BoxDriveStatus.Error;
            }
        }
    }
}