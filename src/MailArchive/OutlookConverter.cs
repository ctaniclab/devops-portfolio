using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using XstReader;
using Message = XstReader.Message;
using System.Security.Cryptography;

namespace MailArchive;

public sealed record ConversionProgress(int Examined, int Converted, int Failed, int Skipped, string Folder, int? Total = null, bool Finalizing = false);
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
        if (chosen is { Count: 0 }) throw new InvalidOperationException(L.T("Не выбрано ни одного сообщения."));
        var selectedIds = chosen?.Distinct().GroupBy(s => s.FolderId).ToDictionary(g => g.Key, g => g.Select(s => s.MessageId).ToHashSet());
        var id = Guid.NewGuid().ToString("N");
        var reportPath = target + "." + id + ".conversion.json";
        var report = new ConversionReport { Source = source, Destination = target,
            Mode = chosen is null ? "Mailbox" : chosen.Count == 1 ? "SelectedMessage" : "SelectedMessages", Selection = selection,
            Selections = chosen?.Distinct().ToList(), OutputFormat = format.ToString().ToUpperInvariant(), IncludeServiceFolders = includeServiceFolders };
        report.JournalPath = reportPath + ".jsonl";
        using var journal = new ConversionJournal(report.JournalPath);
        journal.Append(new { Event = "ConversionStarted", Report = report });
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
                    var available = headers.Select(m => m.Nid.dwValue).ToHashSet();
                    if (group.Any(selectedMessage => !available.Contains(selectedMessage.MessageId)))
                        throw new InvalidOperationException(L.T("Выбранное сообщение больше не найдено в исходном архиве. Откройте файл заново и повторите выбор."));
                    folder.Messages.Clear();
                }
            }
            if (format == ExportFormat.Pst)
            {
                var type = Type.GetTypeFromProgID("Outlook.Application")
                    ?? throw new InvalidOperationException(L.T("Не найден классический Outlook. Новый Outlook не поддерживает COM. Установите и настройте классический Outlook."));
                dynamic app = session.Keep(Activator.CreateInstance(type)!);
                outlookNamespace = session.Keep(app.GetNamespace("MAPI"));
            }
            var writer = new MailWriterContext((object?)outlookNamespace, format, target,
                format == ExportFormat.Msg ? new MsgFolderLayout(root) : null, chosen?.Distinct().Count(), journal);
            progress.Report(new(0, 0, 0, 0, format == ExportFormat.Msg ? L.T("Подготовка MSG") : L.T("Подготовка Outlook"), writer.Total));
            if (format == ExportFormat.Msg)
            {
                Directory.CreateDirectory(target);
                CopyFolder(archive, root, () => new object(), report, progress, cancellation, writer, selectedIds);
                progress.Report(new(report.Examined, report.Converted, report.Failed, report.Skipped, L.T("Проверка результата"), writer.Total, true));
                report.OutputMessagesVerified = report.MsgFiles.Count;
                report.MsgReadBackVerified = report.Converted > 0 && report.ContentVerified == report.Converted && report.MsgFiles.Count == report.Converted;
                report.Completed = report.FolderErrors == 0 && report.Failed == 0 && report.MsgReadBackVerified;
                cancellation.ThrowIfCancellationRequested();
            }
            else
            {
            // 2 = olStoreUnicode. Do not Logon/Logoff or Quit the user's Outlook session.
            // Use the requested NEW pathname from the start. Outlook can retain OS handles even
            // after RemoveStore; renaming the saved PST was the fatal error in the user's report.
            ArchiveUtilities.ValidateDestination(source, target);
            outlookNamespace!.AddStoreEx(Path.GetFullPath(target), 2);
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
            if (targetRoot is null) throw new IOException(L.T("Outlook не подключил созданный PST."));
            targetRoot.Name = Path.GetFileNameWithoutExtension(target);
            // Mail folders sit directly under the named archive, without an extra import wrapper.
            CopyFolder(archive, root, () => (object)targetRoot, report, progress, cancellation, writer, selectedIds);
            progress.Report(new(report.Examined, report.Converted, report.Failed, report.Skipped, L.T("Проверка PST"), writer.Total, true));
            PstInventory.Verify(report, OutlookPst.ReadFolderCounts((object)targetRoot));
            cancellation.ThrowIfCancellationRequested();
            report.Completed = report.FolderErrors == 0 && report.Failed == 0;
            }
        }
        catch (OperationCanceledException) { report.Cancelled = true; report.Completed = false; }
        catch (Exception e)
        {
            report.Completed = false;
            var failure = OutlookConnection.IsDisconnected(e) ? new OutlookDisconnectedException(e) : e;
            report.FatalError = failure.Message;
            report.Issues.Add(new("", "", failure.Message, failure is OutlookDisconnectedException ? "OutlookDisconnected" : "Fatal", e.ToString()));
        }
        finally
        {
            if (targetRoot is not null && outlookNamespace is not null)
            {
                try { outlookNamespace.RemoveStore(targetRoot); detached = true; }
                catch (Exception e)
                {
                    report.Completed = false;
                    report.FatalError ??= OutlookConnection.IsDisconnected(e) ? new OutlookDisconnectedException(e).Message
                        : L.T("Не удалось отключить выходной PST от Outlook: ") + e.Message;
                }
            }
            session.Dispose();
        }

        if (report.Converted == 0)
        {
            report.Completed = false;
            report.FatalError ??= L.T("Ни одно письмо не сохранено. Проверьте причины пропуска и ошибок в отчёте.");
        }
        if (format == ExportFormat.Pst) OutputFileStatus.Apply(report, target, detached);
        else if (!report.Completed && Directory.Exists(target)) report.RetainedPartialDirectory = target;
        report.FinishedUtc = DateTime.UtcNow;
        journal.Append(new { Event = "ConversionFinished", Report = report });
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
            catch (COMException error) when (!OutlookConnection.IsDisconnected(error))
            {
                report.FolderErrors++;
                report.Issues.Add(new(child.Path, "", L.T("Outlook не создал папку: ") + error.Message, "CreateFolder", error.ToString()));
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
            writer.Journal.Append(new { Event = "MessageStarted", FolderId = source.Nid.dwValue, MessageId = message.Nid.dwValue, Folder = source.Path });
            var convertedBefore = report.Converted;
            var skippedBefore = report.Skipped;
            try
            {
                archive.ReadMessageDetails(message);
                messageClass = ConvertString(message, 0x001A);
                if (messageClass != "IPM.Note")
                    throw new UnsupportedMessageException(L.T("Не поддерживается тип элемента: ") + (messageClass ?? L.T("неизвестен")));
                if (message.Attachments.Any(a => !a.IsFile))
                    throw new UnsupportedMessageException(L.T("Вложенное сообщение, OLE или ссылочное вложение: письмо пропущено целиком."));
                if (message.Attachments.Any(a => string.Equals(a.FileName, "smime.p7m", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a.MimeTag, "application/pkcs7-mime", StringComparison.OrdinalIgnoreCase)))
                    throw new UnsupportedMessageException(L.T("S/MIME требует отдельной обработки: письмо пропущено."));
                stage = "PrepareBody";
                var body = PreparedBody.FromMessage(message);
                stage = "WriteMessage";
                stage = "CreateOutputFolder";
                var msgPath = writer.Format == ExportFormat.Msg
                    ? Path.Combine(writer.MsgFolders!.EnsureDirectory(writer.Target, source), ArchiveUtilities.MsgFileName(message.Subject, source.Nid.dwValue, message.Nid.dwValue)) : null;
                WriteMessage(archive, message, writer.Format == ExportFormat.Pst ? target() : null, body, writer, msgPath, nextStage => stage = nextStage);
                report.Converted++;
                report.ContentVerified++;
                if (message.Received is not null || message.Submitted is not null) report.DatesVerified++;
                else report.MessagesWithoutSourceDates++;
                if (msgPath is not null) report.MsgFiles.Add(msgPath);
                if (body.ConvertedFromRtf) report.RtfBodiesConvertedToHtml++;
            }
            catch (UnsupportedMessageException e)
            {
                report.Skipped++;
                report.Issues.Add(new(source.Path, message.Subject ?? "", e.Message, "Unsupported", ItemClass: messageClass));
            }
            catch (Exception e) when (OutlookConnection.IsDisconnected(e))
            {
                report.Failed++;
                report.Issues.Add(new(source.Path, message.Subject ?? "", e.Message, stage, e.ToString(), ErrorCode: "0x" + unchecked((uint)e.HResult).ToString("X8")));
                throw new OutlookDisconnectedException(e);
            }
            catch (Exception e)
            {
                report.Failed++;
                report.Issues.Add(new(source.Path, message.Subject ?? "", e.Message, stage, e.ToString(), ErrorCode: "0x" + unchecked((uint)e.HResult).ToString("X8")));
            }
            finally
            {
                writer.Journal.Append(new { Event = "MessageFinished", FolderId = source.Nid.dwValue, MessageId = message.Nid.dwValue,
                    Outcome = report.Converted > convertedBefore ? "Converted" : report.Skipped > skippedBefore ? "Skipped" : "Failed",
                    Stage = stage, Issue = report.Converted > convertedBefore ? null : report.Issues.LastOrDefault(), report.Converted, report.Failed, report.Skipped });
                message.ClearContents(); message.Properties.Clear(); message.Recipients.Clear();
            }
            progress.Report(new(report.Examined, report.Converted, report.Failed, report.Skipped, source.Path, writer.Total));
        }
    }

    private sealed record MailWriterContext(object? Session, ExportFormat Format, string Target, MsgFolderLayout? MsgFolders, int? Total, ConversionJournal Journal);

    private static void WriteMessage(XstFile archive, Message message, object? target, PreparedBody body, MailWriterContext writer, string? msgPath,
        Action<string> stage)
    {
        var temp = Path.Combine(Path.GetTempPath(), "MailArchive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        using var scope = new ComScope();
        dynamic? reopened = null;
        object? movedItem = null;
        // Short private path for Outlook's MSG importer; publish verified MSG bytes without COM.
        var standalonePath = Path.Combine(temp, "message.msg");
        try
        {
            stage("WriteStandaloneMsg");
            var expected = StandaloneMsg.Write(standalonePath, message, body, (path, attachment) => archive.SaveAttachment(path, null, attachment));
            var dates = new SavedMailDates(message.Received?.ToUniversalTime(), message.Submitted?.ToUniversalTime());
            if (target is null && msgPath is not null)
            {
                stage("VerifyMsgFile");
                StandaloneMsg.Verify(standalonePath, expected, dates);
                stage("PublishMsgFile"); File.Move(standalonePath, msgPath, overwrite: false);
                return;
            }
            dynamic mapinamespace = writer.Session!;
            stage("ReopenStandaloneMsg");
            reopened = scope.Keep(mapinamespace.OpenSharedItem(standalonePath));
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
                var identities = new List<string>();
                for (int index = 1; index <= (int)savedRecipients.Count; index++)
                {
                    dynamic recipient = checkScope.Keep(savedRecipients[index]);
                    dynamic properties = checkScope.Keep(recipient.PropertyAccessor);
                    identities.Add(SavedMailContent.RecipientIdentity((int)recipient.Type,
                        (string)properties.GetProperty(Dasl(0x3002001F)), (string)properties.GetProperty(Dasl(0x3003001F))));
                }
                dynamic mailProperties = checkScope.Keep(mail.PropertyAccessor);
                expected.Verify(new((string?)mail.Subject ?? "", (string?)mail.HTMLBody ?? "", (string?)mail.Body ?? "", (int)savedRecipients.Count, actualHashes)
                    { RecipientIdentities = identities, SenderAddress = expected.SenderAddress is null ? null : (string)mailProperties.GetProperty(Dasl(0x0C1F001F)) });
                stage(verificationStage == "VerifyPstContent" ? "VerifyPstDates" : "VerifyMsgDates");
                dynamic dateProperties = checkScope.Keep(mail.PropertyAccessor);
                dates.Verify(tag =>
                {
                    try
                    {
                        // PropertyAccessor PT_SYSTIME returns a UTC DATE (COM Kind is Unspecified).
                        object value = dateProperties.GetProperty(Dasl(tag));
                        return value is DateTime date ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : null;
                    }
                    catch (COMException e) when (e.HResult == unchecked((int)0x8004010F)) { return null; }
                });
            }
            Verify((object)reopened, "VerifyMsgContent");
            if (target is not null)
            {
                var imported = OutlookMessageTransfer.MoveAndReopen((object)reopened, target, writer.Session!,
                    value => (object)scope.Keep(value), stage, value => movedItem = value);
                reopened = imported;
                Verify(imported, "VerifyPstContent");
            }
        }
        catch (Exception failure) when (movedItem is not null && target is not null)
        {
            // Isolate only the exact newly moved item, and only inside the new PST.
            // Never Delete(): Outlook could route deletion into an account's Deleted Items.
            try
            {
                dynamic item = movedItem;
                dynamic parent = scope.Keep(item.Parent);
                dynamic destination = target;
                dynamic ns = writer.Session!;
                if (!ns.CompareEntryIDs((string)parent.StoreID, (string)destination.StoreID))
                    throw new IOException(L.T("Нельзя изолировать элемент вне выходного PST."));
                dynamic store = scope.Keep(destination.Store);
                dynamic root = scope.Keep(store.GetRootFolder());
                dynamic folders = scope.Keep(root.Folders);
                dynamic quarantine = scope.Keep(folders.Add(L.T("Непроверенное-") + Guid.NewGuid().ToString("N"), 6));
                dynamic isolated = scope.Keep(item.Move(quarantine));
                dynamic isolatedParent = scope.Keep(isolated.Parent);
                if (!ns.CompareEntryIDs((string)isolatedParent.StoreID, (string)destination.StoreID)
                    || !ns.CompareEntryIDs((string)isolatedParent.EntryID, (string)quarantine.EntryID))
                    throw new IOException(L.T("Не подтверждена папка изоляции."));
                throw new SavedMailVerificationException(failure.Message + L.T(" Элемент изолирован в папке «") + (string)quarantine.Name + L.T("» выходного PST."));
            }
            catch (SavedMailVerificationException) { throw; }
            catch (Exception isolationFailure)
            {
                throw new AggregateException(L.T("Проверка переноса не пройдена; изолировать элемент не удалось. Частичный PST требует проверки."), failure, isolationFailure);
            }
        }
        finally
        {
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
