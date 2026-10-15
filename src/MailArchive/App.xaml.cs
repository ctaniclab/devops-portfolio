using System.Text;
using System.Windows;
using System.Text.Json;
using Microsoft.Web.WebView2.Wpf;

namespace MailArchive;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (e.Args is ["--self-check-output", var output])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Window? window = null;
            int exitCode = 1;
            string? error = null;
            try
            {
                var view = new WebView2();
                window = new Window { Width = 640, Height = 400, Content = view, Title = "Mail Archive — проверка просмотра" };
                window.Show();
                await PreviewSelfCheck.VerifyAsync(view).WaitAsync(TimeSpan.FromSeconds(45));
                exitCode = 0;
            }
            catch (Exception failure) { error = failure.ToString(); }
            finally
            {
                try { File.WriteAllText(output, JsonSerializer.Serialize(new { AppVersion = "1.1.0", HtmlRenderingVerified = exitCode == 0, Error = error })); }
                catch { exitCode = 1; }
                window?.Close();
                Shutdown(exitCode);
            }
            return;
        }
        StartupUri = new Uri("MainWindow.xaml", UriKind.Relative);
        base.OnStartup(e);
    }
}
