using System.Buffers.Binary;
using OpenMcdf;

namespace MailArchive;

// MS-OXMSG 2.2.3: top-level __properties_version1.0 has a 32-byte header and
// 16-byte property entries. PT_SYSTIME and PT_LONG have inline fixed-size values.
// Work ONLY on our closed temporary MSG, before Outlook opens/imports it.
public static class MsgDateMetadata
{
    public const string StreamName = "__properties_version1.0";
    public const uint FlagsTag = 0x0E070003;

    public static byte[] RestoreProperties(byte[] original, SavedMailDates dates, int sourceFlags,
        DateTime? creationUtc = null, DateTime? modificationUtc = null)
    {
        if (original.Length < 32 || (original.Length - 32) % 16 != 0)
            throw new IOException("Неверная таблица свойств временного MSG.");
        var entries = new List<byte[]>();
        for (int offset = 32; offset < original.Length; offset += 16) entries.Add(original.AsSpan(offset, 16).ToArray());
        void Set(uint tag, long? value)
        {
            var found = entries.FindIndex(e => BinaryPrimitives.ReadUInt32LittleEndian(e) == tag);
            // Preserve missing source send/delivery dates instead of inventing conversion-time values.
            if (value is null) { entries.RemoveAll(e => BinaryPrimitives.ReadUInt32LittleEndian(e) == tag); return; }
            var entry = found >= 0 ? entries[found] : new byte[16];
            if (found < 0)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(entry, tag);
                BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4), 6); // readable + writable
                entries.Add(entry);
            }
            BinaryPrimitives.WriteInt64LittleEndian(entry.AsSpan(8), value.Value);
        }
        Set(SavedMailDates.ReceivedTag, dates.ReceivedUtc?.ToFileTimeUtc());
        Set(SavedMailDates.SentTag, dates.SentUtc?.ToFileTimeUtc());
        if (creationUtc is DateTime created) Set(0x30070040, created.ToFileTimeUtc());
        if (modificationUtc is DateTime modified) Set(0x30080040, modified.ToFileTimeUtc());
        var flagEntry = entries.FirstOrDefault(e => BinaryPrimitives.ReadUInt32LittleEndian(e) == FlagsTag);
        int flags = flagEntry is null ? 0 : BinaryPrimitives.ReadInt32LittleEndian(flagEntry.AsSpan(8));
        // Keep file-derived attachment state; preserve source READ/UNSENT only. Never copy
        // SUBMIT or pending read-receipt flags, which could trigger transport actions.
        flags = (flags & 0x10) | (sourceFlags & (0x1 | 0x8 | 0x20));
        Set(FlagsTag, unchecked((uint)flags));
        var output = new byte[32 + entries.Count * 16];
        original.AsSpan(0, 32).CopyTo(output);
        for (int i = 0; i < entries.Count; i++) entries[i].CopyTo(output, 32 + i * 16);
        return output;
    }

    public static void Restore(string temporaryMsg, SavedMailDates dates, int sourceFlags,
        DateTime? creationUtc = null, DateTime? modificationUtc = null)
    {
        using var root = RootStorage.Open(temporaryMsg, FileMode.Open, FileAccess.ReadWrite);
        using var properties = root.OpenStream(StreamName);
        if (properties.Length > 16 * 1024 * 1024) throw new IOException("Таблица свойств MSG слишком велика.");
        var original = new byte[checked((int)properties.Length)]; properties.ReadExactly(original);
        var updated = RestoreProperties(original, dates, sourceFlags, creationUtc, modificationUtc);
        properties.Position = 0; properties.Write(updated); properties.SetLength(updated.Length); properties.Flush();
        root.Flush();
    }

    public static DateTime? ReadTime(byte[] properties, uint tag)
    {
        if (properties.Length < 32 || (properties.Length - 32) % 16 != 0) throw new IOException("Неверная таблица свойств MSG.");
        for (int offset = 32; offset < properties.Length; offset += 16)
            if (BinaryPrimitives.ReadUInt32LittleEndian(properties.AsSpan(offset)) == tag)
                return DateTime.FromFileTimeUtc(BinaryPrimitives.ReadInt64LittleEndian(properties.AsSpan(offset + 8)));
        return null;
    }
}
