using System.Text;
using System.Text.RegularExpressions;
using XstReader;

namespace MailArchive;

public static class MessageBodies
{
    public static string Html(Message message)
    {
        if (message.BodyHtml is not null) return message.BodyHtml;
        if (message.Html is not { Length: > 0 } bytes) return message.Body ?? "";
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes).TrimStart('\uFEFF');
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes).TrimStart('\uFEFF');
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 8192));
        var charset = Regex.Match(head, "charset\\s*=\\s*['\"]?([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (charset.Success)
        {
            try { return Encoding.GetEncoding(charset.Groups[1].Value).GetString(bytes).TrimStart('\uFEFF'); }
            catch (ArgumentException) { } // Unknown sender charset; try the cached code page next.
        }
        object? codePage = message.Properties.FirstOrDefault(p => p.Tag == EpropertyTag.PidTagInternetCodepage)?.Value;
        if (codePage is not null)
        {
            try { return Encoding.GetEncoding(System.Convert.ToInt32(codePage)).GetString(bytes); }
            catch (Exception error) when (error is ArgumentException or FormatException or OverflowException) { }
        }
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(1252).GetString(bytes); }
    }
}
