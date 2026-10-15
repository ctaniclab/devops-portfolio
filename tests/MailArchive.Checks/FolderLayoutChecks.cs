using MailArchive;
using XstReader;

internal static class FolderLayoutChecks
{
    public static void Run(Action<bool, string> check)
    {
        var root = new Folder { Name = "Archive.ost" };
        Folder Child(Folder parent, string name)
        {
            var child = new Folder { Name = name, ParentFolder = parent };
            parent.Folders.Add(child); return child;
        }
        var store = Child(root, "Корень - п/я");
        var ipm = Child(store, "IPM_SUBTREE");
        var inbox = Child(ipm, "Входящие");
        var projects = Child(inbox, "Работа");
        var nested = Child(projects, "2026");
        var sent = Child(ipm, "Отправленные");
        var deleted = Child(ipm, "Удалённые");
        var similar = Child(ipm, "входящие");
        var slash = Child(ipm, "a/b"); var backslash = Child(ipm, "a\\b");
        var con = Child(ipm, "CON"); var escape = Child(ipm, "../../escape");
        var empty = Child(ipm, "Пустая папка");
        var userIpm = Child(projects, "IPM_SUBTREE");
        var layout = new MsgFolderLayout(root);
        string output = Path.Combine(Path.GetTempPath(), "MailArchive-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            check(layout.PathFor(output, inbox) == Path.Combine(output, "Входящие")
                && layout.PathFor(output, sent) == Path.Combine(output, "Отправленные")
                && layout.PathFor(output, deleted) == Path.Combine(output, "Удалённые"),
                "MSG export preserves inbox/sent/deleted under the named archive without OST wrappers");
            check(layout.PathFor(output, nested) == Path.Combine(output, "Входящие", "Работа", "2026")
                && layout.PathFor(output, userIpm) == Path.Combine(output, "Входящие", "Работа", "IPM_SUBTREE"),
                "Nested user folders retain their full hierarchy, including a user folder named IPM_SUBTREE");
            check(!string.Equals(layout.PathFor(output, inbox), layout.PathFor(output, similar), StringComparison.OrdinalIgnoreCase)
                && !string.Equals(layout.PathFor(output, slash), layout.PathFor(output, backslash), StringComparison.OrdinalIgnoreCase),
                "Case-insensitive and sanitized source-folder collisions cannot merge MSG messages");
            check(Path.GetFileName(layout.PathFor(output, con)) == "_CON"
                && Path.GetRelativePath(output, layout.PathFor(output, escape)).Split(Path.DirectorySeparatorChar).Length == 1,
                "Reserved Windows names and slash/traversal folder names stay safely below the output root");
            var folder = layout.EnsureDirectory(output, nested);
            File.WriteAllText(Path.Combine(folder, "selected.msg"), "test output");
            check(File.Exists(Path.Combine(output, "Входящие", "Работа", "2026", "selected.msg"))
                && !Directory.Exists(layout.PathFor(output, empty)) && !Directory.Exists(layout.PathFor(output, sent)),
                "Selecting one nested message creates only its own directory and ancestors");
            var first = ArchiveUtilities.NewMsgRoot(output, "mailbox.pst"); Directory.CreateDirectory(first);
            File.WriteAllText(Path.Combine(first, "preserve.msg"), "preserve");
            var second = ArchiveUtilities.NewMsgRoot(output, "mailbox.ost");
            check(Path.GetFileName(first) == "mailbox" && Path.GetFileName(second) == "mailbox (2)"
                && File.ReadAllText(Path.Combine(first, "preserve.msg")) == "preserve",
                "Both OST and PST use a source-named MSG root and preserve previous exports");
            var pstRoot = new Folder { Name = "mailbox.pst" };
            var pstInbox = Child(pstRoot, "Inbox");
            check(new MsgFolderLayout(pstRoot).PathFor(output, pstInbox) == Path.Combine(output, "Inbox"),
                "Exporting a PST uses its real folders without adding the source's root twice");
        }
        finally { Directory.Delete(output, true); }
    }
}
