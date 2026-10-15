using System.Net;
using XstReader;

namespace MailArchive;

public sealed record PreparedBody(string Html, string Text, bool ConvertedFromRtf)
{
    public static PreparedBody FromMessage(Message message)
    {
        // OST commonly contains HTML even when the cached native-format marker says RTF.
        if (!string.IsNullOrEmpty(message.BodyHtml) || message.Html is { Length: > 0 })
        {
            var html = MessageBodies.Html(message);
            return new(html, string.IsNullOrEmpty(message.Body) ? ArchiveUtilities.HtmlToText(html) : message.Body, false);
        }
        if (message.RtfCompressed is { Length: > 0 })
        {
            using var decoded = new RtfDecompressor().Decompress(message.RtfCompressed, true)
                ?? throw new InvalidDataException("Не удалось распаковать RTF-текст письма.");
            decoded.Position = 0;
            // Use a byte stream so RtfPipe can honor the RTF's own ansicpg/Unicode escapes.
            var html = RtfPipe.Rtf.ToHtml(decoded);
            return new(html, ArchiveUtilities.HtmlToText(html), true);
        }
        var text = message.Body ?? "";
        return new("<pre style=\"white-space:pre-wrap;font-family:inherit\">" + WebUtility.HtmlEncode(text) + "</pre>", text, false);
    }

    // Keep writing the body testable without an installed Outlook instance. No RTFBody byte-array
    // assignment is used: the real Outlook rejected that operation for 43 messages in the report.
    public void WriteTo(Action<int> setFormat, Action<string> setHtml)
    {
        setFormat(2); // olFormatHTML
        setHtml(Html);
    }
}
