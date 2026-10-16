using System.Globalization;
using System.Text.Json;

namespace MailArchive;

// Only explicit UI strings are translated. Message contents, paths and folder names
// are arguments, never translation keys, and are preserved verbatim.
public static class L
{
    public static string Language { get; private set; } = "ru";
    public static IReadOnlyDictionary<string, string> English { get; } = Read();
    private static Dictionary<string, string> Read()
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("MailArchive.Strings.en.json")
            ?? throw new InvalidOperationException("Missing English language resources.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    public static void SetLanguage(string language) => Language = language == "en" ? "en" : "ru";
    public static string T(string text) => Language == "en" && English.TryGetValue(text, out var translated) ? translated : text;
    public static string F(FormattableString value) => string.Format(CultureInfo.GetCultureInfo(Language == "en" ? "en-US" : "ru-RU"), T(value.Format), value.GetArguments());
}
