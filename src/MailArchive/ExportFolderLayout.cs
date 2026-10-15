using XstReader;

namespace MailArchive;

public static class ExportFolderLayout
{
    public const string ImportFolderName = "Перенесённая почта";

    // OST service containers belong to the store, not to the visible mailbox tree.
    // Only unwrap the top of the archive; a user's nested folder named IPM_SUBTREE
    // must remain a real folder. Messages in a wrapper are copied to its parent.
    private static bool IsIpm(Folder folder) => string.Equals(folder.Name, "IPM_SUBTREE", StringComparison.OrdinalIgnoreCase);
    private static bool IsStoreBranch(Folder folder) => folder.Folders.Any(IsIpm)
        && new[] { "Root - Mailbox", "Root - Public", "Root - Public Folders", "Корень - п/я", "Корень - Общие" }
            .Contains(folder.Name, StringComparer.OrdinalIgnoreCase);

    public static bool IsWrapper(Folder folder, int parentDepth) =>
        (parentDepth == 0 && (IsIpm(folder) || IsStoreBranch(folder)))
        || (parentDepth == 1 && IsIpm(folder) && folder.ParentFolder is not null && IsStoreBranch(folder.ParentFolder));

    public static string UniqueName(string? original, ISet<string> used)
    {
        var name = string.IsNullOrWhiteSpace(original) ? "Без имени" : original;
        var candidate = name;
        for (int suffix = 2; !used.Add(candidate); suffix++) candidate = $"{name} ({suffix})";
        return candidate;
    }

    public static Folder InitialFolder(Folder root)
    {
        IEnumerable<Folder> Walk(Folder folder)
        {
            yield return folder;
            foreach (var child in folder.Folders) foreach (var descendant in Walk(child)) yield return descendant;
        }
        var folders = Walk(root).ToList();
        bool Inbox(Folder f) => string.Equals(f.Name, "Входящие", StringComparison.OrdinalIgnoreCase)
            || string.Equals(f.Name, "Inbox", StringComparison.OrdinalIgnoreCase);
        return folders.FirstOrDefault(f => Inbox(f) && f.ContentCount > 0)
            ?? folders.FirstOrDefault(f => f.ContentCount > 0) ?? folders.FirstOrDefault(Inbox) ?? root;
    }
}
