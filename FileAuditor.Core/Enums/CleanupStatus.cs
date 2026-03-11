namespace FileAuditor.Core.Enums
{
    public enum CleanupStatus
    {
        Pending,
        Scanning,
        ReadyToDelete,
        ReadyToMove,
        Deleting,
        Moving,
        Completed,
        Failed,
        Cancelled
    }
}