using XstReader;

namespace MailArchive;

internal static class FolderReadPolicy
{
    public static List<Message> Read(XstFile archive, Folder folder, ConversionReport report) =>
        Read(() => archive.ReadMessages(folder), () => archive.LastReadWarnings, folder.Path, report);

    internal static List<Message> Read(Func<List<Message>> read, Func<IEnumerable<ReadIssue>> warnings,
        string path, ConversionReport report)
    {
        List<Message> messages;
        try { messages = read(); }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            report.FolderErrors++;
            report.Issues.Add(new(path, "", L.T("Не удалось прочитать папку: ") + e.Message, "ReadFolder", e.ToString()));
            // The traversal still visits this folder's children and its siblings.
            return [];
        }
        foreach (var warning in warnings())
        {
            if (warning.MessageSkipped) { report.Failed++; report.Examined++; }
            report.Issues.Add(new(path, warning.NodeId, warning.Reason,
                warning.MessageSkipped ? "ReadHeader" : "HeaderRecovery", warning.Details));
        }
        return messages;
    }
}
