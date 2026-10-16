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
    public bool GlassEffects { get; set; } = true;
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
        Current.Theme = ThemePalette.Migrate(Current.Theme);
        var palette = ThemePalette.Get(Current.Theme, systemDark);
        IsDark = palette.Dark;
        var colors = palette.Colors;
        var names = new[] { "Canvas", "Surface", "Ink", "MutedInk", "Accent", "AccentSoft", "Line", "Field", "ScrollTrack", "ScrollThumb", "ScrollHover", "Selection" };
        for (int i = 0; i < names.Length; i++)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
            brush.Freeze(); Application.Current.Resources[names[i]] = brush;
        }
        Application.Current.Resources["Canvas"] = ThemeArtwork.Background(palette, palette.NativeGlass);
        Application.Current.Resources["CanvasOpaque"] = ThemeArtwork.Background(palette, false);
        Application.Current.Resources["PanelFill"] = ThemeArtwork.Panel(palette);
        Application.Current.Resources["HeaderFill"] = palette.NativeGlass ? ThemeArtwork.Panel(palette) : Brushes.Transparent;
        Application.Current.Resources["HeaderPadding"] = new Thickness(palette.NativeGlass ? 12 : 0);
        Application.Current.Resources["TabIdleFill"] = palette.NativeGlass ? ThemeArtwork.Brush(colors[7]) : Brushes.Transparent;
        // Acrylic already supplies the blurred desktop and its light/dark tint.
        // Additional opaque control layers would hide that backdrop again.
        if (palette.NativeGlass)
        {
            Application.Current.Resources["Field"] = ThemeArtwork.Brush(palette.Dark ? "#24101C30" : "#30FFFFFF");
            Application.Current.Resources["AccentSoft"] = ThemeArtwork.Brush(palette.Dark ? "#483D699B" : "#609CC8F2");
            Application.Current.Resources["TabIdleFill"] = ThemeArtwork.Brush(palette.Dark ? "#20101C30" : "#20FFFFFF");
            Application.Current.Resources["ScrollTrack"] = ThemeArtwork.Brush(palette.Dark ? "#302A5070" : "#30446C98");
            Application.Current.Resources["Line"] = ThemeArtwork.Brush(palette.Dark ? "#889CC9EE" : "#B0FFFFFF");
        }
        // Popups have their own HWND and must not depend on the main window backdrop.
        Application.Current.Resources["PopupFill"] = ThemeArtwork.Brush(palette.Dark ? "#1A2B48" : colors[0]);
        Application.Current.Resources["RowFill"] = palette.Scene != ThemeScene.Plain || palette.NativeGlass ? ThemeArtwork.Brush(palette.Dark ? "#071D3045" : "#08FFFFFF") : ThemeArtwork.Brush(colors[1]);
        Application.Current.Resources["RowAlternate"] = palette.Scene != ThemeScene.Plain || palette.NativeGlass ? ThemeArtwork.Brush(palette.Dark ? "#121C8EB0" : "#14285B96") : ThemeArtwork.Brush(colors[7]);
        Application.Current.Resources["UiFont"] = new FontFamily(palette.Font);
        Application.Current.Resources["TitleFont"] = new FontFamily(palette.TitleFont);
        Application.Current.Resources["PanelBorderSize"] = new Thickness(palette.BorderSize);
        Application.Current.Resources["MessageRowHeight"] = (double)palette.RowHeight;
        if (palette.Scene == ThemeScene.Liquid)
        {
            var edge = new LinearGradientBrush(ThemeArtwork.Color("#E0FFFFFF"), ThemeArtwork.Color("#606D9DC7"), 80); edge.Freeze();
            Application.Current.Resources["Line"] = edge;
        }
        Application.Current.Resources["CardRadius"] = new CornerRadius(palette.CardRadius);
        Application.Current.Resources["ButtonRadius"] = new CornerRadius(palette.ButtonRadius);
        Application.Current.Resources["FieldRadius"] = new CornerRadius(palette.Scene == ThemeScene.Liquid ? 20 : Math.Min(9, palette.ButtonRadius));
        Application.Current.Resources[SystemColors.HighlightBrushKey] = Application.Current.Resources["Selection"];
        Application.Current.Resources[SystemColors.HighlightTextBrushKey] = Application.Current.Resources["Ink"];
        Application.Current.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = Application.Current.Resources["Selection"];
        Application.Current.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Application.Current.Resources["Ink"];
        RenderOptions.ProcessRenderMode = Current.CompatibleRendering ? RenderMode.SoftwareOnly : RenderMode.Default;
        foreach (Window window in Application.Current.Windows) WindowTheme.Apply(window);
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
