using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using MailArchive;
using OpenMcdf;
using XstReader;

internal static class StandaloneMsgChecks
{
    public static void Run(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "MailArchive-standalone-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            for (int pass = 0; pass < 3; pass++)
            {
                var message = new Message { Subject = "Проверка — " + pass, Body = "Текст письма с кириллицей", BodyHtml = "<p>Текст письма с <b>кириллицей</b></p>",
                    Received = new DateTime(2016, 7, 2, 13, 14, 15, DateTimeKind.Utc), Submitted = new DateTime(2016, 7, 2, 12, 14, 15, DateTimeKind.Utc) };
                message.Recipients.Add(new() { EmailAddress = "person@example.invalid", DisplayName = "Получатель", RecipientType = RecipientType.To });
                message.Recipients.Add(new() { EmailAddress = "copy@example.invalid", DisplayName = "Копия", RecipientType = RecipientType.Cc });
                message.Properties.Add(new() { Tag = (EpropertyTag)0x0E07, Value = pass == 2 ? 0x32D : 0x325 }); // retain drafts, strip queued/receipt flags
                message.Properties.Add(new() { Tag = (EpropertyTag)0x0C1A, Value = "Отправитель" });
                message.Properties.Add(new() { Tag = (EpropertyTag)0x0C1E, Value = "SMTP" });
                message.Properties.Add(new() { Tag = (EpropertyTag)0x0C1F, Value = "sender@example.invalid" });
                message.Attachments.Add(new() { LongFileName = "пример.txt", MimeTag = "text/plain", ContentId = "original-cid", Hidden = true });
                // Third pass exceeds the 109 FAT-sector pointers in the CFB header.
                var payload = Enumerable.Range(0, pass == 0 ? 0 : pass == 1 ? 100 : 8 * 1024 * 1024 + 17).Select(i => (byte)i).ToArray();
                if (pass == 0) { message.Subject = ""; message.Body = ""; message.BodyHtml = ""; message.Received = message.Submitted = null; }
                string path = Path.Combine(directory, $"sample-{pass}.msg");
                var expected = StandaloneMsg.Write(path, message, PreparedBody.FromMessage(message), (file, _) => File.WriteAllBytes(file, payload));
                var dates = new SavedMailDates(message.Received, message.Submitted);
                StandaloneMsg.Verify(path, expected, dates);
                check(true, $"MSG pass {pass + 1}: Unicode body, recipients, source dates and attachment hash survive disk round-trip without Outlook");
                using (var root = RootStorage.OpenRead(path, StorageModeFlags.StrictValidation))
                {
                    using var table = root.OpenStream(MsgDateMetadata.StreamName);
                    var data = new byte[(int)table.Length]; table.ReadExactly(data);
                    var flags = Enumerable.Range(0, (data.Length - 32) / 16).Select(i => 32 + i * 16)
                        .Single(i => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i)) == MsgDateMetadata.FlagsTag);
                    check((BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(flags + 8)) & 0x304) == 0
                        && (BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(flags + 8)) & 8) == (pass == 2 ? 8 : 0)
                        && root.CLSID == new Guid("00020D0B-0000-0000-C000-000000000046"), "MSG has Outlook message class and no queued-send or receipt flags");
                }
                bool refused = false;
                try { StandaloneMsg.Write(path, message, PreparedBody.FromMessage(message), (_, _) => { }); }
                catch (IOException) { refused = true; }
                check(refused, "MSG refuses to overwrite an existing file");
                bool mismatch = false;
                try { StandaloneMsg.Verify(path, expected with { Subject = "Other message" }, dates); }
                catch (SavedMailVerificationException) { mismatch = true; }
                check(mismatch, "MSG verification detects changed content");
                ExportForIndependent(path, message, expected, "sample-" + pass);
            }
        }
        finally { Directory.Delete(directory, true); }
        foreach (uint code in new uint[] { 0x80010108, 0x800706BA, 0x800706BE, 0x80010007, 0x80010012, 0x800401FD })
            check(OutlookConnection.IsDisconnected(new IOException("wrapped", new COMException("closed", unchecked((int)code)))),
                "Outlook disconnect " + code.ToString("X8") + " stops the operation instead of failing every remaining message");
        check(!OutlookConnection.IsDisconnected(new COMException("item error", unchecked((int)0x80040107))),
            "An individual invalid message ID does not masquerade as an Outlook shutdown");
    }

    public static void ExportForIndependent(string path, Message source, SavedMailContent expected, string name)
    {
        var directory = Environment.GetEnvironmentVariable("MAILARCHIVE_MSG_CHECK_OUTPUT");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.Copy(path, Path.Combine(directory, name + ".msg"), false);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(new
        {
            expected.Subject, expected.Html, expected.Text, expected.Recipients, expected.AttachmentHashes,
            Received = source.Received?.ToUniversalTime(), Sent = source.Submitted?.ToUniversalTime(),
            Addresses = source.Recipients.Where(r => (int)r.RecipientType is >= 1 and <= 3 && !string.IsNullOrWhiteSpace(r.EmailAddress ?? r.DisplayName))
                .Select(r => new { Name = r.DisplayName ?? r.EmailAddress, Email = r.EmailAddress ?? r.DisplayName, Type = (int)r.RecipientType }).ToArray(),
            Attachments = source.Attachments.Select(a => new { Name = a.FileName ?? "attachment.bin", a.ContentId }).ToArray()
        }));
    }
}
