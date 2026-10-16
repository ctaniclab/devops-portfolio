using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using XstReader;
using Message = XstReader.Message;

namespace MailArchive;

public partial class MainWindow : Window
{
    private XstFile? archive;
    private Folder? currentFolder;
    private string? sourcePath;
    private FileStream? readGuard;
    private bool busy;
    private bool initializing = true;
    private bool suppressSelection;
    private CancellationTokenSource? conversion;
    private string? lastReportPath;
    private string? lastOutputPath;
    private string? lastMsgDirectory;
    private readonly Stopwatch conversionWatch = new();
    private readonly DispatcherTimer conversionTicker = new() { Interval = TimeSpan.FromSeconds(1) };
    private ConversionEstimate? conversionEstimate;
    private ConversionProgress? lastConversionProgress;
    private ConversionTray? tray;

    public MainWindow()
    {
        InitializeComponent();
        TaskbarItemInfo = new System.Windows.Shell.TaskbarItemInfo();
        WindowTheme.Attach(this);
        ThemeChoice.ItemsSource = ThemePalette.Names;
        ThemeChoice.SelectedItem = DisplaySettings.Current.Theme;
        LanguageChoice.SelectedIndex = L.Language == "en" ? 1 : 0;
        CompatibleRendering.IsChecked = DisplaySettings.Current.CompatibleRendering;
        GlassEffects.IsChecked = DisplaySettings.Current.GlassEffects;
        initializing = false;
        Activated += (_, _) => UpdateThemeHint();
        UpdateThemeHint();
        conversionTicker.Tick += (_, _) => UpdateConversionTiming();
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && conversion is not null) HideInTray(); };
        Closed += (_, _) => tray?.Dispose();
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || ThemeChoice.SelectedItem is not string theme) return;
        DisplaySettings.Current.Theme = theme; ApplyDisplay();
    }
    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (initializing) return;
        DisplaySettings.Current.Language = LanguageChoice.SelectedIndex == 1 ? "en" : "ru";
        var error = DisplaySettings.Save();
        Status.Text = error ?? L.T("Язык изменится после перезапуска приложения. Текущая конвертация продолжится.");
    }
    private void Compatibility_Changed(object sender, RoutedEventArgs e)
    {
        if (initializing) return;
        DisplaySettings.Current.CompatibleRendering = CompatibleRendering.IsChecked == true; ApplyDisplay();
    }
    private void GlassEffects_Changed(object sender, RoutedEventArgs e)
    {
        if (initializing) return;
        DisplaySettings.Current.GlassEffects = GlassEffects.IsChecked == true; ApplyDisplay();
    }
    private void ApplyDisplay()
    {
        DisplaySettings.Apply();
        UpdateThemeHint();
        var error = DisplaySettings.Save();
        if (error is not null) Status.Text = error;
    }
    private void UpdateThemeHint()
    {
        var palette = ThemePalette.Get(DisplaySettings.Current.Theme, DisplaySettings.IsDark);
        var effect = WindowTheme.BackdropFor(this);
        GlassEffects.Visibility = palette.NativeGlass ? Visibility.Visible : Visibility.Collapsed;
        ThemeHint.Text = palette.NativeGlass ? effect.Message : L.T(palette.Description);
    }
    private void About_Click(object sender, RoutedEventArgs e) => LicenseInfo.Show(this);
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = L.T("Архивы Outlook (*.ost;*.pst)|*.ost;*.pst"), CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await LoadArchiveAsync(dialog.FileName);
    }
    private async Task<bool> ChooseOstAsync()
    {
        var dialog = new OpenFileDialog { Filter = L.T("Копия архива Outlook (*.ost;*.pst)|*.ost;*.pst"), CheckFileExists = true };
        return dialog.ShowDialog(this) == true && await LoadArchiveAsync(dialog.FileName);
    }
    private async void ChooseOst_Click(object sender, RoutedEventArgs e)
    { if (await ChooseOstAsync()) ModeTabs.SelectedIndex = 1; }
    private async void ShowService_Changed(object sender, RoutedEventArgs e)
    { if (!initializing && !busy && sourcePath is not null) await LoadArchiveAsync(sourcePath); }

    internal Task<bool> LoadForCheckAsync(string path) => LoadArchiveAsync(path);

    private async Task<bool> LoadArchiveAsync(string path)
    {
        SetBusy(true, L.T("Чтение папок и поиск сообщений…"));
        FileStream? guard = null;
        TreeViewItem? initial = null;
        bool success = false;
        try
        {
            guard = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var reader = new XstFile(path);
            bool showService = ShowServiceFolders.IsChecked == true;
            var outline = await Task.Run(() => ArchiveOutline.Read(reader, reader.ReadFolderTree(), showService));
            readGuard?.Dispose(); readGuard = guard; guard = null;
            archive = reader; sourcePath = path; currentFolder = null;
            Messages.ItemsSource = null; Search.Clear(); Folders.Items.Clear();
            foreach (var folder in outline.Folders) Folders.Items.Add(MakeFolder(folder));
            IEnumerable<TreeViewItem> Walk(TreeViewItem item)
            { yield return item; foreach (TreeViewItem child in item.Items) foreach (var descendant in Walk(child)) yield return descendant; }
            var items = Folders.Items.Cast<TreeViewItem>().SelectMany(Walk).ToList();
            initial = items.FirstOrDefault(i => i.Tag is Folder { ContentCount: > 0 } f && (f.Name.Equals("Inbox", StringComparison.OrdinalIgnoreCase) || f.Name.Equals("Входящие", StringComparison.OrdinalIgnoreCase)))
                ?? items.FirstOrDefault(i => i.Tag is Folder { ContentCount: > 0 }) ?? items.FirstOrDefault();
            // Expand ancestors without firing a second asynchronous folder read.
            if (initial is not null)
            {
                foreach (var item in items.Where(i => i.Items.Count > 0)) item.IsExpanded = true;
                suppressSelection = true; initial.IsSelected = true; suppressSelection = false;
            }
            FileLabel.Text = path; FileLabel.ToolTip = path;
            ConversionSource.Text = path;
            Status.Text = items.Count == 0 ? L.T("Папки с почтой не найдены. При необходимости включите «Показать служебные».") : L.T("Архив открыт только для чтения. Выберите письмо и нажмите «Читать письмо».");
            if (outline.Warnings.Count > 0) SetReport(DiagnosticLog.Write(new IOException(string.Join("\n", outline.Warnings)), L.T("Поиск папок с почтой")));
            success = true;
        }
        catch (Exception error) { ShowError(error, L.T("Не удалось открыть архив. Закройте Outlook и используйте локальную копию OST/PST.")); }
        finally { guard?.Dispose(); SetBusy(false); }
        if (initial?.Tag is Folder selected) await LoadFolderAsync(selected, initial);
        return success;
    }

    private static TreeViewItem MakeFolder(ArchiveFolder node)
    {
        var text = (string.IsNullOrWhiteSpace(node.Source.Name) ? L.T("Письма") : node.Source.Name) + $" ({node.Messages})" + (node.ReadWarning ? " ⚠" : "");
        var item = new TreeViewItem { Header = new TextBlock { Text = text }, ToolTip = node.Source.Path, Tag = node.Source, Margin = new Thickness(1) };
        foreach (var child in node.Children) item.Items.Add(MakeFolder(child));
        return item;
    }
    private async void Folder_Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
    { if (!busy && !suppressSelection && archive is not null && e.NewValue is TreeViewItem { Tag: Folder folder } item) await LoadFolderAsync(folder, item); }
    private async Task LoadFolderAsync(Folder folder, TreeViewItem item)
    {
        if (archive is null) return;
        SetBusy(true, L.T("Загрузка списка писем…"));
        try
        {
            currentFolder?.Messages.Clear(); currentFolder = folder;
            var messages = await Task.Run(() => archive.ReadMessages(folder));
            Messages.ItemsSource = messages; ApplyFilter();
            FolderCaption.Text = L.F($"{folder.Name} · Элементов: {messages.Count}. Двойной щелчок открывает письмо.");
            Status.Text = messages.Count == 0 ? L.T("Папка пуста. Выберите другую папку.") : L.F($"Найдено сообщений: {messages.Count}. Выберите письмо и нажмите «Читать письмо».");
            item.Header = new TextBlock { Text = $"{folder.Name} ({messages.Count})" };
            if (archive.LastReadWarnings.Count > 0)
                SetReport(DiagnosticLog.Write(new IOException(string.Join("\n", archive.LastReadWarnings.Select(w => w.Reason))), L.T("Чтение списка писем")));
        }
        catch (Exception error) { Messages.ItemsSource = null; ShowError(error, L.T("Не удалось прочитать папку.")); }
        finally { SetBusy(false); }
        if (Messages.Items.Count > 0) Messages.SelectedIndex = 0;
    }
    private void Message_Selected(object sender, SelectionChangedEventArgs e) => ReadMessageButton.IsEnabled = !busy && Messages.SelectedItem is Message;
    private async void ReadMessage_Click(object sender, RoutedEventArgs e) => await ReadSelectedAsync();
    private async void Messages_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject target && ItemsControl.ContainerFromElement(Messages, target) is DataGridRow)
            await ReadSelectedAsync();
    }
    private async void Messages_KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { e.Handled = true; await ReadSelectedAsync(); } }
    private async Task ReadSelectedAsync()
    {
        if (busy || archive is null || Messages.SelectedItem is not Message message) return;
        var reader = new MessageWindow(archive, message) { Owner = this };
        reader.ShowDialog();
        if (reader.LastDiagnosticPath is not null) SetReport(reader.LastDiagnosticPath);
        if (reader.ExportRequested) await BeginConversionAsync(true);
    }
    private int searchRevision;
    private async void Search_Changed(object sender, TextChangedEventArgs e)
    {
        int revision = ++searchRevision;
        await Task.Delay(300);
        if (revision == searchRevision && IsLoaded) ApplyFilter();
    }
    private void ApplyFilter()
    {
        if (Messages?.ItemsSource is null) return;
        var view = CollectionViewSource.GetDefaultView(Messages.ItemsSource);
        var query = Search.Text.Trim();
        view.Filter = value => value is Message message && (query.Length == 0 || (message.Subject ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
            || (message.From ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) || (message.To ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));
    }
    private async void Convert_Click(object sender, RoutedEventArgs e) => await BeginConversionAsync(false);
    private async Task BeginConversionAsync(bool selectedOnly)
    {
        if (busy) return;
        if (sourcePath is null)
            if (!await ChooseOstAsync()) return;
        if (sourcePath is null) return;
        var selected = Messages.SelectedItem as Message;
        if (selectedOnly && selected is null) return;
        var selection = selectedOnly ? MessageSelection.FromMessage(selected!) : null;
        bool includeService = IncludeServiceFolders.IsChecked == true;
        var wizard = new ExportWizard(sourcePath, includeService || selectedOnly, selection) { Owner = this };
        if (wizard.ShowDialog() != true) return;
        string target;
        if (wizard.Format == ExportFormat.Pst)
        {
            var dialog = new SaveFileDialog { Filter = L.T("Новый Outlook PST (*.pst)|*.pst"), DefaultExt = ".pst", AddExtension = true,
                FileName = Path.GetFileNameWithoutExtension(sourcePath) + "-converted.pst" };
            if (dialog.ShowDialog(this) != true) return;
            target = dialog.FileName;
            try { ArchiveUtilities.ValidateDestination(sourcePath, target); }
            catch (Exception error) { ShowError(error, L.T("Выберите новое имя PST.")); return; }
        }
        else
        {
            var dialog = new OpenFolderDialog { Title = L.T("Куда сохранить MSG? Внутри появится папка с именем архива и исходными папками писем"), Multiselect = false };
            if (dialog.ShowDialog(this) != true) return;
            target = ArchiveUtilities.NewMsgRoot(dialog.FolderName, sourcePath);
        }
        ModeTabs.SelectedIndex = 1;
        conversion = new CancellationTokenSource(); SetBusy(true, wizard.Format == ExportFormat.Msg ? L.T("Сохраняются выбранные MSG…") : L.T("Outlook сохраняет выбранные письма в PST. Не закрывайте Outlook до завершения.")); CancelButton.IsEnabled = true;
        TrayButton.IsEnabled = true;
        conversionEstimate = new(); conversionWatch.Restart();
        lastConversionProgress = new(0, 0, 0, 0, "", wizard.Selected.Distinct().Count());
        conversionTicker.Start(); UpdateConversionTiming();
        try
        {
            var progress = new Progress<ConversionProgress>(p =>
            {
                if (conversionEstimate is null) return;
                lastConversionProgress = p;
                Status.Text = L.F($"Обработано: {p.Examined}") + (p.Total is int total ? $" / {total}" : "")
                    + L.F($" · Сохранено и проверено: {p.Converted} · Пропущено: {p.Skipped} · Ошибок: {p.Failed}");
                UpdateConversionTiming();
            });
            var result = await OutlookConverter.ConvertAsync(sourcePath, target, progress, conversion.Token, includeServiceFolders: includeService, selections: wizard.Selected, format: wizard.Format);
            var report = result.Report; SetReport(result.ReportPath);
            if (report.OutputFormat == "PST" && File.Exists(report.Destination)) lastOutputPath = report.Destination;
            if (report.OutputFormat == "MSG" && Directory.Exists(report.Destination)) lastMsgDirectory = report.Destination;
            OpenOutlookButton.Visibility = report.OutputFormat == "PST" ? Visibility.Visible : Visibility.Collapsed;
            OpenResultFolderButton.Visibility = report.OutputFormat == "MSG" ? Visibility.Visible : Visibility.Collapsed;
            var heading = report.Cancelled ? L.T("Конвертация остановлена") : report.Completed ? L.T("Конвертация завершена") : L.T("Конвертация выполнена частично");
            ResultSummary.Text = L.F($"{heading}.\nПисем сохранено: {report.Converted}; содержимое проверено: {report.ContentVerified}. Ошибок: {report.Failed + report.FolderErrors}.\n")
                + (report.Skipped > 0 ? L.T("Пропущено элементов: ") + report.Skipped + ".\n" + PstInventory.SkipSummary(report) + "\n" : "")
                + (report.ExcludedServiceFolders.Count > 0 ? L.F($"Служебных веток исключено: {report.ExcludedServiceFolders.Count}. Их можно включить в дополнительных параметрах.\n") : "")
                + (report.MetadataWarnings > 0 ? L.F($"Предупреждений о необязательных полях: {report.MetadataWarnings} (подробности в отчёте).\n") : "")
                + (report.DatesVerified > 0 ? L.F($"Исходные даты проверены: {report.DatesVerified}.\n") : "")
                + (report.MessagesWithoutSourceDates > 0 ? L.F($"Без исходной даты: {report.MessagesWithoutSourceDates}. Дату нельзя восстановить из отсутствующих данных.\n") : "")
                + L.T("Результат: ") + report.Destination + "\n"
                + (report.FatalError is null ? "" : "\n" + report.FatalError);
            Status.Text = (report.Completed ? "100 % · " : "") + heading + L.T(" · Прошло ") + ConversionEstimateValue.Duration(conversionWatch.Elapsed) + ". " + (report.Converted > 0 ? L.T("Откройте результат кнопкой в шаге 3.") : L.T("Проверьте причины в отчёте."));
            tray?.Finish(heading, L.F($"Сохранено: {report.Converted} · Ошибок: {report.Failed + report.FolderErrors}\nПрошло ") + ConversionEstimateValue.Duration(conversionWatch.Elapsed), report.Completed);
            if (!report.Completed && report.FatalError is not null && tray?.Hidden != true) MessageBox.Show(this, report.FatalError + L.T("\n\nПодробности доступны в отчёте. Исходный архив не изменён."), L.T("Проверка результата"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception error) { ShowError(error, L.T("Не удалось выполнить перенос.")); }
        finally { conversionTicker.Stop(); conversionWatch.Stop(); conversionEstimate = null; conversion.Dispose(); conversion = null; CancelButton.IsEnabled = false; TrayButton.IsEnabled = false; SetBusy(false); }
    }
    private void UpdateConversionTiming()
    {
        if (conversionEstimate is null || lastConversionProgress is not { } p) return;
        var estimate = conversionEstimate.Observe(p.Examined, p.Total, conversionWatch.Elapsed, p.Finalizing);
        RenderConversionEstimate(estimate);
        tray?.Update(estimate, Status.Text);
    }
    private void RenderConversionEstimate(ConversionEstimateValue estimate)
    {
        Progress.IsIndeterminate = estimate.Percent is null;
        Progress.Value = estimate.Percent ?? 0;
        ConversionTiming.Text = estimate.Label;
        TaskbarItemInfo.ProgressState = estimate.Percent is null ? System.Windows.Shell.TaskbarItemProgressState.Indeterminate : System.Windows.Shell.TaskbarItemProgressState.Normal;
        TaskbarItemInfo.ProgressValue = (estimate.Percent ?? 0) / 100;
    }
    internal void ShowProgressForCheck(ConversionEstimateValue estimate)
    {
        ProgressRow.Visibility = Visibility.Visible;
        RenderConversionEstimate(estimate);
    }
    private async void OpenOutlook_Click(object sender, RoutedEventArgs e)
    {
        var path = lastOutputPath;
        if (path is null || !File.Exists(path))
        {
            var dialog = new OpenFileDialog { Filter = "Outlook PST (*.pst)|*.pst", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return; path = dialog.FileName;
        }
        if (sourcePath is not null && string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            Messages.ItemsSource = null; Folders.Items.Clear(); archive = null; currentFolder = null; sourcePath = null;
            readGuard?.Dispose(); readGuard = null; FileLabel.Text = L.T("PST передан в Outlook: ") + path;
        }
        SetBusy(true, L.T("Открытие PST как отдельного архива Outlook…"));
        try
        {
            var opened = await OutlookPst.OpenAsync(path);
            ResultSummary.Text = L.F($"Архив открыт в Outlook. Элементов: {opened.Messages}.\nПапка: {opened.FolderPath}\nМастер импорта не требуется.");
            Status.Text = L.T("PST открыт в Outlook как отдельный архив.");
            if (opened.Messages == 0) MessageBox.Show(this, L.T("В выбранном PST Outlook не нашёл элементов. Выберите файл результата, указанный в отчёте."), L.T("PST пуст"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error) { ShowError(error, L.T("Не удалось показать PST в Outlook.")); }
        finally { SetBusy(false); }
    }
    private void OpenResultFolder_Click(object sender, RoutedEventArgs e)
    {
        if (lastMsgDirectory is null || !Directory.Exists(lastMsgDirectory)) return;
        try { var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = false }; info.ArgumentList.Add(lastMsgDirectory); Process.Start(info); }
        catch (Exception error) { ShowError(error, L.T("Не удалось открыть папку MSG.")); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e)
    { conversion?.Cancel(); CancelButton.IsEnabled = false; Status.Text = L.T("Остановка после текущего письма и его проверки…"); }
    private void SetBusy(bool value, string? status = null)
    {
        busy = value;
        if (!value) TaskbarItemInfo.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
        OpenButton.IsEnabled = Folders.IsEnabled = Messages.IsEnabled = Search.IsEnabled = ShowServiceFolders.IsEnabled = ChooseOstButton.IsEnabled = ConvertButton.IsEnabled = OpenOutlookButton.IsEnabled = IncludeServiceFolders.IsEnabled = !value;
        ExportButton.IsEnabled = OpenResultFolderButton.IsEnabled = !value;
        ReadMessageButton.IsEnabled = !value && Messages.SelectedItem is Message;
        ReportButton.IsEnabled = !value && lastReportPath is not null;
        ProgressRow.Visibility = value ? Visibility.Visible : Visibility.Collapsed; Progress.IsIndeterminate = value;
        if (conversionEstimate is null) ConversionTiming.Text = "";
        if (status is not null) Status.Text = status;
    }
    private void ShowError(Exception error, string context)
    {
        if (tray?.Hidden == true) tray.Restore();
        SetReport(DiagnosticLog.Write(error, context)); Status.Text = context + " " + error.Message;
        MessageBox.Show(this, Status.Text, "Mail Archive", MessageBoxButton.OK, MessageBoxImage.Error);
    }
    private void SetReport(string? path) { if (path is null) return; lastReportPath = path; ReportButton.IsEnabled = !busy; }
    private void Report_Click(object sender, RoutedEventArgs e)
    {
        if (lastReportPath is null) return;
        try { var info = new ProcessStartInfo("notepad.exe") { UseShellExecute = false }; info.ArgumentList.Add(lastReportPath); Process.Start(info); }
        catch (Exception error) { MessageBox.Show(this, L.T("Отчёт сохранён: ") + lastReportPath + "\n" + error.Message); }
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (busy) { e.Cancel = true; if (conversion is not null) HideInTray(); return; }
        readGuard?.Dispose();
    }
    private void Tray_Click(object sender, RoutedEventArgs e) => HideInTray();
    private void HideInTray()
    {
        try
        {
            tray ??= new ConversionTray(this);
            UpdateConversionTiming();
            if (!tray.HideToTray()) { Show(); WindowState = WindowState.Normal; Status.Text = L.T("Трей Windows недоступен. Окно оставлено открытым."); }
        }
        catch (Exception error) { Show(); WindowState = WindowState.Normal; ShowError(error, L.T("Не удалось свернуть в трей.")); }
    }
}
