namespace FileAuditor.Core.Enums
{
    public enum CleanupDateMode
    {
        AnyDate,        // No date filtering
        OlderThan,      // Older than X days
        NewerThan,      // Newer than X days
        DateRange,      // Between two specific dates
        ExactDate       // On a specific date
    }
}