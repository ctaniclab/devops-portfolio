using System.IO;
using System.Security.Cryptography;
using System.Text;
using MailArchive;
using XstReader;

// Requires interactive Windows with a configured classic Outlook profile.
// Uses the public sample OST only; never run against a live mailbox.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
if (args.Length != 2) throw new ArgumentException("Usage: WindowsChecks <example-2013.ost> <new-output.pst>");
var before = SHA256.HashData(File.ReadAllBytes(args[0]));
var result = await OutlookConverter.ConvertAsync(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]),
    new Progress<ConversionProgress>(), CancellationToken.None);
if (!result.Report.Completed || result.Report.Converted == 0 || result.Report.Failed != 0)
    throw new Exception("Outlook conversion failed. Inspect report: " + result.ReportPath);
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
