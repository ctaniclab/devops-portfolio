using MailArchive;
using XstReader;

internal static class ExportChecks
{
    public static void Run(Action<bool, string> check)
    {
        var root = new Folder { Name = "Archive" };
        var emptyInbox = new Folder { Name = "Входящие", ParentFolder = root };
        var store = new Folder { Name = "Корень - п/я", ParentFolder = root };
        var ipm = new Folder { Name = "IPM_SUBTREE", ParentFolder = store };
        var realInbox = new Folder { Name = "Входящие", ContentCount = 109, ParentFolder = ipm };
        root.Folders.AddRange([emptyInbox, store]); store.Folders.Add(ipm); ipm.Folders.Add(realInbox);
        check(ExportFolderLayout.InitialFolder(root) == realInbox,
            "Opening a converted PST prefers the populated inbox over an empty default inbox");
        check(ExportFolderLayout.IsWrapper(store, 0) && ExportFolderLayout.IsWrapper(ipm, 1)
            && !ExportFolderLayout.IsWrapper(realInbox, 2), "OST mailbox and IPM service wrappers are flattened while the inbox is preserved");
        var project = new Folder { Name = "Projects", ParentFolder = root };
        var userIpm = new Folder { Name = "IPM_SUBTREE", ParentFolder = project }; project.Folders.Add(userIpm);
        check(!ExportFolderLayout.IsWrapper(project, 0) && !ExportFolderLayout.IsWrapper(userIpm, 1),
            "A normal user's Projects/IPM_SUBTREE branch is preserved");
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var first = ExportFolderLayout.UniqueName("Inbox", used);
        var second = ExportFolderLayout.UniqueName("inbox", used);
        check(first == "Inbox" && second == "inbox (2)" && used.Count == 2,
            "Equal folder names from separate store branches remain separate instead of overwriting mail");
        var report = new ConversionReport { Converted = 109, ContentVerified = 109, Skipped = 3 };
        PstInventory.Verify(report, [new("PST/Перенесённая почта/Входящие", 65), new("PST/Перенесённая почта/Отправленные", 44)]);
        check(report.OutlookReadBackVerified && report.OutputMessagesVerified == 109, "Saved folder inventory accounts for every converted message");
        bool mismatchRejected = false;
        try { PstInventory.Verify(report, [new("PST/Входящие", 0)]); }
        catch (IOException) { mismatchRejected = true; }
        check(mismatchRejected && !report.OutlookReadBackVerified, "An empty output cannot pass verification after reporting 109 saved messages");
        report.Issues.AddRange(Enumerable.Range(0, 3).Select(_ => new ConversionIssue("Contacts", "", "Не поддерживается тип элемента: IPM.Contact", "Unsupported", ItemClass: "IPM.Contact")));
        report.Issues.Add(new("Inbox", "", "Property refused", "Metadata"));
        check(PstInventory.SkipSummary(report) == "Контакты (не письма): 3", "Skip summary explains three contacts without labeling metadata warnings as lost mail");
    }
}
