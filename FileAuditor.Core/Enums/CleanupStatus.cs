namespace FileAuditor.Core.Enums
{
    public enum CleanupStatus
    {
        Pending,
        Scanning,
        ReadyToDelete,
        Deleting,
        Completed,
        Failed,
        Cancelled
    }
}