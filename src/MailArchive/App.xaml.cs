using System.Text;
using System.Windows;
using System.Text.Json;
using Microsoft.Web.WebView2.Wpf;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

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
            bool mainWindowVerified = false;
            try
            {
                window = new MainWindow();
                window.Show();
                await Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                var art = (Image)window.FindName("HeaderArt");
                var open = (Button)window.FindName("OpenButton");
                open.ApplyTemplate();
                var chrome = (Border)open.Template.FindName("Chrome", open);
                if (art.Source is not BitmapSource bitmap || bitmap.PixelWidth < 256 || chrome.CornerRadius.TopLeft < 8)
                    throw new InvalidDataException("Application illustration/rounded UI was not loaded.");
                var screenshot = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                screenshot.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(screenshot));
                using (var file = File.Create(output + ".png")) encoder.Save(file);
                mainWindowVerified = true;
                var view = (WebView2)window.FindName("HtmlPreview");
                view.Visibility = Visibility.Visible;
                ((TextBlock)window.FindName("PreviewHint")).Visibility = Visibility.Collapsed;
                await PreviewSelfCheck.VerifyAsync(view).WaitAsync(TimeSpan.FromSeconds(45));
                exitCode = 0;
            }
            catch (Exception failure) { error = failure.ToString(); }
            finally
            {
                try { File.WriteAllText(output, JsonSerializer.Serialize(new { AppVersion = "1.2.0", HtmlRenderingVerified = exitCode == 0, MainWindowVerified = mainWindowVerified, Error = error })); }
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
