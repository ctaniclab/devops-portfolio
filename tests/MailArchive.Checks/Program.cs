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
    int folders = 0, messages = 0, bodies = 0;
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
            if (body.Contains("This is an e-mail message sent automatically by Microsoft Outlook while testing the settings for your account.")) knownBody = true;
            message.ClearContents();
        }
        foreach (var child in folder.Folders) Visit(child);
    }
    Visit(root);
    Check(folders > 1 && messages > 0 && bodies > 0, $"Actual {Path.GetExtension(path)} folder/header/body reading: {folders} folders, {messages} items, {bodies} bodies");
    if (path.EndsWith(".ost", StringComparison.OrdinalIgnoreCase)) Check(knownBody, "OST known Outlook test email body matches fixture expectation");
    Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Source archive SHA256 unchanged after browsing");
}
Console.WriteLine($"Completed: {checks} checks passed.");
