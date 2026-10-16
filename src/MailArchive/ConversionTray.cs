using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MailArchive;

// Native notification area: no additional runtime or shell extension required.
internal sealed class ConversionTray : IDisposable
{
    private const int Callback = 0x8000 + 71;
    private readonly Window owner;
    private readonly HwndSource source;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly DispatcherTimer hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Window preview;
    private readonly Border panel;
    private readonly TextBlock heading = new() { FontSize = 16, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock timing = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock counts = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), FontSize = 12 };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Height = 8, Margin = new Thickness(0, 14, 0, 0) };
    private NotifyData data;
    private bool visible, disposed;
    private DateTime lastHover;
    public bool Hidden => visible && !owner.IsVisible;

    public ConversionTray(Window owner)
    {
        this.owner = owner;
        var hwnd = new WindowInteropHelper(owner).EnsureHandle();
        source = HwndSource.FromHwnd(hwnd)!;
        var iconPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mailarchive-icon-" + Guid.NewGuid() + ".ico");
        IntPtr icon;
        try
        {
            using (var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app-icon.ico"))!.Stream)
            using (var file = System.IO.File.Create(iconPath)) resource.CopyTo(file);
            icon = LoadImage(IntPtr.Zero, iconPath, 1, 32, 32, 0x10);
        }
        finally { System.IO.File.Delete(iconPath); }
        if (icon == IntPtr.Zero) throw new IOException(L.T("Не удалось загрузить значок трея."));
        data = new NotifyData { Size = Marshal.SizeOf<NotifyData>(), Window = hwnd, Id = 1, Flags = 1 | 2 | 4,
            CallbackMessage = Callback, Icon = icon, Tip = "Mail Archive", Info = "", InfoTitle = "" };
        var stack = new StackPanel();
        foreach (var child in new UIElement[] { heading, progress, timing, counts }) stack.Children.Add(child);
        var open = new Button { Content = L.T("Открыть Mail Archive"), Margin = new Thickness(0, 14, 0, 0) };
        open.Click += (_, _) => Restore(); stack.Children.Add(open);
        panel = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(18), Child = stack };
        ApplySurface();
        panel.SetResourceReference(Border.BorderBrushProperty, "Accent");
        progress.SetResourceReference(Control.ForegroundProperty, "Accent");
        preview = new Window { Width = 340, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None,
            AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false,
            ShowActivated = false, Topmost = true, ResizeMode = ResizeMode.NoResize, Content = panel };
        preview.SetResourceReference(Control.ForegroundProperty, "Ink");
        preview.SetResourceReference(Control.FontFamilyProperty, "UiFont");
        source.AddHook(Hook);
        hoverTimer.Tick += (_, _) =>
        {
            var identity = new IconIdentity { Size = Marshal.SizeOf<IconIdentity>(), Window = data.Window, Id = data.Id };
            GetCursorPos(out var point);
            bool overIcon = Shell_NotifyIconGetRect(ref identity, out var rect) == 0
                && point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;
            if (!preview.IsMouseOver && !overIcon && DateTime.UtcNow - lastHover > TimeSpan.FromMilliseconds(800))
            { preview.Hide(); hoverTimer.Stop(); }
        };
    }

    public bool HideToTray()
    {
        if (!visible)
        {
            visible = Shell_NotifyIcon(0, ref data);
            if (!visible) return false; // Never hide the only reachable window without an icon.
        }
        owner.Hide();
        return true;
    }

    public void Restore()
    {
        preview.Hide(); hoverTimer.Stop();
        owner.Show();
        if (owner.WindowState == WindowState.Minimized) owner.WindowState = WindowState.Normal;
        owner.Activate();
        if (visible) Shell_NotifyIcon(2, ref data);
        visible = false;
    }

    public void Update(ConversionEstimateValue estimate, string summary)
    {
        ApplySurface();
        heading.Text = L.T("Конвертация");
        timing.Text = estimate.Label; counts.Text = summary;
        progress.IsIndeterminate = estimate.Percent is null; progress.Value = estimate.Percent ?? 0;
        data.Tip = "Mail Archive · " + (estimate.Percent is double p ? $"{p:0}%" : L.T("Подготовка"))
            + (estimate.Remaining is TimeSpan left ? " · ≈ " + ConversionEstimateValue.Duration(left) : "");
        if (visible) Shell_NotifyIcon(1, ref data);
    }

    public void Finish(string title, string summary, bool completed)
    {
        ApplySurface();
        heading.Text = title; timing.Text = summary; counts.Text = L.T("Нажмите значок, чтобы посмотреть результат и отчёт.");
        progress.IsIndeterminate = false;
        if (completed) progress.Value = 100;
        data.Tip = "Mail Archive · " + title;
        if (visible)
        {
            data.Flags |= 0x10; data.InfoTitle = "Mail Archive"; data.Info = title; data.InfoFlags = completed ? 1u : 2u;
            Shell_NotifyIcon(1, ref data); data.Flags &= ~0x10u;
        }
    }

    private void ApplySurface()
    {
        var surface = ThemeArtwork.Color(ThemePalette.Get(DisplaySettings.Current.Theme, DisplaySettings.IsDark).Colors[7]);
        surface.A = 255; panel.Background = new SolidColorBrush(surface);
    }

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == taskbarCreated && visible && !Shell_NotifyIcon(0, ref data)) Restore();
        if (message != Callback) return IntPtr.Zero;
        switch (lParam.ToInt32())
        {
            case 0x200: // WM_MOUSEMOVE; the overflow notification area sends this too.
                lastHover = DateTime.UtcNow;
                if (!preview.IsVisible)
                {
                    GetCursorPos(out var cursor);
                    preview.Show();
                    var previewHandle = new WindowInteropHelper(preview).Handle;
                    SetWindowPos(previewHandle, IntPtr.Zero, cursor.X, cursor.Y, 0, 0, 0x15);
                    var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                    if (GetMonitorInfo(MonitorFromPoint(cursor, 2), ref monitor) && GetWindowRect(previewHandle, out var bounds))
                    {
                        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
                        SetWindowPos(previewHandle, IntPtr.Zero,
                            Math.Clamp(cursor.X - width + 24, monitor.Work.Left, Math.Max(monitor.Work.Left, monitor.Work.Right - width)),
                            Math.Clamp(cursor.Y - height - 14, monitor.Work.Top, Math.Max(monitor.Work.Top, monitor.Work.Bottom - height)), 0, 0, 0x15);
                    }
                }
                hoverTimer.Start(); break;
            case 0x202: case 0x203: case 0x405: Restore(); break;
            case 0x205:
                var menu = new ContextMenu();
                var restore = new MenuItem { Header = L.T("Открыть Mail Archive") };
                restore.Click += (_, _) => Restore(); menu.Items.Add(restore);
                var stop = new MenuItem { Header = L.T("Управление конвертацией…") };
                stop.Click += (_, _) => Restore(); menu.Items.Add(stop);
                SetForegroundWindow(hwnd); menu.IsOpen = true; break;
        }
        handled = true; return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        hoverTimer.Stop(); preview.Close();
        if (visible) Shell_NotifyIcon(2, ref data);
        source.RemoveHook(Hook); DestroyIcon(data.Icon);
    }

    internal Window PreviewForCheck => preview;
    internal void ShowPreviewForCheck()
    {
        bool handled = false;
        Hook(data.Window, Callback, IntPtr.Zero, new IntPtr(0x200), ref handled);
        if (!handled || !preview.IsVisible || progress.Value != 42 || !timing.Text.Contains("42"))
            throw new IOException(L.T("Окно прогресса трея не отобразило текущую оценку."));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData
    {
        public int Size; public IntPtr Window; public uint Id, Flags; public int CallbackMessage; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct IconIdentity { public int Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref IconIdentity identity, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint command, ref NotifyData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
}
