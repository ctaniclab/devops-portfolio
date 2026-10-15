using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using XstReader;
using Message = XstReader.Message;
using System.Security.Cryptography;

namespace MailArchive;

public sealed record ConversionProgress(int Examined, int Converted, int Failed, int Skipped, string Folder);
public sealed record ConversionResult(ConversionReport Report, string ReportPath);
public enum ExportFormat { Pst, Msg }

// Outlook automation must stay on one STA thread, never on the UI thread or a thread-pool thread.
public static class OutlookConverter
{
    public static Task<ConversionResult> ConvertAsync(string source, string target,
        IProgress<ConversionProgress> progress, CancellationToken cancellation, MessageSelection? selection = null, bool includeServiceFolders = false,
        IReadOnlyList<MessageSelection>? selections = null, ExportFormat format = ExportFormat.Pst)
    {
        var completion = new TaskCompletionSource<ConversionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(Convert(source, target, progress, cancellation, selection, includeServiceFolders, selections, format)); }
            catch (Exception e) { completion.SetException(e); }
        }) { IsBackground = true, Name = "Outlook PST writer" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static ConversionResult Convert(string source, string target,
        IProgress<ConversionProgress> progress, CancellationToken cancellation, MessageSelection? selection, bool includeServiceFolders,
        IReadOnlyList<MessageSelection>? selections, ExportFormat format)
    {
        if (format == ExportFormat.Pst) ArchiveUtilities.ValidateDestination(source, target);
        else ArchiveUtilities.ValidateMsgDestination(target);
        var chosen = selections ?? (selection is null ? null : new[] { selection });
        if (chosen is { Count: 0 }) throw new InvalidOperationException("Не выбрано ни одного сообщения.");
        var selectedIds = chosen?.Distinct().GroupBy(s => s.FolderId).ToDictionary(g => g.Key, g => g.Select(s => s.MessageId).ToHashSet());
        var id = Guid.NewGuid().ToString("N");
        var reportPath = target + "." + id + ".conversion.json";
        var report = new ConversionReport { Source = source, Destination = target,
            Mode = chosen is null ? "Mailbox" : chosen.Count == 1 ? "SelectedMessage" : "SelectedMessages", Selection = selection,
            Selections = chosen?.Distinct().ToList(), OutputFormat = format.ToString().ToUpperInvariant(), IncludeServiceFolders = includeServiceFolders };
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
            if (chosen is not null)
            {
                foreach (var group in chosen.Distinct().GroupBy(s => s.FolderId))
                {
                    var folder = group.First().ResolveFolder(root);
                    var headers = archive.ReadMessages(folder);
                    foreach (var selectedMessage in group) selectedMessage.ResolveMessage(headers);
                    folder.Messages.Clear();
                }
            }
            var type = Type.GetTypeFromProgID("Outlook.Application")
                ?? throw new InvalidOperationException("Не найден классический Outlook. Новый Outlook не поддерживает COM. Установите и настройте классический Outlook.");
            dynamic app = session.Keep(Activator.CreateInstance(type)!);
            outlookNamespace = session.Keep(app.GetNamespace("MAPI"));
            var writer = new MailWriterContext((object)app, (object)outlookNamespace, format, target,
                format == ExportFormat.Msg ? new MsgFolderLayout(root) : null);
            if (format == ExportFormat.Msg)
            {
                Directory.CreateDirectory(target);
                CopyFolder(archive, root, () => new object(), report, progress, cancellation, writer, selectedIds);
                report.OutputMessagesVerified = report.MsgFiles.Count;
                report.OutlookReadBackVerified = report.Converted > 0 && report.ContentVerified == report.Converted && report.MsgFiles.Count == report.Converted;
                report.Completed = report.FolderErrors == 0 && report.Failed == 0 && report.OutlookReadBackVerified;
                cancellation.ThrowIfCancellationRequested();
            }
            else
            {
            // 2 = olStoreUnicode. Do not Logon/Logoff or Quit the user's Outlook session.
            // Use the requested NEW pathname from the start. Outlook can retain OS handles even
            // after RemoveStore; renaming the saved PST was the fatal error in the user's report.
            ArchiveUtilities.ValidateDestination(source, target);
            outlookNamespace.AddStoreEx(Path.GetFullPath(target), 2);
            dynamic stores = session.Keep(outlookNamespace.Stores);
            for (int index = 1; index <= (int)stores.Count; index++)
            {
                using var scope = new ComScope();
                dynamic store = scope.Keep(stores[index]);
                if (string.Equals((string)store.FilePath, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                {
                    targetRoot = session.Keep(store.GetRootFolder());
                    break;
                }
            }
            if (targetRoot is null) throw new IOException("Outlook не подключил созданный PST.");
            targetRoot.Name = Path.GetFileNameWithoutExtension(target);
            // Mail folders sit directly under the named archive, without an extra import wrapper.
            CopyFolder(archive, root, () => (object)targetRoot, report, progress, cancellation, writer, selectedIds);
            PstInventory.Verify(report, OutlookPst.ReadFolderCounts((object)targetRoot));
            cancellation.ThrowIfCancellationRequested();
            report.Completed = report.FolderErrors == 0 && report.Failed == 0;
            }
        }
        catch (OperationCanceledException) { report.Cancelled = true; report.Completed = false; }
        catch (Exception e)
        {
            report.Completed = false;
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

        if (report.Converted == 0)
        {
            report.Completed = false;
            report.FatalError ??= "Ни одно письмо не сохранено. Проверьте причины пропуска и ошибок в отчёте.";
        }
        if (format == ExportFormat.Pst) OutputFileStatus.Apply(report, target, detached);
        else if (!report.Completed && Directory.Exists(target)) report.RetainedPartialDirectory = target;
        report.FinishedUtc = DateTime.UtcNow;
        // Never overwrite an existing report. A cancelled/failed conversion also gets a report.
        using (var stream = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write))
            JsonSerializer.Serialize(stream, report, new JsonSerializerOptions
                { WriteIndented = true, Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) });
        return new ConversionResult(report, reportPath);
    }

    private static void CopyFolder(XstFile archive, Folder source, Func<object> destination,
        ConversionReport report, IProgress<ConversionProgress> progress, CancellationToken cancellation,
        MailWriterContext writer, Dictionary<uint, HashSet<uint>>? selectedIds,
        int depth = 0, HashSet<string>? destinationNames = null)
    {
        destinationNames ??= new(StringComparer.OrdinalIgnoreCase);
        cancellation.ThrowIfCancellationRequested();
        var messages = selectedIds is null || selectedIds.ContainsKey(source.Nid.dwValue) ? FolderReadPolicy.Read(archive, source, report) : [];
        if (selectedIds is not null && selectedIds.TryGetValue(source.Nid.dwValue, out var ids)) messages = messages.Where(m => ids.Contains(m.Nid.dwValue)).ToList();
        CopyMessages(archive, source, destination, messages, report, progress, cancellation, writer);
        // Release cached headers as well: do not retain the whole mailbox in RAM during conversion.
        source.Messages.Clear();
        foreach (var child in source.Folders)
        {
            cancellation.ThrowIfCancellationRequested();
            if (selectedIds is null && !report.IncludeServiceFolders && ExportFolderLayout.IsServiceBranch(child, depth))
            {
                report.ExcludedServiceFolders.Add(child.Path);
                continue;
            }
            if (writer.Format == ExportFormat.Msg || ExportFolderLayout.IsWrapper(child, depth))
            {
                CopyFolder(archive, child, destination, report, progress, cancellation, writer, selectedIds, depth + 1, destinationNames);
                continue;
            }
            using var scope = new ComScope();
            var childFolder = new Lazy<object>(() =>
            {
                dynamic parent = destination();
                dynamic folders = scope.Keep(parent.Folders);
                var name = ExportFolderLayout.UniqueName(child.Name, destinationNames);
                for (int i = 1; i <= (int)folders.Count; i++)
                {
                    dynamic existing = scope.Keep(folders[i]);
                    // A new PST may already contain a standard mail folder (e.g. Deleted Items).
                    if (string.Equals((string)existing.Name, name, StringComparison.OrdinalIgnoreCase) && (int)existing.DefaultItemType == 0)
                        return (object)existing;
                }
                return (object)scope.Keep(folders.Add(name, 6));
            });
            try { CopyFolder(archive, child, () => childFolder.Value, report, progress, cancellation, writer, selectedIds, depth + 1); }
            catch (COMException error)
            {
                report.FolderErrors++;
                report.Issues.Add(new(child.Path, "", "Outlook не создал папку: " + error.Message, "CreateFolder", error.ToString()));
                continue;
            }
        }
    }

    private static void CopyMessages(XstFile archive, Folder source, Func<object> target, List<Message> messages,
        ConversionReport report, IProgress<ConversionProgress> progress, CancellationToken cancellation, MailWriterContext writer)
    {
        foreach (var message in messages)
        {
            cancellation.ThrowIfCancellationRequested();
            report.Examined++;
            var stage = "ReadMessage";
            string? messageClass = null;
            try
            {
                archive.ReadMessageDetails(message);
                messageClass = ConvertString(message, 0x001A);
                if (messageClass != "IPM.Note")
                    throw new UnsupportedMessageException("Не поддерживается тип элемента: " + (messageClass ?? "неизвестен"));
                if (message.Attachments.Any(a => !a.IsFile))
                    throw new UnsupportedMessageException("Вложенное сообщение, OLE или ссылочное вложение: письмо пропущено целиком.");
                if (message.Attachments.Any(a => string.Equals(a.FileName, "smime.p7m", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a.MimeTag, "application/pkcs7-mime", StringComparison.OrdinalIgnoreCase)))
                    throw new UnsupportedMessageException("S/MIME требует отдельной обработки: письмо пропущено.");
                stage = "PrepareBody";
                var body = PreparedBody.FromMessage(message);
                stage = "WriteMessage";
                stage = "CreateOutputFolder";
                var msgPath = writer.Format == ExportFormat.Msg
                    ? Path.Combine(writer.MsgFolders!.EnsureDirectory(writer.Target, source), ArchiveUtilities.MsgFileName(message.Subject, source.Nid.dwValue, message.Nid.dwValue)) : null;
                WriteMessage(archive, message, writer.Format == ExportFormat.Pst ? target() : null, body, writer, msgPath, (field, error) =>
                {
                    report.MetadataWarnings++;
                    report.Issues.Add(new(source.Path, message.Subject ?? "",
                        "Outlook не сохранил поле " + field + ": " + error.Message, "Metadata", error.ToString()));
                }, nextStage => stage = nextStage);
                report.Converted++;
                report.ContentVerified++;
                if (msgPath is not null) report.MsgFiles.Add(msgPath);
                if (body.ConvertedFromRtf) report.RtfBodiesConvertedToHtml++;
            }
            catch (UnsupportedMessageException e)
            {
                report.Skipped++;
                report.Issues.Add(new(source.Path, message.Subject ?? "", e.Message, "Unsupported", ItemClass: messageClass));
            }
            catch (Exception e)
            {
                report.Failed++;
                report.Issues.Add(new(source.Path, message.Subject ?? "", e.Message, stage, e.ToString(), ErrorCode: "0x" + unchecked((uint)e.HResult).ToString("X8")));
            }
            finally { message.ClearContents(); message.Properties.Clear(); message.Recipients.Clear(); }
            progress.Report(new(report.Examined, report.Converted, report.Failed, report.Skipped, source.Path));
        }
    }

    private sealed record MailWriterContext(object App, object Session, ExportFormat Format, string Target, MsgFolderLayout? MsgFolders);

    private static void WriteMessage(XstFile archive, Message message, object? target, PreparedBody body, MailWriterContext writer, string? msgPath,
        Action<string, Exception> metadataWarning, Action<string> stage)
    {
        var temp = Path.Combine(Path.GetTempPath(), "MailArchive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        using var scope = new ComScope();
        dynamic? item = null;
        dynamic? reopened = null;
        // Outlook SaveAs does not reliably support long nested destination paths.
        // Write in a short private directory; publish verified MSG bytes with .NET after closing COM.
        var standalonePath = Path.Combine(temp, "message.msg");
        try
        {
            stage("CreateMessage");
            dynamic app = writer.App;
            item = scope.Keep(app.CreateItem(0)); // transient; never Save to the user's default Drafts
            stage("WriteBody");
            item.Subject = message.Subject ?? "";
            body.WriteTo(format => item.BodyFormat = format, html => item.HTMLBody = html);

            stage("WriteRecipients");
            dynamic recipients = scope.Keep(item.Recipients);
            int expectedRecipients = 0;
            foreach (var recipient in message.Recipients.Where(r => (int)r.RecipientType is >= 1 and <= 3))
            {
                var address = recipient.EmailAddress ?? recipient.DisplayName;
                if (string.IsNullOrWhiteSpace(address)) continue;
                dynamic entry = scope.Keep(recipients.Add(address));
                entry.Type = (int)recipient.RecipientType;
                expectedRecipients++;
                // No ResolveAll: Exchange may be unavailable; original address remains in the recipient row.
            }
            stage("WriteAttachments");
            dynamic attachments = scope.Keep(item.Attachments);
            int number = 0;
            var expectedHashes = new List<string>();
            foreach (var attachment in message.Attachments)
            {
                // Unique private directory per attachment prevents duplicate names and path traversal.
                var directory = Path.Combine(temp, (++number).ToString());
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, ArchiveUtilities.SafeAttachmentName(attachment.FileName));
                archive.SaveAttachment(path, null, attachment);
                using (var input = File.OpenRead(path)) expectedHashes.Add(System.Convert.ToHexString(SHA256.HashData(input)));
                dynamic added = scope.Keep(attachments.Add(path, 1, Type.Missing, attachment.FileName ?? "attachment"));
                if (!string.IsNullOrEmpty(attachment.ContentId))
                {
                    dynamic attachmentProperties = scope.Keep(added.PropertyAccessor);
                    attachmentProperties.SetProperty(Dasl(0x3712001F), attachment.ContentId);
                    attachmentProperties.SetProperty(Dasl(0x7FFE000B), attachment.Hidden);
                }
            }
            stage("SaveStandaloneMsg");
            using (new FileStream(standalonePath, FileMode.CreateNew, FileAccess.Write)) { }
            item.SaveAs(standalonePath, 9); // Same proven Unicode MSG path for BOTH formats.
            stage("WriteMetadata");
            dynamic properties = scope.Keep(item.PropertyAccessor);
            // Whitelist only common mail metadata. Never copy store-specific entry IDs or arbitrary properties.
            foreach (ushort tag in new ushort[] { 0x0C1A, 0x0C1E, 0x0C1F, 0x0042, 0x0064, 0x0065, 0x1035 })
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
            // Reassert core fields after optional MAPI setters. Never transplant lifecycle flags.
            item.Subject = message.Subject ?? "";
            body.WriteTo(format => item.BodyFormat = format, html => item.HTMLBody = html);
            stage("SaveStandaloneMsg");
            item.SaveAs(standalonePath, 9);
            item.Close(1); // release the writer before opening its file
            dynamic mapinamespace = writer.Session;
            stage("ReopenStandaloneMsg");
            reopened = scope.Keep(mapinamespace.OpenSharedItem(standalonePath));
            var expected = new SavedMailContent(message.Subject ?? "", body.Html, body.Text, expectedRecipients, expectedHashes);
            void Verify(object value, string verificationStage)
            {
                stage(verificationStage);
                dynamic mail = value;
                using var checkScope = new ComScope();
                dynamic savedAttachments = checkScope.Keep(mail.Attachments);
                var actualHashes = new List<string>();
                for (int index = 1; index <= (int)savedAttachments.Count; index++)
                {
                    dynamic attachment = checkScope.Keep(savedAttachments[index]);
                    var verifyPath = Path.Combine(temp, verificationStage + "-" + index + ".bin");
                    attachment.SaveAsFile(verifyPath);
                    using var input = File.OpenRead(verifyPath);
                    actualHashes.Add(System.Convert.ToHexString(SHA256.HashData(input)));
                }
                dynamic savedRecipients = checkScope.Keep(mail.Recipients);
                expected.Verify(new((string?)mail.Subject ?? "", (string?)mail.HTMLBody ?? "", (string?)mail.Body ?? "", (int)savedRecipients.Count, actualHashes));
            }
            Verify((object)reopened, "VerifyMsgContent");
            if (target is not null)
            {
                var imported = OutlookMessageTransfer.MoveAndReopen((object)reopened, target, writer.Session,
                    value => (object)scope.Keep(value), stage);
                reopened = imported;
                Verify(imported, "VerifyPstContent");
            }
            else if (msgPath is not null)
            {
                reopened.Close(1); reopened = null;
                scope.Dispose();
                stage("PublishMsgFile");
                File.Move(standalonePath, msgPath, overwrite: false);
            }
        }
        finally
        {
            if (item is not null)
            {
                try { item.Close(1); } catch { /* Best effort discard of unsaved item. */ }
            }
            if (reopened is not null) { try { reopened.Close(1); } catch { } }
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

}
