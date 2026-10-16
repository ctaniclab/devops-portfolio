using System.Text.Json;

namespace MailArchive;

// Append-only, flushed before and after each item. A Started entry without a Finished
// entry means the output is uncertain and must not be silently imported again.
public sealed class ConversionJournal : IDisposable
{
    private readonly FileStream stream;
    public ConversionJournal(string path)
        => stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);

    public void Append(object entry)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(entry);
        stream.Write(bytes);
        stream.WriteByte((byte)'\n');
        stream.Flush(flushToDisk: true);
    }

    public void Dispose() => stream.Dispose();
}
