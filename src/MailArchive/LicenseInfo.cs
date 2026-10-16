using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace MailArchive;

internal static class LicenseInfo
{
    public static string Text
    {
        get
        {
            var assembly = typeof(LicenseInfo).Assembly;
            return "Mail Archive " + assembly.GetName().Version + "\n\n" + string.Join("\n\n", assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith("MailArchive.Licenses.", StringComparison.Ordinal)).OrderBy(n => n.Contains("NOTICES") ? 0 : 1).ThenBy(n => n)
                .Select(n => { using var stream = assembly.GetManifestResourceStream(n)!; using var reader = new StreamReader(stream); return n + "\n\n" + reader.ReadToEnd(); }));
        }
    }
    public static void Show(Window owner)
    {
        var window = new Window { Owner = owner, Title = "О Mail Archive · лицензии", Width = 760, Height = 600,
            MinWidth = 600, MinHeight = 420, Icon = owner.Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(Control.ForegroundProperty, "Ink");
        window.SetResourceReference(Control.FontFamilyProperty, "UiFont");
        var panel = new DockPanel { Margin = new Thickness(16) };
        var close = new Button { Content = "Закрыть", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), IsCancel = true };
        close.Click += (_, _) => window.Close(); DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close);
        panel.Children.Add(new TextBox { Text = Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        window.Content = panel; WindowTheme.Attach(window); window.ShowDialog();
    }
}
