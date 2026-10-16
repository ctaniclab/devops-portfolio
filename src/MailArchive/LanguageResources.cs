using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Data;

namespace MailArchive;

internal static class LanguageResources
{
    public static void Apply(string language)
    {
        L.SetLanguage(language);
        foreach (var text in L.English.Keys)
        {
            var key = "UI_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()[..16];
            Application.Current.Resources[key] = L.T(text);
        }
    }
}

public sealed class ThemeNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => L.T(value as string ?? "");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
