using System.Security.Cryptography;
using System.Text;
using MailArchive;
using XstReader;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    checks++; Console.WriteLine("PASS: " + name);
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (IOException) { Check(true, name); return; }
    throw new Exception("Expected IOException: " + name);
}

ReaderRegressionChecks.Run(Check);
FeatureChecks.Run(Check);
ExportChecks.Run(Check);
ContentChecks.Run(Check);
TransferChecks.Run(Check);
ThemeChecks.Run(Check);
TimingChecks.Run(Check);
FolderLayoutChecks.Run(Check);
var recoveryReport = new ConversionReport();
var unreadable = FolderReadPolicy.Read(() => throw new IndexOutOfRangeException("bad table"),
    () => [], "Входящие", recoveryReport);
var readable = FolderReadPolicy.Read(() => [new Message { Subject = "Next folder" }],
    () => [], "Другая папка", recoveryReport);
Check(unreadable.Count == 0 && readable.Count == 1 && recoveryReport.FolderErrors == 1,
    "Unreadable folder is reported while following folders remain available");
Check(recoveryReport.Issues.Single().Stage == "ReadFolder" && recoveryReport.Issues.Single().Details!.Contains("IndexOutOfRangeException"),
    "Folder failure report retains its stage and exception details");
FolderReadPolicy.Read(() => [], () => [new ReadIssue("1234", true, "Unreadable header", "details")], "Folder", recoveryReport);
Check(recoveryReport.Failed == 1 && recoveryReport.Examined == 1, "Lost message header is counted as a failure, not an empty folder");
int refusedMetadata = 0;
Check(!MetadataCopy.TrySet("ReceivedTime", () => throw new System.Runtime.InteropServices.COMException("read-only", unchecked((int)0x80070005)),
        (_, _) => refusedMetadata++) && refusedMetadata == 1,
    "Outlook read-only metadata is reported without discarding the message");

Check(ArchiveUtilities.HtmlToText("<p>Привет &amp; мир</p><script>alert(1)</script><style>body{}</style><p>Тест</p>")
    == "Привет & мир\nТест", "HTML body text; scripts and styles excluded");
Check(ArchiveUtilities.HtmlToText(null) == "", "Missing HTML body");
Check(ArchiveUtilities.SafeAttachmentName("../../outside.txt") == "outside.txt", "Unix attachment traversal blocked");
Check(ArchiveUtilities.SafeAttachmentName(@"C:\folder\..\outside.txt") == "outside.txt", "Windows attachment traversal blocked");
Check(ArchiveUtilities.SafeAttachmentName("NUL.txt") == "_NUL.txt", "Windows device name blocked");
Check(ArchiveUtilities.SafeAttachmentName("..") == "attachment.bin", "Empty attachment name fallback");
Check(ArchiveUtilities.SafeAttachmentName("a:b?.txt") == "a_b_.txt", "Invalid attachment characters removed");
Check(MessageBodies.Html(new Message { Html = Encoding.UTF8.GetBytes("<p>Привет</p>") }) == "<p>Привет</p>", "UTF-8 HTML without codepage preserves Cyrillic");
Check(MessageBodies.Html(new Message { Html = Encoding.GetEncoding(1251).GetBytes("<meta charset=windows-1251><p>Привет</p>") })
    .Contains("Привет"), "HTML charset fallback decodes Windows-1251");

var temp = Path.Combine(Path.GetTempPath(), "MailArchive-Checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var existing = Path.Combine(temp, "existing.pst");
    File.WriteAllText(existing, "preserve me");
    Reject(() => ArchiveUtilities.ValidateDestination("source.ost", existing), "Existing PST overwrite rejected");
    Reject(() => ArchiveUtilities.ValidateDestination(existing, existing), "Same input/output rejected");
    Reject(() => ArchiveUtilities.ValidateDestination("source.ost", Path.Combine(temp, "out.ost")), "Wrong output extension rejected");
    Check(File.ReadAllText(existing) == "preserve me", "Destination validation preserves existing content");
    var invalid = Path.Combine(temp, "invalid.pst");
    File.WriteAllText(invalid, "not a PST");
    bool rejected = false;
    try { new XstFile(invalid).ReadFolderTree(); }
    catch (Exception) { rejected = true; }
    Check(rejected, "Reader rejects invalid PST");
}
finally { Directory.Delete(temp, true); }

if (args.Length != 2) throw new ArgumentException("Supply public fixture paths: example-2013.ost dist-list.pst. Fixture tests are required, not skipped.");
foreach (var path in args)
{
    var before = SHA256.HashData(File.ReadAllBytes(path));
    var file = new XstFile(path);
    var root = file.ReadFolderTree();
    var outline = ArchiveOutline.Read(file, root, false);
    IEnumerable<ArchiveFolder> OutlineNodes(ArchiveFolder folder)
    { yield return folder; foreach (var child in folder.Children) foreach (var descendant in OutlineNodes(child)) yield return descendant; }
    var indexed = outline.Folders.SelectMany(OutlineNodes).ToList();
    Check(indexed.Any(f => f.Messages > 0) && indexed.All(f => f.Messages > 0 || f.Children.Count > 0 || f.ReadWarning), "Actual archive outline omits empty leaves while preserving mail and unreadable folders");
    if (path.EndsWith(".ost", StringComparison.OrdinalIgnoreCase)) Check(indexed.Any(f => f.Source.ContainerClass == "IPF.Imap" && f.Messages > 0), "IMAP mail containers remain visible in the simplified archive tree");
    IEnumerable<Folder> AllFolders(Folder folder)
    {
        yield return folder;
        foreach (var child in folder.Folders)
            foreach (var descendant in AllFolders(child)) yield return descendant;
    }
    var populated = AllFolders(root).First(f => f.ContentCount > 0);
    var headerCount = file.ReadMessages(populated).Count;
    var selected = MessageSelection.FromMessage(populated.Messages[0]);
    var selectedFolder = selected.ResolveFolder(root);
    var chosen = selected.ResolveMessage(file.ReadMessages(selectedFolder));
    Check(chosen.Nid.dwValue == selected.MessageId && chosen.Folder.Nid.dwValue == selected.FolderId,
        $"{Path.GetExtension(path)} single-message selection resolves exact folder and message IDs");
    bool rejectsWrong = false;
    try { new MessageSelection(selected.FolderId, 0xFFFFFFE4).ResolveMessage(file.ReadMessages(selectedFolder)); }
    catch (InvalidOperationException) { rejectsWrong = true; }
    Check(rejectsWrong, "Missing single-message ID fails instead of exporting a different message");
    populated.ContentCount = 0;
    Check(headerCount > 0 && file.ReadMessages(populated).Count == headerCount,
        $"{Path.GetExtension(path)} messages remain visible with a stale zero cached count");
    int folders = 0, messages = 0, bodies = 0, rtfBodies = 0;
    bool knownBody = false;
    void Visit(Folder folder)
    {
        folders++;
        foreach (var message in file.ReadMessages(folder))
        {
            messages++;
            file.ReadMessageDetails(message);
            var body = message.Body ?? MessageBodies.Html(message);
            if (body.Length > 0 || message.RtfCompressed?.Length > 0) bodies++;
            if (message.RtfCompressed is { Length: > 0 })
            {
                var prepared = PreparedBody.FromMessage(message);
                if (string.IsNullOrWhiteSpace(prepared.Html)) throw new Exception("RTF fixture produced an empty formatted body.");
                rtfBodies++;
            }
            if (body.Contains("This is an e-mail message sent automatically by Microsoft Outlook while testing the settings for your account.")) knownBody = true;
            message.ClearContents();
        }
        foreach (var child in folder.Folders) Visit(child);
    }
    Visit(root);
    if (rtfBodies > 0) Check(true, $"Actual {Path.GetExtension(path)} RTF fixture bodies prepare successfully: {rtfBodies}");
    Check(folders > 1 && messages > 0 && bodies > 0, $"Actual {Path.GetExtension(path)} folder/header/body reading: {folders} folders, {messages} items, {bodies} bodies");
    if (path.EndsWith(".ost", StringComparison.OrdinalIgnoreCase)) Check(knownBody, "OST known Outlook test email body matches fixture expectation");
    Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Source archive SHA256 unchanged after browsing");
}
Console.WriteLine($"Completed: {checks} checks passed.");
