namespace MailArchive;

internal static class OutputFileStatus
{
    // A saved PST may remain open inside Outlook after RemoveStore. Merely inspect its existing
    // pathname; never move, rename or copy a live store to declare conversion complete.
    public static void Apply(ConversionReport report, string target, bool detached)
    {
        bool exists = File.Exists(target);
        if (report.Completed && (!detached || !exists))
        {
            report.Completed = false;
            report.FatalError ??= exists ? L.T("Outlook не отключил выходное хранилище.") : L.T("Outlook не создал выходной PST.");
        }
        if (!report.Completed && exists) report.RetainedPartialFile = target;
    }
}
