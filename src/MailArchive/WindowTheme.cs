using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MailArchive;

// Keep native caption buttons, resizing, keyboard shortcuts and accessibility.
public static class WindowTheme
{
    private sealed record CaptionStatus(int Dark, int Caption, int Text, int Border);
    private static readonly ConditionalWeakTable<Window, CaptionStatus> Status = new();
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);

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
        int darkResult = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        if (darkResult != 0) darkResult = DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
        Status.Remove(window);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Status.Add(window, new(darkResult, 0, 0, 0)); return;
        }
        var palette = ThemePalette.Get(DisplaySettings.Current.Theme, DisplaySettings.IsDark);
        int caption = ColorRef(palette.Colors[0]), text = ColorRef(palette.Colors[2]), border = ColorRef(palette.Colors[6]);
        int captionResult = DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        int textResult = DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        int borderResult = DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
        Status.Add(window, new(darkResult, captionResult, textResult, borderResult));
    }

    public static bool IsConfigured(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return false;
        int code = DwmGetWindowAttribute(handle, 20, out int dark, sizeof(int));
        if (code != 0) code = DwmGetWindowAttribute(handle, 19, out dark, sizeof(int));
        if (code != 0 || (dark != 0) != DisplaySettings.IsDark) return false;
        // Caption/text/border colors support DwmSetWindowAttribute, not DwmGetWindowAttribute
        // (the latter returns E_INVALIDARG even on Windows 11 build 26100).
        return Status.TryGetValue(window, out var status) && status is { Dark: 0, Caption: 0, Text: 0, Border: 0 };
    }

    public static string Describe(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        DwmIsCompositionEnabled(out bool composition);
        Status.TryGetValue(window, out var status);
        return $"OS={Environment.OSVersion.Version}; composition={composition}; dark={DisplaySettings.IsDark}; setters={status}; "
            + string.Join("; ", new[] { 19, 20, 34, 35, 36 }.Select(attribute =>
            {
                int result = DwmGetWindowAttribute(handle, attribute, out int value, sizeof(int));
                return $"DWM{attribute}: HRESULT=0x{unchecked((uint)result):X8}, value=0x{unchecked((uint)value):X8}";
            }));
    }
}
