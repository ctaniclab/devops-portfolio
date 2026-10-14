using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using XstReader;
using Message = XstReader.Message;

namespace MailArchive;

public sealed record ConversionProgress(int Examined, int Converted, int Failed, int Skipped, string Folder);
public sealed record ConversionResult(ConversionReport Report, string ReportPath);

// Outlook automation must stay on one STA thread, never on the UI thread or a thread-pool thread.
public static class OutlookConverter
{
    public static Task<ConversionResult> ConvertAsync(string source, string target,
        IProgress<ConversionProgress> progress, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<ConversionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(Convert(source, target, progress, cancellation)); }
            catch (Exception e) { completion.SetException(e); }
        }) { IsBackground = true, Name = "Outlook PST writer" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static ConversionResult Convert(string source, string target,
        IProgress<ConversionProgress> progress, CancellationToken cancellation)
    {
        ArchiveUtilities.ValidateDestination(source, target);
        var id = Guid.NewGuid().ToString("N");
        var partial = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target))!,
            Path.GetFileNameWithoutExtension(target) + "." + id + ".partial.pst");
        var reportPath = target + "." + id + ".conversion.json";
        var report = new ConversionReport { Source = source, Destination = target };
        using var session = new ComScope();
        dynamic? outlookNamespace = null;
        dynamic? targetRoot = null;
        bool detached = false;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            using var sourceGuard = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            // Read source before creating an output or starting Outlook.
            var archive = new XstFile(source);
            var root = archive.ReadFolderTree();
            var type = Type.GetTypeFromProgID("Outlook.Application")
                ?? throw new InvalidOperationException("Не найден классический Outlook. Новый Outlook не поддерживает COM. Установите и настройте классический Outlook.");
            dynamic app = session.Keep(Activator.CreateInstance(type)!);
            outlookNamespace = session.Keep(app.GetNamespace("MAPI"));
            // 2 = olStoreUnicode. Do not Logon/Logoff or Quit the user's Outlook session.
            outlookNamespace.AddStoreEx(partial, 2);
            dynamic stores = session.Keep(outlookNamespace.Stores);
            for (int index = 1; index <= (int)stores.Count; index++)
            {
                using var scope = new ComScope();
                dynamic store = scope.Keep(stores[index]);
                if (string.Equals((string)store.FilePath, partial, StringComparison.OrdinalIgnoreCase))
                {
                    targetRoot = session.Keep(store.GetRootFolder());
                    break;
                }
            }
            if (targetRoot is null) throw new IOException("Outlook не подключил созданный PST.");
            targetRoot.Name = Path.GetFileNameWithoutExtension(target);
            // Dedicated wrapper avoids colliding with default PST folders and keeps the source tree.
            using (var scope = new ComScope())
            {
                dynamic folders = scope.Keep(targetRoot.Folders);
                dynamic importedRoot = scope.Keep(folders.Add("Импорт OST", 6)); // olFolderInbox = mail folder
                CopyFolder(archive, root, importedRoot, report, progress, cancellation);
            }
            cancellation.ThrowIfCancellationRequested();
            report.Completed = report.FolderErrors == 0;
        }
        catch (OperationCanceledException) { report.Cancelled = true; }
        catch (Exception e)
        {
            report.FatalError = e.Message;
            report.Issues.Add(new("", "", e.Message, "Fatal", e.ToString()));
        }
        finally
        {
            if (targetRoot is not null && outlookNamespace is not null)
            {
                try { outlookNamespace.RemoveStore(targetRoot); detached = true; }
                catch (Exception e)
                {
                    report.Completed = false;
                    report.FatalError = (report.FatalError is null ? "" : report.FatalError + "\n")
                        + "Не удалось отключить выходной PST от Outlook: " + e.Message;
                }
            }
            session.Dispose();
        }

        if (report.Completed && detached)
        {
            try { File.Move(partial, target, overwrite: false); }
            catch (Exception e) { report.Completed = false; report.FatalError = "Не удалось завершить выходной файл: " + e.Message; }
        }
        if (!report.Completed && File.Exists(partial)) report.RetainedPartialFile = partial;
        report.FinishedUtc = DateTime.UtcNow;
        // Never overwrite an existing report. A cancelled/failed conversion also gets a report.
        using (var stream = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write))
            JsonSerializer.Serialize(stream, report, new JsonSerializerOptions
                { WriteIndented = true, Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) });
        return new ConversionResult(report, reportPath);
    }

    private static void CopyFolder(XstFile archive, Folder source, dynamic destination,
        ConversionReport report, IProgress<ConversionProgress> progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var messages = FolderReadPolicy.Read(archive, source, report);
        foreach (var message in messages)
        {
            cancellation.ThrowIfCancellationRequested();
            report.Examined++;
            var stage = "ReadMessage";
            try
            {
                archive.ReadMessageDetails(message);
                var messageClass = ConvertString(message, 0x001A);
                if (messageClass != "IPM.Note")
                    throw new UnsupportedMessageException("Не поддерживается тип элемента: " + (messageClass ?? "неизвестен"));
                if (message.Attachments.Any(a => !a.IsFile))
                    throw new UnsupportedMessageException("Вложенное сообщение, OLE или ссылочное вложение: письмо пропущено целиком.");
                if (message.Attachments.Any(a => string.Equals(a.FileName, "smime.p7m", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a.MimeTag, "application/pkcs7-mime", StringComparison.OrdinalIgnoreCase)))
                    throw new UnsupportedMessageException("S/MIME требует отдельной обработки: письмо пропущено.");
                stage = "WriteMessage";
                WriteMessage(archive, message, (object)destination, (field, error) =>
                {
                    report.MetadataWarnings++;
                    report.Issues.Add(new(source.Path, message.Subject ?? "",
                        "Outlook не сохранил поле " + field + ": " + error.Message, "Metadata", error.ToString()));
                });
                report.Converted++;
            }
            catch (UnsupportedMessageException e)
            {
                report.Skipped++;
                report.Issues.Add(new(source.Path, message.Subject ?? "", e.Message, "Unsupported"));
            }
            catch (Exception e)
            {
                report.Failed++;
                report.Issues.Add(new(source.Path, message.Subject ?? "", e.Message, stage, e.ToString()));
            }
            finally { message.ClearContents(); message.Properties.Clear(); message.Recipients.Clear(); }
            progress.Report(new(report.Examined, report.Converted, report.Failed, report.Skipped, source.Path));
        }
        // Release cached headers as well: do not retain the whole mailbox in RAM during conversion.
        source.Messages.Clear();
        foreach (var child in source.Folders)
        {
            cancellation.ThrowIfCancellationRequested();
            using var scope = new ComScope();
            dynamic childFolder;
            try
            {
                dynamic folders = scope.Keep(destination.Folders);
                childFolder = scope.Keep(folders.Add(string.IsNullOrWhiteSpace(child.Name) ? "Без имени" : child.Name, 6));
            }
            catch (COMException error)
            {
                report.FolderErrors++;
                report.Issues.Add(new(child.Path, "", "Outlook не создал папку: " + error.Message, "CreateFolder", error.ToString()));
                continue;
            }
            CopyFolder(archive, child, childFolder, report, progress, cancellation);
        }
    }

    private static void WriteMessage(XstFile archive, Message message, object target, Action<string, Exception> metadataWarning)
    {
        dynamic folder = target;
        var temp = Path.Combine(Path.GetTempPath(), "MailArchive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        using var scope = new ComScope();
        dynamic? item = null;
        bool saved = false;
        try
        {
            dynamic items = scope.Keep(folder.Items);
            item = scope.Keep(items.Add("IPM.Note"));
            item.Subject = message.Subject ?? "";
            if (message.IsBodyHtml) item.HTMLBody = MessageBodies.Html(message);
            else if (message.IsBodyRtf && message.RtfCompressed is not null)
            {
                using var stream = new RtfDecompressor().Decompress(message.RtfCompressed, true);
                item.RTFBody = stream.ToArray();
            }
            else item.Body = message.Body ?? "";

            dynamic recipients = scope.Keep(item.Recipients);
            foreach (var recipient in message.Recipients.Where(r => (int)r.RecipientType is >= 1 and <= 3))
            {
                var address = recipient.EmailAddress ?? recipient.DisplayName;
                if (string.IsNullOrWhiteSpace(address)) continue;
                dynamic entry = scope.Keep(recipients.Add(address));
                entry.Type = (int)recipient.RecipientType;
                // No ResolveAll: Exchange may be unavailable; original address remains in the recipient row.
            }
            dynamic attachments = scope.Keep(item.Attachments);
            int number = 0;
            foreach (var attachment in message.Attachments)
            {
                // Unique private directory per attachment prevents duplicate names and path traversal.
                var directory = Path.Combine(temp, (++number).ToString());
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, ArchiveUtilities.SafeAttachmentName(attachment.FileName));
                archive.SaveAttachment(path, null, attachment);
                dynamic added = scope.Keep(attachments.Add(path, 1, Type.Missing, attachment.FileName ?? "attachment"));
                if (!string.IsNullOrEmpty(attachment.ContentId))
                {
                    dynamic attachmentProperties = scope.Keep(added.PropertyAccessor);
                    attachmentProperties.SetProperty(Dasl(0x3712001F), attachment.ContentId);
                    attachmentProperties.SetProperty(Dasl(0x7FFE000B), attachment.Hidden);
                }
            }
            dynamic properties = scope.Keep(item.PropertyAccessor);
            // Whitelist only common mail metadata. Never copy store-specific entry IDs or arbitrary properties.
            foreach (ushort tag in new ushort[] { 0x0C1A, 0x0C1E, 0x0C1F, 0x0042, 0x0064, 0x0065, 0x1035, 0x007D })
            {
                var value = ConvertString(message, tag);
                if (!string.IsNullOrEmpty(value))
                    MetadataCopy.TrySet(tag.ToString("X4"), () => properties.SetProperty(Dasl(((uint)tag << 16) | 0x001F), value), metadataWarning);
            }
            if (message.Received is DateTime received)
                MetadataCopy.TrySet("ReceivedTime", () => properties.SetProperty(Dasl(0x0E060040), received.ToUniversalTime()), metadataWarning);
            if (message.Submitted is DateTime sent)
                MetadataCopy.TrySet("SentOn", () => properties.SetProperty(Dasl(0x00390040), sent.ToUniversalTime()), metadataWarning);
            var originalFlags = message.Properties.FirstOrDefault(p => (ushort)p.Tag == 0x0E07)?.Value;
            int flags = originalFlags is null ? 1 : System.Convert.ToInt32(originalFlags);
            MetadataCopy.TrySet("UnRead", () => item.UnRead = (flags & 1) == 0, metadataWarning);
            // Set before first Save: preserve read/unsent state without transplanting attachment/store flags.
            MetadataCopy.TrySet("MessageFlags", () => properties.SetProperty(Dasl(0x0E070003), flags & 0x00000009), metadataWarning);
            item.Save();
            saved = true;
        }
        finally
        {
            if (!saved && item is not null)
            {
                try { item.Close(1); } catch { /* Best effort discard of unsaved item. */ }
            }
            scope.Dispose();
            try { Directory.Delete(temp, recursive: true); } catch { /* No source files are ever removed. */ }
        }
    }

    private static string? ConvertString(Message message, ushort tag)
    {
        object? value = message.Properties.FirstOrDefault(p => (ushort)p.Tag == tag)?.Value;
        return value as string;
    }
    private static string Dasl(uint tag) => "http://schemas.microsoft.com/mapi/proptag/0x" + tag.ToString("X8");
    private sealed class UnsupportedMessageException(string message) : Exception(message);

    private sealed class ComScope : IDisposable
    {
        private readonly List<object> values = [];
        public dynamic Keep(object value) { values.Add(value); return value; }
        public void Dispose()
        {
            for (int i = values.Count - 1; i >= 0; i--)
            {
                try { if (Marshal.IsComObject(values[i])) Marshal.ReleaseComObject(values[i]); }
                catch (InvalidComObjectException) { }
            }
            values.Clear();
        }
    }
}
