namespace FileAuditor.Core.Services
{
    public interface IBoxDriveHandler
    {
        bool IsBoxDrivePath(string path);
        Task<bool> RefreshDirectoryAsync(string path, int timeoutSeconds, CancellationToken cancellationToken);
        Task<bool> EnsureDirectoryAvailableAsync(string path, CancellationToken cancellationToken);
        BoxDriveStatus GetDirectoryStatus(string path);
    }

    public enum BoxDriveStatus
    {
        NotBoxDrive,
        Available,
        OnlineOnly,
        Syncing,
        Error
    }
}