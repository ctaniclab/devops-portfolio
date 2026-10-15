using System.Net;
using System.Text.RegularExpressions;

namespace MailArchive;
public static class ArchiveUtilities
{
    public static string SafeDirectoryName(string? original)
    {
        var name = Regex.Replace(original ?? "", "[<>:\"/\\\\|?*\\x00-\\x1f]", "_").Trim(' ', '.');
        if (name.Length == 0) name = "Без имени";
        if (Regex.IsMatch(name.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$", RegexOptions.IgnoreCase)) name = "_" + name;
        if (name.Length > 80) name = name[..80];
        if (char.IsHighSurrogate(name[^1])) name = name[..^1];
        return name.TrimEnd(' ', '.');
    }

    public static string NewMsgRoot(string parent, string source)
    {
        var basename = SafeDirectoryName(Path.GetFileNameWithoutExtension(source));
        var candidate = Path.Combine(parent, basename);
        for (int suffix = 2; Directory.Exists(candidate) || File.Exists(candidate); suffix++) candidate = Path.Combine(parent, $"{basename} ({suffix})");
        return candidate;
    }

    public static string MsgFileName(string? subject, uint folderId, uint messageId) =>
        SafeAttachmentName(string.IsNullOrWhiteSpace(subject) ? "Без темы" : subject) + $"-{folderId:X8}-{messageId:X8}.msg";

    public static void ValidateMsgDestination(string destination)
    {
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Выберите новую папку для MSG. Существующие файлы не перезаписываются.");
        if (!Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(destination)))) throw new DirectoryNotFoundException("Родительская папка MSG не существует.");
    }
    // Never display email HTML in a browser: no script execution, trackers or navigation.
    public static string HtmlToText(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        const RegexOptions opts = RegexOptions.IgnoreCase | RegexOptions.Singleline;
        var timeout = TimeSpan.FromSeconds(2);
        var text = Regex.Replace(html, @"<(script|style)\b[^>]*>.*?</\1\s*>", "", opts, timeout);
        text = Regex.Replace(text, @"<\s*(br\b[^>]*|/p|/div|/tr|/li|/h[1-6])\s*>", "\n", opts, timeout);
        text = Regex.Replace(text, @"<[^>]+>", "", opts, timeout);
        return WebUtility.HtmlDecode(text).Trim();
    }

    public static string SafeAttachmentName(string? name)
    {
        // Treat both Windows and Unix separators as untrusted, even in cross-platform tests.
        var leaf = (name ?? "attachment.bin").Replace('\\', '/').Split('/').Last();
        leaf = Regex.Replace(leaf, "[<>:\"/\\\\|?*\\x00-\\x1f]", "_").Trim(' ', '.');
        if (leaf.Length == 0) leaf = "attachment.bin";
        if (Regex.IsMatch(leaf.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$", RegexOptions.IgnoreCase))
            leaf = "_" + leaf;
        return leaf.Length <= 120 ? leaf : leaf[..100] + ".bin";
    }

    public static void ValidateDestination(string source, string destination)
    {
        if (!string.Equals(Path.GetExtension(destination), ".pst", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Выберите выходной файл с расширением .pst.");
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Исходный и выходной файлы должны отличаться.");
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("Выходной файл уже существует. Выберите новое имя; перезапись запрещена.");
        if (!Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(destination))))
            throw new DirectoryNotFoundException("Выходная папка не существует.");
    }
}
