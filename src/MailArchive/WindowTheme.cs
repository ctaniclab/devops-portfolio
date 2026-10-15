using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace MailArchive;

// Keep native caption buttons, resizing, keyboard shortcuts and accessibility.
public static class WindowTheme
{
    public sealed record BackdropState(bool Requested, bool Active, int HResult, string Message);
    private sealed record CaptionStatus(int Dark, int Caption, int Text, int Border, BackdropState Backdrop);
    private static readonly ConditionalWeakTable<Window, CaptionStatus> Status = new();
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) => Apply(window);
        window.Activated += (_, _) => Apply(window);
    }

    public static BackdropState BackdropFor(Window window) => Status.TryGetValue(window, out var state)
        ? state.Backdrop : new(false, false, 0, "");

    private static BackdropState ConfigureBackdrop(Window window, IntPtr handle, ThemePalette palette)
    {
        string? unavailable = null;
        if (palette.NativeGlass)
        {
            if (!DisplaySettings.Current.GlassEffects) unavailable = "Эффекты стекла отключены в приложении";
            else if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) unavailable = "Для прозрачности нужна Windows 11 22H2 или новее";
            else if (SystemParameters.HighContrast) unavailable = "Непрозрачный режим: включена высокая контрастность Windows";
            else
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                    if (key?.GetValue("EnableTransparency") is int enabled && enabled == 0)
                        unavailable = "Непрозрачный режим: в Windows отключены эффекты прозрачности";
                }
                catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                { unavailable = "Непрозрачный режим: настройки прозрачности Windows недоступны"; }
            }
        }
        int result = 0;
        bool active = palette.NativeGlass && unavailable is null;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            int material = active ? 3 : 1; // Desktop Acrylic / none; documented DWMSBT values.
            result = DwmSetWindowAttribute(handle, 38, ref material, sizeof(int));
            if (active && result != 0) { active = false; unavailable = "Непрозрачный режим: эффект недоступен в текущем сеансе Windows"; }
        }
        var margins = active ? new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 } : new Margins();
        int frame = DwmExtendFrameIntoClientArea(handle, ref margins);
        if (active && frame != 0)
        {
            active = false; result = frame; unavailable = "Непрозрачный режим: размытие окна недоступно";
            margins = new(); DwmExtendFrameIntoClientArea(handle, ref margins);
            int none = 1; DwmSetWindowAttribute(handle, 38, ref none, sizeof(int));
        }
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is HwndTarget target)
            target.BackgroundColor = active ? Colors.Transparent : ThemeArtwork.Color(palette.Colors[0]);
        window.SetResourceReference(Window.BackgroundProperty, active ? "Canvas" : "CanvasOpaque");
        return new(palette.NativeGlass, active, result, active ? "Прозрачность Windows включена" : unavailable ?? "");
    }

    private static int ColorRef(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        return color.R | color.G << 8 | color.B << 16;
    }

    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        var palette = ThemePalette.Get(DisplaySettings.Current.Theme, DisplaySettings.IsDark);
        var backdrop = ConfigureBackdrop(window, handle, palette);
        int dark = DisplaySettings.IsDark ? 1 : 0;
        int darkResult = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        if (darkResult != 0) darkResult = DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
        Status.Remove(window);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Status.Add(window, new(darkResult, 0, 0, 0, backdrop)); return;
        }
        int caption = backdrop.Active ? -1 : ColorRef(palette.Colors[0]), text = ColorRef(palette.Colors[2]), border = ColorRef(palette.Colors[6]);
        int captionResult = DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        int textResult = DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        int borderResult = DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
        Status.Add(window, new(darkResult, captionResult, textResult, borderResult, backdrop));
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
        return Status.TryGetValue(window, out var status) && status is { Dark: 0, Caption: 0, Text: 0, Border: 0 }
            && (!status.Backdrop.Active || status.Backdrop.HResult == 0);
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
