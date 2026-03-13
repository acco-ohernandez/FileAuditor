namespace FileAuditor.WPF.ViewModels
{
    /// <summary>
    /// Controls whether the Move to Folder tab accepts a single source+destination
    /// pair or a batch list (two-column CSV format).
    /// </summary>
    public enum MoveInputMode
    {
        Single,
        Batch
    }
}
