using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using XstReader;

namespace MailArchive;

public static class UiSelfCheck
{
    private static void VerifyTextContrast(Control control)
    {
        double Luminance(Color color)
        {
            double Channel(byte value) { double v = value / 255d; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
            return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        }
        var foreground = Luminance(((SolidColorBrush)control.Foreground).Color);
        var background = Luminance(((SolidColorBrush)Application.Current.Resources["Surface"]).Color);
        if ((Math.Max(foreground, background) + 0.05) / (Math.Min(foreground, background) + 0.05) < 4.5)
            throw new IOException("Format control text has insufficient contrast in the active theme.");
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var item in Descendants<T>(child)) yield return item;
        }
    }
    public static void Capture(Window window, string output)
    {
        window.UpdateLayout();
        var surface = (FrameworkElement)window.Content;
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
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(screenshot));
        using var file = File.Create(output); encoder.Save(file);
    }
    private static async Task VerifyScrollBarsAsync(Window owner)
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            Content = new Border { Width = 2000, Height = 2000, Background = Brushes.SlateBlue } };
        var window = new Window { Owner = owner, Width = 400, Height = 300, Content = scroll, ShowInTaskbar = false };
        try
        {
            window.Show(); await owner.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
            foreach (var bar in Descendants<ScrollBar>(scroll).Where(b => b.IsVisible))
            {
                bar.ApplyTemplate(); var track = (Track)bar.Template.FindName("PART_Track", bar);
                track.Thumb.ApplyTemplate();
                var chrome = (Border)track.Thumb.Template.FindName("ThumbChrome", track.Thumb);
                if (chrome.CornerRadius.TopLeft < 5 || bar.Maximum <= 0) throw new IOException("Rounded scrollbar thumb was not loaded.");
                double before = bar.Orientation == Orientation.Vertical ? scroll.VerticalOffset : scroll.HorizontalOffset;
                var command = bar.Orientation == Orientation.Vertical ? ScrollBar.PageDownCommand : ScrollBar.PageRightCommand;
                command.Execute(null, bar);
                await owner.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
                double after = bar.Orientation == Orientation.Vertical ? scroll.VerticalOffset : scroll.HorizontalOffset;
                if (after <= before) throw new IOException("Themed scrollbar paging did not scroll its content.");
                track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                track.Thumb.RaiseEvent(new DragDeltaEventArgs(bar.Orientation == Orientation.Horizontal ? 20 : 0, bar.Orientation == Orientation.Vertical ? 20 : 0) { RoutedEvent = Thumb.DragDeltaEvent });
                track.Thumb.RaiseEvent(new DragCompletedEventArgs(20, 20, false) { RoutedEvent = Thumb.DragCompletedEvent });
                await owner.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
                double dragged = bar.Orientation == Orientation.Vertical ? scroll.VerticalOffset : scroll.HorizontalOffset;
                if (dragged <= after) throw new IOException("Themed scrollbar thumb dragging did not scroll its content.");
            }
            if (Descendants<ScrollBar>(scroll).Count(b => b.IsVisible) != 2) throw new IOException("Both scrollbar orientations must be tested.");
        }
        finally { window.Close(); }
    }
    public static async Task RunAsync(string output, string? fixture)
    {
        DisplaySettings.SuppressPersistence = true;
        DisplaySettings.Current.Theme = "Светлая"; DisplaySettings.Current.CompatibleRendering = true; DisplaySettings.Apply();
        MainWindow? main = null; MessageWindow? reader = null;
        bool mainVerified = false, readerVerified = false, scrollbarsVerified = false, htmlVerified = false, wizardVerified = false, captionVerified = false;
        var verifiedThemes = new List<string>();
        var backdrops = new Dictionary<string, WindowTheme.BackdropState>();
        bool glassFallbackVerified = false, progressUiVerified = false, iconVerified = false, glassPanelsVerified = false, disabledTreeVerified = false, tabBoundsVerified = false, offlineMsgVerified = false;
        string? error = null; int exit = 1;
        try
        {
            main = new MainWindow { Width = 1000, Height = 680 }; main.Show();
            using (var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app-icon.ico")).Stream)
            using (var iconHeader = new BinaryReader(iconStream))
            {
                if (iconHeader.ReadUInt16() != 0 || iconHeader.ReadUInt16() != 1 || iconHeader.ReadUInt16() < 7
                    || main.Icon is null || !NativeWindowCapture.HasExecutableIcon(Environment.ProcessPath!))
                    throw new IOException("Selected multi-resolution icon was not embedded in the window and published EXE.");
                iconVerified = true;
            }
            if (!LicenseInfo.Text.Contains("Mozilla Public License Version 2.0") || !LicenseInfo.Text.Contains("11b5d876cdebb472f1845dfa55e9e9b953aed65f"))
                throw new IOException("Standalone EXE is missing its OpenMcdf license or corresponding-source notice.");
            await main.Dispatcher.InvokeAsync(main.UpdateLayout, DispatcherPriority.ApplicationIdle);
            var open = (Button)main.FindName("OpenButton"); open.ApplyTemplate();
            if (((Border)open.Template.FindName("Chrome", open)).CornerRadius.TopLeft != ThemePalette.Get("Светлая").ButtonRadius
                || ((Image)main.FindName("HeaderArt")).Source is not BitmapSource { PixelWidth: >= 256 }) throw new IOException("Main window resources were not loaded.");
            if (fixture is not null && !await main.LoadForCheckAsync(fixture)) throw new IOException("Actual archive viewing failed.");
            Capture(main, output + ".png");
            await VerifyScrollBarsAsync(main); scrollbarsVerified = true;
            ((ComboBox)main.FindName("ThemeChoice")).SelectedItem = "Тёмная";
            await main.Dispatcher.InvokeAsync(main.UpdateLayout, DispatcherPriority.ApplicationIdle);
            Capture(main, output + ".dark.png"); mainVerified = true;
            int messageCount = ((DataGrid)main.FindName("Messages")).Items.Count;
            var extraThemes = new Dictionary<string, string> { ["Классическая"] = "classic", ["Liquid Glass"] = "liquid", ["Прозрачная"] = "transparent", ["Космос"] = "space", ["Океан"] = "ocean", ["Аврора"] = "aurora", ["Неон"] = "neon", ["Терминал"] = "terminal" };
            foreach (var theme in ThemePalette.Names)
            {
                ((ComboBox)main.FindName("ThemeChoice")).SelectedItem = theme;
                await main.Dispatcher.InvokeAsync(main.UpdateLayout, DispatcherPriority.ApplicationIdle);
                VerifyTextContrast((Button)main.FindName("ReadMessageButton"));
                VerifyTextContrast((CheckBox)main.FindName("ShowServiceFolders"));
                VerifyTextContrast((ComboBox)main.FindName("ThemeChoice"));
                var expectedPalette = ThemePalette.Get(theme, DisplaySettings.IsDark);
                if (((Border)open.Template.FindName("Chrome", open)).CornerRadius.TopLeft != expectedPalette.ButtonRadius || main.FontFamily.Source != expectedPalette.Font)
                    throw new IOException("Theme geometry/font did not change: " + theme);
                if (!WindowTheme.IsConfigured(main)) throw new IOException("Native window caption did not follow theme: " + theme + ". " + WindowTheme.Describe(main));
                backdrops[theme] = WindowTheme.BackdropFor(main);
                if (expectedPalette.NativeGlass && backdrops[theme].HResult != 0) throw new IOException("Windows backdrop API failed: " + backdrops[theme]);
                if (((DataGrid)main.FindName("Messages")).Items.Count != messageCount) throw new IOException("Theme switch changed the loaded mailbox.");
                var tree = (TreeView)main.FindName("Folders"); tree.IsEnabled = false; tree.ApplyTemplate();
                var chrome = (Border)tree.Template.FindName("TreeChrome", tree);
                if (chrome.Background is not SolidColorBrush { Color.A: 0 } || !Equals(tree.Foreground, Application.Current.Resources["Ink"]))
                    throw new IOException("Disabled folder tree acquired a system background or foreground: " + theme);
                await main.Dispatcher.InvokeAsync(main.UpdateLayout, DispatcherPriority.ApplicationIdle);
                if (expectedPalette.NativeGlass)
                {
                    var fill = (Brush)Application.Current.Resources["PanelFill"];
                    bool transparent = fill is SolidColorBrush solid ? solid.Color.A <= 112
                        : fill is GradientBrush gradient && gradient.GradientStops.All(stop => stop.Color.A <= 112);
                    if (!transparent || ((SolidColorBrush)Application.Current.Resources["Field"]).Color.A > 80)
                        throw new IOException("Opaque panel or field hides the native glass backdrop: " + theme);
                }
                var tabs = (TabControl)main.FindName("ModeTabs");
                var headerPanel = Descendants<TabPanel>(tabs).First();
                foreach (TabItem tab in tabs.Items)
                {
                    tab.ApplyTemplate();
                    var tabChrome = (Border)tab.Template.FindName("Chrome", tab);
                    var bounds = tabChrome.TransformToAncestor(headerPanel).TransformBounds(new Rect(tabChrome.RenderSize));
                    var localBounds = tabChrome.TransformToAncestor(tab).TransformBounds(new Rect(tabChrome.RenderSize));
                    if (bounds.Top < -.5 || bounds.Bottom > headerPanel.ActualHeight + .5
                        || bounds.Left < -.5 || bounds.Right > headerPanel.ActualWidth + .5
                        || localBounds.Left < -.5 || localBounds.Right > tab.ActualWidth + .5)
                        throw new IOException("Tab capsule is clipped by the native tab header: " + theme);
                }
                if (extraThemes.TryGetValue(theme, out var filename))
                {
                    Capture(main, output + "." + filename + ".png");
                    NativeWindowCapture.Save(main, output + "." + filename + ".window.png");
                }
                tree.IsEnabled = true;
                verifiedThemes.Add(theme);
            }
            glassPanelsVerified = disabledTreeVerified = tabBoundsVerified = true;
            if (fixture is not null)
            {
                var msgDirectory = output + ".msg-export";
                var result = await OutlookConverter.ConvertAsync(fixture, msgDirectory, new Progress<ConversionProgress>(), CancellationToken.None, format: ExportFormat.Msg);
                if (!result.Report.Completed || result.Report.Converted == 0 || !result.Report.MsgReadBackVerified || result.Report.OutlookReadBackVerified)
                    throw new IOException("MSG export without Outlook failed: " + result.Report.FatalError + "; " + string.Join("; ", result.Report.Issues.Select(i => i.Reason)));
                offlineMsgVerified = true;
            }
            ((ComboBox)main.FindName("ThemeChoice")).SelectedItem = "Тёмная";
            captionVerified = true;
            ((ComboBox)main.FindName("ThemeChoice")).SelectedItem = "Liquid Glass";
            ((CheckBox)main.FindName("GlassEffects")).IsChecked = false;
            if (WindowTheme.BackdropFor(main).Active || !ReferenceEquals(main.Background, Application.Current.Resources["CanvasOpaque"]))
                throw new IOException("Disabling glass failed to restore an opaque readable window.");
            Capture(main, output + ".liquid-fallback.png");
            ((CheckBox)main.FindName("GlassEffects")).IsChecked = true;
            var backdrop = WindowTheme.BackdropFor(main);
            if (!backdrop.Requested || (backdrop.Active && backdrop.HResult != 0)) throw new IOException("Native glass backdrop configuration failed.");
            ((ComboBox)main.FindName("ThemeChoice")).SelectedItem = "Космос";
            if (WindowTheme.BackdropFor(main).Active || !ReferenceEquals(main.Background, Application.Current.Resources["CanvasOpaque"]))
                throw new IOException("Switching away from glass retained a transparent backdrop.");
            glassFallbackVerified = true;
            var timing = new ConversionEstimate();
            timing.Observe(1, 10, TimeSpan.FromSeconds(20));
            main.ShowProgressForCheck(timing.Observe(2, 10, TimeSpan.FromSeconds(22)));
            if (((ProgressBar)main.FindName("Progress")).IsIndeterminate || !((TextBlock)main.FindName("ConversionTiming")).Text.Contains("20 %"))
                throw new IOException("Determinate conversion percentage and ETA were not visible.");
            progressUiVerified = true;
            Capture(main, output + ".progress.png");
            ((Grid)main.FindName("ProgressRow")).Visibility = Visibility.Collapsed;
            ((ComboBox)main.FindName("ThemeChoice")).SelectedItem = "Тёмная";
            var message = new Message { Subject = "Тест чтения письма", From = "Демонстрация интерфейса", BodyHtml = "<table><tr><td>Тест письма</td></tr></table><p><b>Форматирование и кириллица</b></p><script>document.body.innerText='unsafe'</script>" + string.Concat(Enumerable.Range(1, 45).Select(i => "<p>Демонстрационный текст письма " + i + "</p>")) };
            reader = new MessageWindow(null, message) { Owner = main, Width = 820, Height = 640 };
            var view = (Microsoft.Web.WebView2.Wpf.WebView2)reader.FindName("HtmlPreview");
            var navigated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            view.NavigationCompleted += (_, e) =>
            {
                if (view.CoreWebView2?.Source.StartsWith("https://mailarchive.invalid/message/", StringComparison.Ordinal) != true) return;
                if (e.IsSuccess) navigated.TrySetResult(); else navigated.TrySetException(new IOException("Reader navigation failed: " + e.WebErrorStatus));
            };
            reader.Show(); await reader.Ready.Task.WaitAsync(TimeSpan.FromSeconds(45)); await navigated.Task.WaitAsync(TimeSpan.FromSeconds(30));
            if (!WindowTheme.IsConfigured(reader)) throw new IOException("Reader caption did not follow the active theme.");
            if (JsonSerializer.Deserialize<string>(await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('table td').textContent")) != "Тест письма"
                || view.CoreWebView2.Settings.IsScriptEnabled || await view.CoreWebView2.ExecuteScriptAsync("document.scripts.length") != "0") throw new IOException("Reader HTML content/script checks failed.");
            htmlVerified = true;
            reader.UpdateLayout();
            if (view.ActualHeight < 160) throw new IOException("Reader body was collapsed at its minimum size.");
            using (var capture = new MemoryStream())
            {
                await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, capture);
                capture.Position = 0;
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = capture; bitmap.EndInit(); bitmap.Freeze();
                var image = new Image { Source = bitmap, Stretch = Stretch.Fill };
                ((Grid)view.Parent).Children.Add(image); view.Visibility = Visibility.Collapsed;
                Capture(reader, output + ".reader.png");
            }
            readerVerified = true; reader.Close(); reader = null;
            if (fixture is not null)
            {
                var wizard = new ExportWizard(fixture, false) { Owner = main };
                wizard.Show(); await wizard.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
                var rows = ((DataGrid)wizard.FindName("MessageChoices")).Items.Cast<MessageChoice>().ToList();
                if (rows.Count < 2) throw new IOException("Fixture must provide at least two messages for selection checks.");
                rows[0].Chosen = true; rows[1].Chosen = true;
                await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                if (!((Button)wizard.FindName("NextButton")).IsEnabled) throw new IOException("Selection count did not enable the next step.");
                ((Button)wizard.FindName("NextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (wizard.Selected.Count != 2) throw new IOException("Two individually selected messages were not retained.");
                ((Button)wizard.FindName("BackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((Button)wizard.FindName("AllButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((Button)wizard.FindName("NextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (((Border)wizard.FindName("FormatStep")).Visibility != Visibility.Visible || wizard.Selected.Count != rows.Count) throw new IOException("Select-all did not advance to format choice with every message.");
                ((RadioButton)wizard.FindName("MsgChoice")).IsChecked = true;
                foreach (string theme in ThemePalette.Names)
                {
                    ((ComboBox)main.FindName("ThemeChoice")).SelectedItem = theme;
                    await wizard.Dispatcher.InvokeAsync(wizard.UpdateLayout, DispatcherPriority.ApplicationIdle);
                    VerifyTextContrast((RadioButton)wizard.FindName("PstChoice"));
                    VerifyTextContrast((RadioButton)wizard.FindName("MsgChoice"));
                    if (!WindowTheme.IsConfigured(wizard)) throw new IOException("Export wizard caption did not follow theme: " + theme);
                }
                ((ComboBox)main.FindName("ThemeChoice")).SelectedItem = "Тёмная";
                Capture(wizard, output + ".wizard.png");
                wizard.Close(); wizardVerified = true;
            }
            using (var tray = new ConversionTray(main))
            {
                tray.Update(new(42, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3), false, false), "Проверка трея · 42 из 100");
                try
                {
                    if (!tray.HideToTray() || main.IsVisible) throw new IOException("Native tray icon did not hide the main window.");
                    tray.ShowPreviewForCheck();
                    await main.Dispatcher.InvokeAsync(tray.PreviewForCheck.UpdateLayout, DispatcherPriority.ApplicationIdle);
                    Capture(tray.PreviewForCheck, output + ".tray.png");
                }
                finally { tray.Restore(); }
                if (!main.IsVisible) throw new IOException("Tray restore did not restore the main window.");
            }
            foreach (var language in new[] { "ru", "en" })
            {
                LanguageResources.Apply(language);
                DisplaySettings.Current.Theme = "Светлая"; DisplaySettings.Apply();
                var localized = new MainWindow { Width = 1320, Height = 880 };
                try
                {
                    localized.Show();
                    if (fixture is not null && !await localized.LoadForCheckAsync(fixture)) throw new IOException("Localized archive browsing failed.");
                    if ((string)((Button)localized.FindName("OpenButton")).Content != L.T("Открыть OST / PST…"))
                        throw new IOException("Main window did not load its language resources.");
                    await localized.Dispatcher.InvokeAsync(localized.UpdateLayout, DispatcherPriority.ApplicationIdle);
                    Capture(localized, output + ".main-" + language + ".png");
                    ((TabControl)localized.FindName("ModeTabs")).SelectedIndex = 1;
                    await localized.Dispatcher.InvokeAsync(localized.UpdateLayout, DispatcherPriority.ApplicationIdle);
                    Capture(localized, output + ".convert-" + language + ".png");
                    if (fixture is not null)
                    {
                        var wizard = new ExportWizard(fixture, false) { Owner = localized };
                        try
                        {
                            wizard.Show(); await wizard.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
                            if ((string)((Button)wizard.FindName("AllButton")).Content != L.T("Выбрать все"))
                                throw new IOException("Export wizard did not load its language resources.");
                        }
                        finally { wizard.Close(); }
                    }
                }
                finally { localized.Close(); }
            }
            LanguageResources.Apply("ru");
            exit = 0;
        }
        catch (Exception failure) { error = failure.ToString(); }
        finally
        {
            reader?.Close(); main?.Close();
            File.WriteAllText(output, JsonSerializer.Serialize(new { AppVersion = "1.7.0", MainWindowVerified = mainVerified, ReaderWindowVerified = readerVerified,
                HtmlRenderingVerified = htmlVerified, ThemedScrollBarsVerified = scrollbarsVerified, ExportWizardVerified = wizardVerified,
                ThemesVerified = verifiedThemes, AllThemesVerified = verifiedThemes.Count == ThemePalette.Names.Count,
                NativeCaptionApiAccepted = captionVerified, NativeDarkModeVerified = captionVerified,
                NativeCaptionColorReadBackAvailable = false,
                GlassFallbackVerified = glassFallbackVerified, ConversionProgressUiVerified = progressUiVerified, Backdrops = backdrops, ExecutableIconVerified = iconVerified,
                GlassPanelsVerified = glassPanelsVerified, DisabledTreeVerified = disabledTreeVerified, TabBoundsVerified = tabBoundsVerified, OfflineMsgExportVerified = offlineMsgVerified,
                TrayVerified = exit == 0, LocalizationVerified = exit == 0, HdrHardwareVerified = false, CompatibleRendering = DisplaySettings.Current.CompatibleRendering, Error = error }));
            Application.Current.Shutdown(exit);
        }
    }
}
