using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using MailArchive;
using XstReader;

// Requires interactive Windows with a configured classic Outlook profile.
// Uses the public sample OST only; never run against a live mailbox.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
if (args is ["--preview-smoke"]) { await PreviewSmoke.RunAsync(); return; }
if (args.Length != 2) throw new ArgumentException("Usage: WindowsChecks <example-2013.ost> <new-output.pst>");
for (int pass = 1; pass <= 3; pass++)
{
    string outputPath = Path.GetFullPath(args[1]) + (pass == 1 ? "" : $".pass-{pass}.pst");
    Console.WriteLine($"Conversion pass {pass}/3");
    await RunPassAsync(outputPath);
}

async Task RunPassAsync(string outputPath)
{
    var accountBefore = await AccountSnapshotAsync();
    var before = SHA256.HashData(File.ReadAllBytes(args[0]));
    var result = await OutlookConverter.ConvertAsync(Path.GetFullPath(args[0]), Path.GetFullPath(outputPath),
        new Progress<ConversionProgress>(), CancellationToken.None);
    if (!result.Report.Completed || result.Report.Converted == 0 || result.Report.Failed != 0)
        throw new Exception("Outlook conversion failed. Inspect report: " + result.ReportPath);
    if (result.Report.DatesVerified + result.Report.MessagesWithoutSourceDates != result.Report.Converted)
        throw new Exception("Original message dates were not accounted for.");
    if (!result.Report.OutlookReadBackVerified || result.Report.OutputMessagesVerified != result.Report.Converted)
        throw new Exception("Saved Outlook folder counts were not verified.");
    if (result.Report.OutputFolders.Any(f => f.Path.Contains("\\IPM_SUBTREE\\", StringComparison.OrdinalIgnoreCase)))
        throw new Exception("OST service wrappers leaked into the visible output mailbox.");
    if (!before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(args[0])))) throw new Exception("Source archive changed!");
    var archive = new XstFile(outputPath);
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
    var single = await OutlookConverter.ConvertAsync(Path.GetFullPath(args[0]), Path.GetFullPath(outputPath) + ".single.pst",
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
    var msgs = await OutlookConverter.ConvertAsync(Path.GetFullPath(args[0]), Path.GetFullPath(outputPath) + ".msg-files",
        new Progress<ConversionProgress>(), CancellationToken.None, selections: batch, format: ExportFormat.Msg);
    if (!msgs.Report.Completed || msgs.Report.Converted != 2 || msgs.Report.ContentVerified != 2 || msgs.Report.MsgFiles.Count != 2)
        throw new Exception("Batch MSG conversion failed: " + msgs.ReportPath);
    var expectedMsgLayout = new MsgFolderLayout(sourceRoot);
    var expectedMsgPaths = batch.Select(s =>
    {
        var sourceFolder = s.ResolveFolder(sourceRoot);
        var header = s.ResolveMessage(sourceArchive.ReadMessages(sourceFolder));
        return Path.Combine(expectedMsgLayout.PathFor(msgs.Report.Destination, sourceFolder), ArchiveUtilities.MsgFileName(header.Subject, s.FolderId, s.MessageId));
    }).Order().ToArray();
    if (!msgs.Report.MsgFiles.Order().SequenceEqual(expectedMsgPaths)) throw new Exception("MSG source-folder structure was not preserved.");
    foreach (var output in msgs.Report.MsgFiles)
    {
        using var stream = File.OpenRead(output);
        var magic = new byte[8]; stream.ReadExactly(magic);
        if (!magic.SequenceEqual(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 })) throw new Exception("MSG is not a compound document.");
    }
    Console.WriteLine("PASS: two selected messages become two Unicode MSG files with verified content.");

    var selectedPst = await OutlookConverter.ConvertAsync(Path.GetFullPath(args[0]), outputPath + ".selected.pst",
        new Progress<ConversionProgress>(), CancellationToken.None, selections: batch);
    if (selectedPst.Report.OutputFolders.Any(f => f.Path.Split('\\').Contains("Перенесённая почта")))
        throw new Exception("Unnecessary import wrapper remained in the PST.");
    if (!selectedPst.Report.Completed || selectedPst.Report.Converted != 2 || selectedPst.Report.ContentVerified != 2
        || selectedPst.Report.OutputMessagesVerified != 2 || !selectedPst.Report.OutlookReadBackVerified)
        throw new Exception("Two-message PST import failed: " + selectedPst.ReportPath);
    var selectedArchive = new XstFile(outputPath + ".selected.pst");
    var subjects = Walk(selectedArchive.ReadFolderTree()).SelectMany(f => selectedArchive.ReadMessages(f)).Select(m => m.Subject ?? "").Order().ToArray();
    var expectedSubjects = batch.Select(s => s.ResolveMessage(sourceArchive.ReadMessages(s.ResolveFolder(sourceRoot))).Subject ?? "").Order().ToArray();
    if (!subjects.SequenceEqual(expectedSubjects)) throw new Exception("Independent PST reader found different selected messages.");
    if (!before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(args[0])))) throw new Exception("Source changed after repeated conversions.");
    Console.WriteLine("PASS: two selected letters imported into PST; independent reader confirms their subjects; source unchanged.");
    if (!accountBefore.SetEquals(await AccountSnapshotAsync()))
        throw new Exception("Default account Outbox/Drafts/Sent/Deleted folders changed during conversion. Use an isolated idle test profile to exclude unrelated mail activity.");
    Console.WriteLine("PASS: default-account folder EntryIDs unchanged after PST and MSG conversion.");
}

static Task<HashSet<string>> AccountSnapshotAsync()
{
    var completion = new TaskCompletionSource<HashSet<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
    var thread = new Thread(() =>
    {
        var handles = new List<object>();
        dynamic Keep(object value) { handles.Add(value); return value; }
        try
        {
            dynamic app = Keep(Activator.CreateInstance(Type.GetTypeFromProgID("Outlook.Application")!)!);
            dynamic session = Keep(app.GetNamespace("MAPI"));
            var snapshot = new HashSet<string>(StringComparer.Ordinal);
            foreach (int kind in new[] { 4, 16, 5, 3 }) // Outbox, Drafts, Sent, Deleted
            {
                dynamic folder = Keep(session.GetDefaultFolder(kind));
                dynamic items = Keep(folder.Items);
                for (int i = 1; i <= (int)items.Count; i++)
                {
                    dynamic item = items[i];
                    try { snapshot.Add(kind + ":" + (string)folder.StoreID + ":" + (string)item.EntryID); }
                    finally { Marshal.ReleaseComObject(item); }
                }
            }
            completion.SetResult(snapshot);
        }
        catch (Exception error) { completion.SetException(error); }
        finally { foreach (var handle in handles.AsEnumerable().Reverse()) Marshal.ReleaseComObject(handle); }
    });
    thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
}
