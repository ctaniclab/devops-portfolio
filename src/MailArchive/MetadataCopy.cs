using System.Runtime.InteropServices;

namespace MailArchive;

internal static class MetadataCopy
{
    // Outlook stores can refuse individual read-only metadata fields. Preserve the actual
    // message and explicitly report each refused field; body/attachment/Save errors still fail it.
    public static bool TrySet(string field, Action write, Action<string, Exception> warning)
    {
        try { write(); return true; }
        // IDispatch maps E_ACCESSDENIED to UnauthorizedAccessException and E_INVALIDARG to
        // ArgumentException, not necessarily COMException (as confirmed by the user's report).
        catch (Exception error) when (error is COMException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { warning(field, error); return false; }
    }
}
