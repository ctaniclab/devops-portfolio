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
        object? codePage = message.Properties.FirstOrDefault(p => p.Tag == EpropertyTag.PidTagInternetCodepage)?.Value;
        if (codePage is not null) return Encoding.GetEncoding(System.Convert.ToInt32(codePage)).GetString(bytes);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes).TrimStart('\uFEFF');
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes).TrimStart('\uFEFF');
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 8192));
        var charset = Regex.Match(head, "charset\\s*=\\s*['\"]?([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (charset.Success) return Encoding.GetEncoding(charset.Groups[1].Value).GetString(bytes).TrimStart('\uFEFF');
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(1252).GetString(bytes); }
    }
}
