using System.Runtime.InteropServices;

namespace MailArchive;

internal static class MetadataCopy
{
    // Outlook stores can refuse individual read-only metadata fields. Preserve the actual
    // message and explicitly report each refused field; body/attachment/Save errors still fail it.
    public static bool TrySet(string field, Action write, Action<string, Exception> warning)
    {
        try { write(); return true; }
        catch (COMException error) { warning(field, error); return false; }
    }
}
