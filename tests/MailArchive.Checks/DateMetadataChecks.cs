using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using MailArchive;
using OpenMcdf;

internal static class DateMetadataChecks
{
    public static void Run(Action<bool, string> check)
    {
        var received = new DateTime(2014, 6, 5, 16, 22, 31, DateTimeKind.Utc).AddTicks(12340);
        var sent = received.AddMinutes(-7);
        var dates = new SavedMailDates(received, sent);
        var rootHeader = new byte[32]; rootHeader[8] = 1; rootHeader[12] = 1; rootHeader[16] = 1; rootHeader[20] = 1;
        byte[] Row(uint tag, long value)
        {
            var row = new byte[16]; BinaryPrimitives.WriteUInt32LittleEndian(row, tag);
            BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(4), 6);
            BinaryPrimitives.WriteInt64LittleEndian(row.AsSpan(8), value); return row;
        }
        var old = rootHeader.Concat(Row(SavedMailDates.ReceivedTag, DateTime.UtcNow.ToFileTimeUtc()))
            .Concat(Row(MsgDateMetadata.FlagsTag, 0x32C)).Concat(Row(0x12340003, 123456)).ToArray();
        var restored = MsgDateMetadata.RestoreProperties(old, dates, 1);
        check(MsgDateMetadata.ReadTime(restored, SavedMailDates.ReceivedTag) == received
            && MsgDateMetadata.ReadTime(restored, SavedMailDates.SentTag) == sent,
            "MSG fixed properties preserve original sent/delivery UTC FILETIMEs instead of conversion time");
        check(restored.AsSpan(0, 32).SequenceEqual(rootHeader) && restored.AsSpan(64, 16).SequenceEqual(Row(0x12340003, 123456)),
            "Adding date properties preserves recipient/attachment counters and unrelated MAPI properties");
        int Flags(byte[] properties)
        {
            for (int offset = 32; offset < properties.Length; offset += 16)
                if (BinaryPrimitives.ReadUInt32LittleEndian(properties.AsSpan(offset)) == MsgDateMetadata.FlagsTag)
                    return BinaryPrimitives.ReadInt32LittleEndian(properties.AsSpan(offset + 8));
            throw new Exception("No flags");
        }
        check(Flags(restored) == 1 && (Flags(restored) & 0x30C) == 0,
            "Recovered received mail is not a new unsent draft and carries no queued-send/read-receipt state");
        var attached = old.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(attached.AsSpan(32 + 16 + 8), 0x318);
        check(Flags(MsgDateMetadata.RestoreProperties(attached, dates, 8)) == 0x18,
            "Original drafts remain unsent while file-derived attachment flags are retained");
        var missing = MsgDateMetadata.RestoreProperties(old, new(null, null), 1);
        check(MsgDateMetadata.ReadTime(missing, SavedMailDates.ReceivedTag) is null
            && MsgDateMetadata.ReadTime(missing, SavedMailDates.SentTag) is null,
            "Undated source mail does not acquire fabricated conversion-time date properties");
        bool rejected = false;
        try { MsgDateMetadata.RestoreProperties(new byte[35], dates, 1); } catch (IOException) { rejected = true; }
        check(rejected, "Malformed MSG property tables fail explicitly instead of corrupting message content");
        dates.Verify(tag => tag == SavedMailDates.ReceivedTag ? received : sent);
        check(true, "Read-back verifies both source dates independently in UTC");
        var displayed = new XstReader.Message { Received = received };
        check(displayed.Received?.Kind == DateTimeKind.Utc && displayed.DisplayDate == received.ToLocalTime().ToString("g"),
            "Viewer formats the local time while preserving the original UTC archive value");
        foreach (uint damagedTag in new[] { SavedMailDates.ReceivedTag, SavedMailDates.SentTag })
        {
            bool failed = false;
            try { dates.Verify(tag => tag == damagedTag ? DateTime.UtcNow : tag == SavedMailDates.ReceivedTag ? received : sent); }
            catch (SavedMailVerificationException) { failed = true; }
            check(failed, "Read-back rejects a changed " + (damagedTag == SavedMailDates.ReceivedTag ? "delivery" : "sent") + " timestamp");
        }
        var output = Path.Combine(Path.GetTempPath(), "MailArchive-dates-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        try
        {
            for (int pass = 1; pass <= 3; pass++)
            {
                string path = Path.Combine(output, $"pass-{pass}.msg");
                // Last pass crosses CFB's 4096-byte mini-stream allocation threshold.
                var table = old.ToList();
                if (pass == 3) while (table.Count < 4080) table.AddRange(Row((uint)(0x60000003 + table.Count * 65536), table.Count));
                var text = Encoding.Unicode.GetBytes("Тема и текст письма — сохраняются\0");
                var payload = Enumerable.Range(0, 16384).Select(i => (byte)i).ToArray();
                using (var root = RootStorage.Create(path))
                {
                    using (var props = root.CreateStream(MsgDateMetadata.StreamName)) props.Write(table.ToArray());
                    using (var body = root.CreateStream("__substg1.0_1000001F")) body.Write(text);
                    var attachment = root.CreateStorage("__attach_version1.0_#00000000");
                    using (var data = attachment.CreateStream("__substg1.0_37010102")) data.Write(payload);
                    var recipient = root.CreateStorage("__recip_version1.0_#00000000");
                    using (var address = recipient.CreateStream("__substg1.0_3003001F")) address.Write(text);
                }
                MsgDateMetadata.Restore(path, dates, 1);
                using var read = RootStorage.OpenRead(path, StorageModeFlags.StrictValidation);
                byte[] Read(Stream stream) { using (stream) { using var copy = new MemoryStream(); stream.CopyTo(copy); return copy.ToArray(); } }
                var propsAfter = Read(read.OpenStream(MsgDateMetadata.StreamName));
                dates.Verify(tag => MsgDateMetadata.ReadTime(propsAfter, tag));
                check(Read(read.OpenStream("__substg1.0_1000001F")).SequenceEqual(text)
                    && SHA256.HashData(Read(read.OpenStorage("__attach_version1.0_#00000000").OpenStream("__substg1.0_37010102"))).SequenceEqual(SHA256.HashData(payload))
                    && Read(read.OpenStorage("__recip_version1.0_#00000000").OpenStream("__substg1.0_3003001F")).SequenceEqual(text),
                    $"CFB pass {pass}: original dates persist after reopen; body, recipients and attachment bytes remain unchanged");
            }
        }
        finally { Directory.Delete(output, true); }
    }
}
