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
        if (!ThemePalette.Names.Contains(Current.Theme)) Current.Theme = "Системная";
        var palette = ThemePalette.Get(Current.Theme, systemDark);
        IsDark = palette.Dark;
        var colors = palette.Colors;
        var names = new[] { "Canvas", "Surface", "Ink", "MutedInk", "Accent", "AccentSoft", "Line", "Field", "ScrollTrack", "ScrollThumb", "ScrollHover", "Selection" };
        for (int i = 0; i < names.Length; i++)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
            brush.Freeze(); Application.Current.Resources[names[i]] = brush;
        }
        if (palette.GradientEnd is not null)
        {
            var gradient = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(colors[0]),
                (Color)ColorConverter.ConvertFromString(palette.GradientEnd), 35);
            Brush canvas = gradient;
            if (palette.Stars)
            {
                var sky = new DrawingGroup();
                sky.Children.Add(new GeometryDrawing(gradient, null, new RectangleGeometry(new Rect(0, 0, 1400, 900))));
                for (int i = 0; i < 90; i++)
                {
                    double radius = i % 4 == 0 ? 1.5 : 0.7;
                    sky.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb((byte)(75 + i % 145), 217, 222, 255)), null,
                        new EllipseGeometry(new Point((i * 137 + 31) % 1400, (i * 251 + 19) % 900), radius, radius)));
                }
                canvas = new DrawingBrush(sky) { Stretch = Stretch.Fill };
            }
            canvas.Freeze(); Application.Current.Resources["Canvas"] = canvas;
        }
        Application.Current.Resources["CardRadius"] = new CornerRadius(palette.CardRadius);
        Application.Current.Resources["ButtonRadius"] = new CornerRadius(palette.ButtonRadius);
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
