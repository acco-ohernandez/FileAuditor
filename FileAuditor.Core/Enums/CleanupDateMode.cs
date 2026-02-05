namespace FileAuditor.Core.Enums
{
    public enum CleanupDateMode
    {
        OlderThan,      // Older than X days
        NewerThan,      // Newer than X days
        DateRange,      // Between two specific dates
        ExactDate       // On a specific date
    }
}