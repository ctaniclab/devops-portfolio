using XstReader;

namespace MailArchive;

public sealed record ArchiveFolder(Folder Source, int Messages, List<ArchiveFolder> Children, bool ReadWarning = false);
public sealed record ArchiveOutline(List<ArchiveFolder> Folders, List<string> Warnings)
{
    public static ArchiveOutline Read(XstFile archive, Folder root, bool includeService)
    {
        var warnings = new List<string>();
        List<ArchiveFolder> Visit(Folder folder, int depth, bool rootNode = false)
        {
            if (!rootNode && !includeService && ExportFolderLayout.IsServiceBranch(folder, depth - 1))
                return [];
            bool nonMailContainer = !includeService && new[] { "IPF.Appointment", "IPF.Contact", "IPF.Task", "IPF.Journal", "IPF.StickyNote", "IPF.Configuration" }
                .Any(kind => folder.ContainerClass?.StartsWith(kind, StringComparison.OrdinalIgnoreCase) == true);
            int count = 0;
            bool readWarning = false;
            try
            {
                count = nonMailContainer ? 0 : archive.ReadMessages(folder).Count;
                readWarning = !nonMailContainer && archive.LastReadWarnings.Count > 0;
                if (!nonMailContainer) foreach (var warning in archive.LastReadWarnings) warnings.Add(folder.Path + ": " + warning.Reason);
            }
            catch (Exception error) when (error is IOException or XstException or ArgumentException or IndexOutOfRangeException)
            { readWarning = true; warnings.Add(folder.Path + ": " + error.Message); }
            finally { folder.Messages.Clear(); }
            var children = folder.Folders.SelectMany(f => Visit(f, depth + 1)).ToList();
            if (count == 0 && !readWarning && (rootNode || ExportFolderLayout.IsWrapper(folder, depth - 1))) return children;
            if (count == 0 && children.Count == 0 && !readWarning) return [];
            return [new(folder, count, children, readWarning)];
        }
        return new(Visit(root, 0, true), warnings);
    }
}
