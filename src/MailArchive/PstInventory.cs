namespace MailArchive;

public static class PstInventory
{
    public static void Verify(ConversionReport report, List<PstFolderInfo> folders)
    {
        report.OutlookReadBackVerified = false;
        report.OutputFolders = folders;
        report.OutputMessagesVerified = folders.Sum(f => f.Messages);
        if (report.OutputMessagesVerified != report.Converted || report.ContentVerified != report.Converted)
            throw new IOException(L.F($"Проверка PST: Outlook видит {report.OutputMessagesVerified} элементов вместо {report.Converted} сохранённых писем."));
        report.OutlookReadBackVerified = report.Converted > 0;
    }

    public static string SkipSummary(ConversionReport report)
    {
        var groups = report.Issues.Where(i => i.Stage == "Unsupported")
            .GroupBy(i => i.ItemClass == "IPM.Contact" || (i.ItemClass is null && i.Reason.EndsWith(": IPM.Contact", StringComparison.Ordinal))
                ? L.T("Контакты (не письма)") : i.ItemClass == "IPM.Appointment" ? L.T("События календаря (не письма)") : i.Reason)
            .Select(g => $"{g.Key}: {g.Count()}");
        return string.Join("\n", groups);
    }
}
