namespace FileAuditor.Core.Enums
{
    public enum CleanupDateMode
    {
        AnyDate,        // No date filtering
        OlderThan,      // LastWriteTime before a specified date+time
        NewerThan,      // LastWriteTime after a specified date+time
        DateRange,      // Between two specific dates
        ExactDate       // On a specific date
    }
}