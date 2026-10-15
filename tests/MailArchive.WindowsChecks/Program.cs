using System.IO;
using System.Security.Cryptography;
using System.Text;
using MailArchive;
using XstReader;

// Requires interactive Windows with a configured classic Outlook profile.
// Uses the public sample OST only; never run against a live mailbox.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
if (args is ["--preview-smoke"]) { await PreviewSmoke.RunAsync(); return; }
if (args.Length != 2) throw new ArgumentException("Usage: WindowsChecks <example-2013.ost> <new-output.pst>");
var before = SHA256.HashData(File.ReadAllBytes(args[0]));
var result = await OutlookConverter.ConvertAsync(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]),
    new Progress<ConversionProgress>(), CancellationToken.None);
if (!result.Report.Completed || result.Report.Converted == 0 || result.Report.Failed != 0)
    throw new Exception("Outlook conversion failed. Inspect report: " + result.ReportPath);
if (!result.Report.OutlookReadBackVerified || result.Report.OutputMessagesVerified != result.Report.Converted)
    throw new Exception("Saved Outlook folder counts were not verified.");
if (result.Report.OutputFolders.Any(f => f.Path.Contains("\\IPM_SUBTREE\\", StringComparison.OrdinalIgnoreCase)))
    throw new Exception("OST service wrappers leaked into the visible output mailbox.");
if (!before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(args[0])))) throw new Exception("Source archive changed!");
var archive = new XstFile(args[1]);
var root = archive.ReadFolderTree();
int count = 0;
bool knownBody = false;
void Visit(Folder folder)
{
    foreach (var message in archive.ReadMessages(folder))
    {
        archive.ReadMessageDetails(message);
        count++;
        var body = message.Body ?? MessageBodies.Html(message);
        if (body.Contains("This is an e-mail message sent automatically by Microsoft Outlook while testing the settings for your account."))
            knownBody = true;
    }
    foreach (var child in folder.Folders) Visit(child);
}
Visit(root);
if (count != result.Report.Converted || !knownBody) throw new Exception("Output PST content/count check failed.");
Console.WriteLine($"PASS: Outlook converted {count} messages, known body preserved, source SHA256 unchanged.");
// Reopen the source and independently exercise selection-only conversion into another new file.
var sourceArchive = new XstFile(args[0]);
var sourceRoot = sourceArchive.ReadFolderTree();
IEnumerable<Folder> Walk(Folder folder)
{
    yield return folder;
    foreach (var child in folder.Folders) foreach (var descendant in Walk(child)) yield return descendant;
}
MessageSelection? selection = null;
foreach (var folder in Walk(sourceRoot))
{
    var candidate = sourceArchive.ReadMessages(folder).FirstOrDefault();
    if (candidate is null) continue;
    sourceArchive.ReadMessageDetails(candidate);
    if (candidate.Properties.Any(p => (ushort)p.Tag == 0x001A && (string?)p.Value == "IPM.Note"))
    { selection = MessageSelection.FromMessage(candidate); break; }
}
if (selection is null) throw new Exception("No mail message found for single-message check.");
var single = await OutlookConverter.ConvertAsync(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]) + ".single.pst",
    new Progress<ConversionProgress>(), CancellationToken.None, selection);
if (!single.Report.Completed || single.Report.Examined != 1 || single.Report.Converted != 1 || single.Report.Failed != 0)
    throw new Exception("Single-message Outlook conversion failed: " + single.ReportPath);
Console.WriteLine("PASS: selected-message conversion examines and saves exactly one message.");
var allSelected = new List<MessageSelection>();
foreach (var folder in Walk(sourceRoot))
{
    foreach (var candidate in sourceArchive.ReadMessages(folder))
    {
        sourceArchive.ReadMessageDetails(candidate);
        if (candidate.Properties.Any(p => (ushort)p.Tag == 0x001A && (string?)p.Value == "IPM.Note")) allSelected.Add(MessageSelection.FromMessage(candidate));
        candidate.ClearContents();
    }
}
var batch = allSelected.Take(2).ToArray();
if (batch.Length != 2) throw new Exception("Fixture lacks two messages for batch conversion.");
var msgs = await OutlookConverter.ConvertAsync(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]) + ".msg-files",
    new Progress<ConversionProgress>(), CancellationToken.None, selections: batch, format: ExportFormat.Msg);
if (!msgs.Report.Completed || msgs.Report.Converted != 2 || msgs.Report.ContentVerified != 2 || msgs.Report.MsgFiles.Count != 2)
    throw new Exception("Batch MSG conversion failed: " + msgs.ReportPath);
foreach (var output in msgs.Report.MsgFiles)
{
    using var stream = File.OpenRead(output);
    var magic = new byte[8]; stream.ReadExactly(magic);
    if (!magic.SequenceEqual(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 })) throw new Exception("MSG is not a compound document.");
}
Console.WriteLine("PASS: two selected messages become two Unicode MSG files with verified content.");
