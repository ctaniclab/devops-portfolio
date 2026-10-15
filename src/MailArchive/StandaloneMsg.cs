using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenMcdf;
using XstReader;

namespace MailArchive;

// MS-OXMSG: create a Unicode message on disk without constructing an Outlook
// compose item. No draft, autosave or outgoing item can be created in an account.
public static class StandaloneMsg
{
    private sealed class Properties(Storage storage, int headerSize)
    {
        private readonly List<byte[]> rows = [];
        public void Fixed(uint tag, long value)
        {
            var row = new byte[16]; BinaryPrimitives.WriteUInt32LittleEndian(row, tag);
            BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(4), 6);
            BinaryPrimitives.WriteInt64LittleEndian(row.AsSpan(8), value); rows.Add(row);
        }
        public void Bytes(uint tag, byte[] value, bool unicode = false)
        {
            using (var stream = storage.CreateStream("__substg1.0_" + tag.ToString("X8"))) stream.Write(value);
            // String stream excludes its terminator; length in property table includes it.
            Fixed(tag, value.Length + (unicode ? 2 : 0));
        }
        public void Text(uint tag, string? value) { if (value is not null) Bytes(tag, Encoding.Unicode.GetBytes(value), true); }
        public void Save(int recipients = 0, int attachments = 0)
        {
            var header = new byte[headerSize];
            if (headerSize == 32)
            {
                BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), recipients);
                BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), attachments);
                BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), recipients);
                BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(20), attachments);
            }
            using var stream = storage.CreateStream(MsgDateMetadata.StreamName);
            stream.Write(header); foreach (var row in rows) stream.Write(row);
        }
    }

    public static SavedMailContent Write(string path, Message message, PreparedBody body, Action<string, Attachment> saveAttachment)
    {
        var expected = WriteCore(path, message, body, saveAttachment);
        CfbEmptyStreams.Normalize(path);
        return expected;
    }

    private static SavedMailContent WriteCore(string path, Message message, PreparedBody body, Action<string, Attachment> saveAttachment)
    {
        // Caller owns this new, private pathname. Never overwrite an existing export.
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var root = RootStorage.Create(output);
        root.CLSID = new Guid("00020D0B-0000-0000-C000-000000000046");
        var properties = new Properties(root, 32);
        properties.Text(0x001A001F, "IPM.Note");
        properties.Text(0x0037001F, message.Subject ?? "");
        properties.Text(0x0E1D001F, message.Subject ?? "");
        properties.Text(0x1000001F, body.Text);
        properties.Bytes(0x10130102, Encoding.UTF8.GetBytes(body.Html));
        properties.Fixed(0x3FDE0003, 65001); // Internet code page for HTML bytes
        properties.Fixed(0x3FFD0003, 65001);
        properties.Fixed(0x340D0003, 0x40000); // STORE_UNICODE_OK
        foreach (ushort tag in new ushort[] { 0x0C1A, 0x0C1E, 0x0C1F, 0x0042, 0x0064, 0x0065, 0x1035 })
            properties.Text(((uint)tag << 16) | 0x001F, message.Properties.FirstOrDefault(p => (ushort)p.Tag == tag)?.Value as string);
        foreach (var (tag, date) in new[] { (SavedMailDates.ReceivedTag, message.Received), (SavedMailDates.SentTag, message.Submitted) })
            if (date is DateTime time) properties.Fixed(tag, time.ToUniversalTime().ToFileTimeUtc());
        foreach (ushort tag in new ushort[] { 0x3007, 0x3008 })
            if (message.Properties.FirstOrDefault(p => (ushort)p.Tag == tag)?.Value is DateTime time)
                properties.Fixed(((uint)tag << 16) | 0x0040, time.ToUniversalTime().ToFileTimeUtc());
        int sourceFlags = System.Convert.ToInt32(message.Properties.FirstOrDefault(p => (ushort)p.Tag == 0x0E07)?.Value ?? 1);
        properties.Fixed(MsgDateMetadata.FlagsTag, (sourceFlags & (1 | 8 | 0x20)) | (message.Attachments.Count > 0 ? 0x10 : 0));
        int recipientIndex = 0;
        foreach (var recipient in message.Recipients.Where(r => (int)r.RecipientType is >= 1 and <= 3))
        {
            string? address = recipient.EmailAddress ?? recipient.DisplayName;
            if (string.IsNullOrWhiteSpace(address)) continue;
            var storage = root.CreateStorage("__recip_version1.0_#" + recipientIndex.ToString("X8"));
            var row = new Properties(storage, 8);
            row.Fixed(0x30000003, recipientIndex++);
            row.Fixed(0x0C150003, (int)recipient.RecipientType);
            row.Text(0x3001001F, recipient.DisplayName ?? address);
            string addressType = recipient.Properties.FirstOrDefault(p => (ushort)p.Tag == 0x3002)?.Value as string
                ?? (address.StartsWith("/", StringComparison.Ordinal) ? "EX" : "SMTP");
            row.Text(0x3002001F, addressType);
            row.Text(0x3003001F, address);
            row.Bytes(0x0FFF0102, OneOffEntryId(recipient.DisplayName ?? address, addressType, address));
            row.Bytes(0x300B0102, Encoding.UTF8.GetBytes((addressType + ":" + address).ToUpperInvariant() + "\0"));
            if (address.Contains('@')) row.Text(0x39FE001F, address);
            row.Fixed(0x0FFE0003, 6); // MAPI_MAILUSER
            row.Fixed(0x39000003, 0); // DT_MAILUSER
            row.Save();
        }
        foreach (var (tag, type) in new[] { (0x0E04001Fu, 1), (0x0E03001Fu, 2), (0x0E02001Fu, 3) })
            properties.Text(tag, string.Join("; ", message.Recipients.Where(r => (int)r.RecipientType == type).Select(r => r.DisplayName ?? r.EmailAddress)));
        int attachmentIndex = 0; var hashes = new List<string>();
        foreach (var attachment in message.Attachments)
        {
            var storage = root.CreateStorage("__attach_version1.0_#" + attachmentIndex.ToString("X8"));
            var row = new Properties(storage, 8);
            row.Fixed(0x0E210003, attachmentIndex++); row.Fixed(0x37050003, 1); row.Fixed(0x370B0003, -1);
            row.Text(0x3704001F, attachment.FileName ?? "attachment.bin");
            row.Text(0x3707001F, attachment.FileName ?? "attachment.bin");
            row.Text(0x370E001F, attachment.MimeTag); row.Text(0x3712001F, attachment.ContentId);
            row.Fixed(0x7FFE000B, attachment.Hidden ? 1 : 0);
            var temporary = path + ".attachment";
            try
            {
                saveAttachment(temporary, attachment);
                using var input = File.OpenRead(temporary);
                hashes.Add(System.Convert.ToHexString(SHA256.HashData(input))); input.Position = 0;
                using var data = storage.CreateStream("__substg1.0_37010102"); input.CopyTo(data);
                row.Fixed(0x37010102, checked((int)input.Length));
                row.Fixed(0x0E200003, checked((int)input.Length));
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            row.Save();
        }
        // Empty named-property mapping: only standard property tags are written.
        var names = root.CreateStorage("__nameid_version1.0");
        foreach (string name in new[] { "__substg1.0_00020102", "__substg1.0_00030102", "__substg1.0_00040102" })
            using (names.CreateStream(name)) { }
        properties.Save(recipientIndex, attachmentIndex);
        return new(message.Subject ?? "", body.Html, body.Text, recipientIndex, hashes);
    }

    private static byte[] OneOffEntryId(string name, string addressType, string address)
    {
        // MS-OXCDATA 2.2.5.1: one-off recipient, Unicode, no rich-info lookup.
        byte[] header = [0,0,0,0, 0x81,0x2B,0x1F,0xA4,0xBE,0xA3,0x10,0x19,0x9D,0x6E,0,0xDD,1,0x0F,0x54,2, 0,0,1,0x80];
        return header.Concat(Encoding.Unicode.GetBytes(name + "\0" + addressType + "\0" + address + "\0")).ToArray();
    }

    public static void Verify(string path, SavedMailContent expected, SavedMailDates dates)
    {
        using var root = RootStorage.OpenRead(path, StorageModeFlags.StrictValidation);
        byte[] Read(Storage storage, string name)
        { using var input = storage.OpenStream(name); using var copy = new MemoryStream(); input.CopyTo(copy); return copy.ToArray(); }
        string Text(uint tag) => Encoding.Unicode.GetString(Read(root, "__substg1.0_" + tag.ToString("X8")));
        var table = Read(root, MsgDateMetadata.StreamName);
        int recipients = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(16));
        int attachments = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(20));
        var hashes = new List<string>();
        for (int i = 0; i < attachments; i++)
        {
            using var data = root.OpenStorage("__attach_version1.0_#" + i.ToString("X8")).OpenStream("__substg1.0_37010102");
            hashes.Add(System.Convert.ToHexString(SHA256.HashData(data)));
        }
        for (int i = 0; i < recipients; i++)
            _ = Read(root.OpenStorage("__recip_version1.0_#" + i.ToString("X8")), "__substg1.0_3003001F");
        expected.Verify(new(Text(0x0037001F), Encoding.UTF8.GetString(Read(root, "__substg1.0_10130102")), Text(0x1000001F), recipients, hashes));
        dates.Verify(tag => MsgDateMetadata.ReadTime(table, tag));
    }
}
