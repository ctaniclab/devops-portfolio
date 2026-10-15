using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using Microsoft.Win32;

namespace MailArchive;

public sealed class DisplaySettings
{
    public string Theme { get; set; } = "Системная";
    public bool CompatibleRendering { get; set; } = true;
    public static DisplaySettings Current { get; private set; } = new();
    public static bool IsDark { get; private set; }
    public static bool SuppressPersistence { get; set; }
    private static string Pathname => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MailArchive", "display.json");

    public static void Load()
    {
        try { Current = JsonSerializer.Deserialize<DisplaySettings>(File.ReadAllText(Pathname)) ?? new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { Current = new(); }
        Apply();
    }

    public static void Apply()
    {
        bool systemDark = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            systemDark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        IsDark = Current.Theme == "Тёмная" || (Current.Theme == "Системная" && systemDark);
        var colors = IsDark
            ? new[] { "#171D2A", "#232D40", "#F0F4FC", "#BAC8DF", "#5368C9", "#344666", "#465774", "#1C2536", "#34425C", "#A2B5D8", "#D0DEFA", "#314A72" }
            : new[] { "#EEF1F7", "#FFFFFF", "#18243A", "#4B5E7B", "#4659BD", "#E6ECFF", "#CFD8E8", "#F4F6FB", "#D6DEED", "#637BA6", "#3C5788", "#DBE6FC" };
        var names = new[] { "Canvas", "Surface", "Ink", "MutedInk", "Accent", "AccentSoft", "Line", "Field", "ScrollTrack", "ScrollThumb", "ScrollHover", "Selection" };
        for (int i = 0; i < names.Length; i++)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
            brush.Freeze(); Application.Current.Resources[names[i]] = brush;
        }
        RenderOptions.ProcessRenderMode = Current.CompatibleRendering ? RenderMode.SoftwareOnly : RenderMode.Default;
    }

    public static string? Save()
    {
        if (SuppressPersistence) return null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Pathname)!);
            var temporary = Pathname + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, JsonSerializer.Serialize(Current)); File.Move(temporary, Pathname, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return "Настройки применены, но не сохранены: " + error.Message; }
    }
}
