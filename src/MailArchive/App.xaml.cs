using System.Text;
using System.Windows;

namespace MailArchive;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        DisplaySettings.Load();
        if (e.Args.Length is 2 or 4 && e.Args[0] == "--self-check-output")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            await UiSelfCheck.RunAsync(e.Args[1], e.Args.Length == 4 && e.Args[2] == "--fixture" ? e.Args[3] : null);
            return;
        }
        StartupUri = new Uri("MainWindow.xaml", UriKind.Relative);
        base.OnStartup(e);
    }
}
