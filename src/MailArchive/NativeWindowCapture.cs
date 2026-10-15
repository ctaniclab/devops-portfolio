using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace MailArchive;

// UI diagnostics only: RenderTargetBitmap omits the native window frame/caption.
internal static class NativeWindowCapture
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")]
    private static extern uint ExtractIconEx(string file, int index, IntPtr large, IntPtr small, uint count);

    public static bool HasExecutableIcon(string file) => ExtractIconEx(file, -1, IntPtr.Zero, IntPtr.Zero, 0) > 0;

    public static void Save(Window window, string path)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (!GetWindowRect(handle, out var bounds)) throw new IOException("Cannot read native window bounds.");
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        if (width is < 1 or > 8192 || height is < 1 or > 8192) throw new IOException("Invalid native window capture size.");
        var source = GetWindowDC(handle);
        if (source == IntPtr.Zero) throw new IOException("Cannot acquire native window DC.");
        IntPtr memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            memory = CreateCompatibleDC(source); bitmap = CreateCompatibleBitmap(source, width, height);
            if (memory == IntPtr.Zero || bitmap == IntPtr.Zero) throw new IOException("Cannot allocate native capture bitmap.");
            previous = SelectObject(memory, bitmap);
            if (!PrintWindow(handle, memory, 2)) throw new IOException("Native window capture failed.");
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(path); encoder.Save(file);
        }
        finally
        {
            if (previous != IntPtr.Zero && memory != IntPtr.Zero) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(handle, source);
        }
    }
}
