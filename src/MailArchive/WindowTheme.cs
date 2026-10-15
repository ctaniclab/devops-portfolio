using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MailArchive;

// Keep native caption buttons, resizing, keyboard shortcuts and accessibility.
public static class WindowTheme
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    public static void Attach(Window window) => window.SourceInitialized += (_, _) => Apply(window);

    private static int ColorRef(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        return color.R | color.G << 8 | color.B << 16;
    }

    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        int dark = DisplaySettings.IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var palette = ThemePalette.Get(DisplaySettings.Current.Theme, DisplaySettings.IsDark);
        int caption = ColorRef(palette.Colors[0]), text = ColorRef(palette.Colors[2]), border = ColorRef(palette.Colors[6]);
        DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
    }

    public static bool IsApplied(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return false;
        int code = DwmGetWindowAttribute(handle, 20, out int dark, sizeof(int));
        if (code != 0) code = DwmGetWindowAttribute(handle, 19, out dark, sizeof(int));
        if (code != 0 || (dark != 0) != DisplaySettings.IsDark) return false;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return true;
        var palette = ThemePalette.Get(DisplaySettings.Current.Theme, DisplaySettings.IsDark);
        return DwmGetWindowAttribute(handle, 35, out int caption, sizeof(int)) == 0 && caption == ColorRef(palette.Colors[0])
            && DwmGetWindowAttribute(handle, 36, out int text, sizeof(int)) == 0 && text == ColorRef(palette.Colors[2])
            && DwmGetWindowAttribute(handle, 34, out int border, sizeof(int)) == 0 && border == ColorRef(palette.Colors[6]);
    }
}
