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
                window.Width = window.MinWidth;
                window.Height = window.MinHeight;
                window.Show();
                await Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                var art = (Image)window.FindName("HeaderArt");
                var open = (Button)window.FindName("OpenButton");
                open.ApplyTemplate();
                var chrome = (Border)open.Template.FindName("Chrome", open);
                if (art.Source is not BitmapSource bitmap || bitmap.PixelWidth < 256 || chrome.CornerRadius.TopLeft < 8)
                    throw new InvalidDataException("Application illustration/rounded UI was not loaded.");
                var surface = (FrameworkElement)window.Content;
                var pane = (Border)window.FindName("MessagePane");
                var save = (Button)window.FindName("SaveAttachmentButton");
                var tabs = (TabControl)window.FindName("BodyTabs");
                void CheckMessagePane()
                {
                    window.UpdateLayout();
                    var saveBounds = save.TransformToAncestor(pane).TransformBounds(new Rect(save.RenderSize));
                    if (saveBounds.Bottom > pane.ActualHeight || tabs.ActualHeight < 70)
                        throw new InvalidDataException("The message/attachment controls are clipped at the minimum window size.");
                }
                CheckMessagePane();
                var header = (TextBlock)window.FindName("MessageHeader");
                var emptyHeader = header.Text;
                header.Text = string.Join("\n", Enumerable.Repeat("Проверка длинного заголовка письма", 8));
                CheckMessagePane();
                header.Text = emptyHeader;
                window.UpdateLayout();
                // Capture client content, excluding the native frame that WPF cannot render.
                var margin = surface.Margin;
                var bounds = new Rect(0, 0, surface.ActualWidth + margin.Left + margin.Right, surface.ActualHeight + margin.Top + margin.Bottom);
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    drawing.DrawRectangle(window.Background, null, bounds);
                    drawing.DrawRectangle(new VisualBrush(surface), null, new Rect(margin.Left, margin.Top, surface.ActualWidth, surface.ActualHeight));
                }
                var screenshot = new RenderTargetBitmap((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height), 96, 96, PixelFormats.Pbgra32);
                screenshot.Render(visual);
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
                try { File.WriteAllText(output, JsonSerializer.Serialize(new { AppVersion = "1.2.1", HtmlRenderingVerified = exitCode == 0, MainWindowVerified = mainWindowVerified, Error = error })); }
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
