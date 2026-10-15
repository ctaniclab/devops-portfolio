using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using Microsoft.Win32;
using XstReader;
using Message = XstReader.Message;

namespace MailArchive;

public partial class MainWindow : Window
{
    private XstFile? archive;
    private Folder? currentFolder;
    private Message? currentMessage;
    private string? sourcePath;
    private FileStream? readGuard;
    private bool busy;
    private CancellationTokenSource? conversion;
    private string? lastReportPath;
    private string? lastOutputPath;
    private readonly MailPreview mailPreview;

    public MainWindow() { InitializeComponent(); mailPreview = new MailPreview(HtmlPreview); }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Архивы Outlook (*.ost;*.pst)|*.ost;*.pst", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        SetBusy(true, "Чтение структуры папок…");
        FileStream? guard = null;
        TreeViewItem? initialFolder = null;
        try
        {
            guard = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            var reader = new XstFile(dialog.FileName);
            var root = await Task.Run(reader.ReadFolderTree);
            readGuard?.Dispose();
            readGuard = guard; guard = null;
            archive = reader; sourcePath = dialog.FileName;
            currentFolder = null; currentMessage = null;
            Messages.ItemsSource = null; ResetMessage();
            Search.Clear();
            Folders.Items.Clear();
            var item = MakeFolder(root); item.IsExpanded = true;
            Folders.Items.Add(item);
            var preferred = ExportFolderLayout.InitialFolder(root);
            initialFolder = FindAndExpand(item, f => ReferenceEquals(f, preferred)) ?? item;
            FileLabel.Text = sourcePath;
            Status.Text = "Архив открыт только для чтения. Выберите папку. Поиск работает в выбранной папке.";
        }
        catch (Exception error) { ShowError(error, "Не удалось открыть архив. Закройте Outlook и используйте отдельную локальную копию OST/PST."); }
        finally { guard?.Dispose(); SetBusy(false); }
        // Selecting while busy would discard the SelectedItemChanged event.
        if (initialFolder is not null) initialFolder.IsSelected = true;
    }

    private static TreeViewItem MakeFolder(Folder folder)
    {
        var name = string.IsNullOrWhiteSpace(folder.Name) ? "Корень" : folder.Name;
        var item = new TreeViewItem { Header = folder.ContentCount > 0 ? $"{name} ({folder.ContentCount})" : name, Tag = folder, Margin = new Thickness(2) };
        foreach (var child in folder.Folders) item.Items.Add(MakeFolder(child));
        return item;
    }

    private static TreeViewItem? FindAndExpand(TreeViewItem item, Func<Folder, bool> predicate)
    {
        if (item.Tag is Folder folder && predicate(folder)) return item;
        foreach (TreeViewItem child in item.Items)
        {
            var result = FindAndExpand(child, predicate);
            if (result is null) continue;
            item.IsExpanded = true;
            return result;
        }
        return null;
    }

    private async void Folder_Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (busy || archive is null || e.NewValue is not TreeViewItem { Tag: Folder folder } item) return;
        SetBusy(true, "Загрузка писем…");
        try
        {
            if (currentFolder is not null) currentFolder.Messages.Clear();
            currentFolder = folder; ResetMessage();
            var headers = await Task.Run(() => archive.ReadMessages(folder));
            Messages.ItemsSource = headers;
            ApplyFilter();
            Messages.SelectedItem = null;
            item.Header = $"{(string.IsNullOrWhiteSpace(folder.Name) ? "Корень" : folder.Name)} ({headers.Count})";
            Status.Text = $"{folder.Path} · Писем: {headers.Count}";
            if (archive.LastReadWarnings.Count > 0)
            {
                var skipped = archive.LastReadWarnings.Count(w => w.MessageSkipped);
                Status.Text += $" · Чтение восстановлено по индексу. Непрочитанных заголовков: {skipped}.";
                var details = string.Join("\n\n", archive.LastReadWarnings.Select(w => w.NodeId + " " + w.Reason + "\n" + w.Details));
                SetReport(DiagnosticLog.Write(new IOException(details), "Чтение папки " + folder.Path));
            }
        }
        catch (Exception error) { Messages.ItemsSource = null; ShowError(error, "Не удалось прочитать папку."); }
        finally { SetBusy(false); }
        if (Messages.Items.Count > 0) Messages.SelectedIndex = 0;
    }

    private async void Message_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (busy || archive is null) return;
        if (Messages.SelectedItem is not Message message) { ResetMessage(); return; }
        SetBusy(true, "Чтение письма…");
        try
        {
            ResetMessage();
            await Task.Run(() => archive.ReadMessageDetails(message));
            currentMessage = message;
            string Recipients(RecipientType type) => string.Join("; ", message.Recipients.Where(r => r.RecipientType == type)
                .Select(r => string.IsNullOrEmpty(r.EmailAddress) ? r.DisplayName : $"{r.DisplayName} <{r.EmailAddress}>"));
            var senderAddress = message.Properties.FirstOrDefault(p => (ushort)p.Tag == 0x0C1F)?.Value as string;
            MessageHeader.Text = $"{message.Subject}\nОт: {message.From} {senderAddress}\n"
                + $"Кому: {Recipients(RecipientType.To)}\nКопия: {Recipients(RecipientType.Cc)}\n"
                + $"Скрытая копия: {Recipients(RecipientType.Bcc)}\nПолучено: {message.Received:g}    Отправлено: {message.Submitted:g}";
            var body = await Task.Run(() => PreparedBody.FromMessage(message));
            BodyText.Text = body.Text;
            HtmlText.Text = body.Html;
            if (string.IsNullOrEmpty(BodyText.Text)) BodyText.Text = "Текстовое содержимое отсутствует или недоступно в локальном архиве.";
            MessageProperties.ItemsSource = message.Properties;
            Attachments.ItemsSource = message.Attachments;
            if (message.Attachments.Count > 0) Attachments.SelectedIndex = 0;
            Status.Text = $"Письмо открыто. Вложений: {message.Attachments.Count}. Вложенные письма и OLE не открываются автоматически.";
            try
            {
                int imageBytes = 0;
                var preview = await Task.Run(() => PreviewHtml.Create(body.Html, cid =>
                {
                    var attachment = message.Attachments.FirstOrDefault(a => a.IsFile && string.Equals(a.ContentId?.Trim('<', '>'), cid, StringComparison.OrdinalIgnoreCase));
                    if (attachment is null || attachment.Size > 8 * 1024 * 1024 || imageBytes >= 24 * 1024 * 1024) return null;
                    try
                    {
                        using var stream = new BoundedImageStream(8 * 1024 * 1024);
                        archive.SaveAttachment(stream, attachment);
                        imageBytes += (int)stream.Length;
                        return stream.ToArray();
                    }
                    catch (Exception error) when (error is IOException or XstException) { return null; }
                }));
                await mailPreview.ShowAsync(preview).WaitAsync(TimeSpan.FromSeconds(25));
                HtmlPreview.Visibility = Visibility.Visible;
                PreviewHint.Visibility = Visibility.Collapsed;
            }
            catch (Exception error)
            {
                HtmlPreview.Visibility = Visibility.Collapsed;
                PreviewHint.Visibility = Visibility.Visible;
                PreviewHint.Text = "Подробный HTML-просмотр недоступен. Текст письма доступен на вкладке «Текст письма». Для HTML-просмотра нужен Microsoft Edge WebView2 Runtime.";
                SetReport(DiagnosticLog.Write(error, "Инициализация подробного просмотра письма"));
                BodyTabs.SelectedIndex = 1;
            }
        }
        catch (Exception error) { ResetMessage(); ShowError(error, "Не удалось прочитать письмо."); }
        finally { SetBusy(false); }
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void ApplyFilter()
    {
        if (Messages?.ItemsSource is null) return;
        var view = CollectionViewSource.GetDefaultView(Messages.ItemsSource);
        var query = Search.Text.Trim();
        view.Filter = value => value is Message m && (query.Length == 0
            || (m.Subject ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
            || (m.From ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
            || (m.To ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void Attachment_Selected(object sender, SelectionChangedEventArgs e) =>
        SaveAttachmentButton.IsEnabled = !busy && Attachments.SelectedItem is Attachment { IsFile: true };

    private async void SaveAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (archive is null || Attachments.SelectedItem is not Attachment { IsFile: true } attachment) return;
        var dialog = new SaveFileDialog { FileName = ArchiveUtilities.SafeAttachmentName(attachment.FileName), OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        SetBusy(true, "Сохранение вложения…");
        try
        {
            // The final file only appears after successful extraction. Do not truncate a pre-existing file on failure.
            var temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await Task.Run(() => archive.SaveAttachment(temporary, null, attachment));
                File.Move(temporary, dialog.FileName, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Status.Text = "Вложение сохранено: " + dialog.FileName;
        }
        catch (Exception error) { ShowError(error, "Не удалось сохранить вложение."); }
        finally { SetBusy(false); }
    }

    private async void Convert_Click(object sender, RoutedEventArgs e) => await BeginConversionAsync(false);
    private async void ConvertSelected_Click(object sender, RoutedEventArgs e) => await BeginConversionAsync(true);

    private async Task BeginConversionAsync(bool selectedOnly)
    {
        if (sourcePath is null) return;
        var selected = Messages.SelectedItem as Message;
        if (selectedOnly && selected is null) return;
        var selection = selectedOnly ? MessageSelection.FromMessage(selected!) : null;
        var dialog = new SaveFileDialog { Filter = "Outlook PST (*.pst)|*.pst", DefaultExt = ".pst", AddExtension = true,
            FileName = selectedOnly ? Path.GetFileNameWithoutExtension(ArchiveUtilities.SafeAttachmentName(selected!.Subject)) + ".pst"
                : Path.GetFileNameWithoutExtension(sourcePath) + "-converted.pst" };
        if (dialog.ShowDialog(this) != true) return;
        try { ArchiveUtilities.ValidateDestination(sourcePath, dialog.FileName); }
        catch (Exception error) { ShowError(error, "Выберите другой выходной файл."); return; }
        if (MessageBox.Show(this,
            (selectedOnly ? "Будет перенесено только выбранное письмо с его файловыми вложениями в новый PST.\n\n"
                : "Будут восстановлены письма, папки и обычные файловые вложения в новом PST через классический Outlook.\n\n")
            + "Это не полная копия MAPI: контакты, календарь, задачи, S/MIME, вложенные письма и OLE не переносятся. Все пропуски и ошибки попадут в JSON-отчёт.\n\n"
            + "Используйте отдельную локальную копию OST. Исходный файл останется без изменений. Outlook может запросить разрешение на доступ.\n\nПродолжить?",
            "Перенос писем OST → PST", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        conversion = new CancellationTokenSource();
        SetBusy(true, "Запуск Outlook и создание PST…");
        CancelButton.IsEnabled = true;
        try
        {
            var progress = new Progress<ConversionProgress>(p => Status.Text =
                $"{p.Folder} · Обработано: {p.Examined} · Перенесено: {p.Converted} · Пропущено: {p.Skipped} · Ошибок: {p.Failed}");
            var result = await OutlookConverter.ConvertAsync(sourcePath, dialog.FileName, progress, conversion.Token, selection);
            var r = result.Report;
            SetReport(result.ReportPath);
            if (File.Exists(r.Destination)) lastOutputPath = r.Destination;
            var heading = r.Cancelled ? "Перенос остановлен" : !r.Completed ? "Перенос выполнен частично"
                : r.Skipped + r.Failed + r.MetadataWarnings > 0 ? "Перенос завершён с предупреждениями" : "Перенос писем завершён";
            var text = $"{heading}.\nПеренесено писем: {r.Converted}; пропущено элементов: {r.Skipped}; ошибок писем: {r.Failed}.\n"
                + (r.Skipped == 0 ? "" : "\nПричины пропуска:\n" + PstInventory.SkipSummary(r) + "\n\n")
                + $"Ошибок папок: {r.FolderErrors}; предупреждений метаданных: {r.MetadataWarnings}.\n\n"
                + (r.OutlookReadBackVerified ? $"Outlook подтвердил количество сохранённых элементов: {r.OutputMessagesVerified}.\n" : "")
                + (r.OutputFolders.Count == 0 ? "" : "Первая папка с письмами: " + r.OutputFolders[0].Path + "\nНажмите «Открыть PST в Outlook…», чтобы перейти к письмам.\n\n")
                + $"RTF-писем переведено в HTML: {r.RtfBodiesConvertedToHtml}.\n\n"
                + (r.Completed ? "PST: " + r.Destination + "\n" : "")
                + (r.RetainedPartialFile is null ? "" : "Неполный PST: " + r.RetainedPartialFile + "\n")
                + "Отчёт: " + result.ReportPath + (r.FatalError is null ? "" : "\n\n" + r.FatalError);
            Status.Text = $"{heading}. Писем: {r.Converted}; пропущено элементов: {r.Skipped}; ошибок: {r.Failed + r.FolderErrors}. "
                + (r.Converted > 0 ? "Результат доступен через «Открыть PST в Outlook…»." : "Причины — в отчёте.");
            MessageBox.Show(this, text, "Результат", MessageBoxButton.OK,
                r.Completed && r.Skipped + r.Failed + r.MetadataWarnings == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception error) { ShowError(error, "Не удалось выполнить перенос. Проверьте установку классического Outlook и права на выходную папку."); }
        finally { conversion.Dispose(); conversion = null; CancelButton.IsEnabled = false; SetBusy(false); }
    }

    private async void OpenOutlook_Click(object sender, RoutedEventArgs e)
    {
        var path = lastOutputPath;
        if (path is null || !File.Exists(path))
        {
            var dialog = new OpenFileDialog { Filter = "Outlook PST (*.pst)|*.pst", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            path = dialog.FileName;
        }
        // Outlook needs a writable store. Release our read-only guard if the user
        // explicitly opens the same PST in Outlook; avoid a sharing violation with its writable store.
        if (sourcePath is not null && string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            ResetMessage(); Messages.ItemsSource = null; Folders.Items.Clear();
            archive = null; currentFolder = null; sourcePath = null;
            readGuard?.Dispose(); readGuard = null;
            FileLabel.Text = "PST передан в Outlook: " + path;
        }
        SetBusy(true, "Открытие папки с письмами в Outlook…");
        try
        {
            var opened = await OutlookPst.OpenAsync(path);
            Status.Text = $"PST открыт в Outlook. Элементов: {opened.Messages}. Показана папка: {opened.FolderPath}";
            if (opened.Messages == 0)
                MessageBox.Show(this, "Outlook не нашёл элементов в этом PST. Проверьте, что выбран файл результата, указанный в отчёте.", "PST пуст", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error) { ShowError(error, "Не удалось показать PST в Outlook."); }
        finally { SetBusy(false); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        conversion?.Cancel(); CancelButton.IsEnabled = false;
        Status.Text = "Остановка после текущего письма. Если Outlook открыл диалог, закройте его. Неполный PST будет сохранён отдельно.";
    }

    private void SetBusy(bool value, string? status = null)
    {
        busy = value;
        OpenButton.IsEnabled = Folders.IsEnabled = Messages.IsEnabled = Search.IsEnabled = !value;
        ConvertButton.IsEnabled = !value && sourcePath is not null && Path.GetExtension(sourcePath).Equals(".ost", StringComparison.OrdinalIgnoreCase);
        ConvertSelectedButton.IsEnabled = !value && sourcePath is not null && Messages.SelectedItem is Message;
        SaveAttachmentButton.IsEnabled = !value && Attachments.SelectedItem is Attachment { IsFile: true };
        ReportButton.IsEnabled = !value && lastReportPath is not null;
        OpenOutlookButton.IsEnabled = !value;
        Progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        Progress.IsIndeterminate = value;
        if (status is not null) Status.Text = status;
    }

    private void ResetMessage()
    {
        Attachments.ItemsSource = null;
        if (currentMessage is not null) { currentMessage.ClearContents(); currentMessage.Properties.Clear(); currentMessage.Recipients.Clear(); }
        currentMessage = null; MessageHeader.Text = "Выберите письмо";
        BodyText.Clear(); HtmlText.Clear(); SaveAttachmentButton.IsEnabled = false;
        ConvertSelectedButton.IsEnabled = false;
        MessageProperties.ItemsSource = null;
        mailPreview.Clear();
        HtmlPreview.Visibility = Visibility.Collapsed;
        PreviewHint.Text = "Выберите письмо для подробного просмотра.";
        PreviewHint.Visibility = Visibility.Visible;
    }

    private void ShowError(Exception error, string context)
    {
        SetReport(DiagnosticLog.Write(error, context));
        Status.Text = context + " " + error.Message;
        MessageBox.Show(this, Status.Text, "Mail Archive", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void SetReport(string? path)
    {
        if (path is null) return;
        lastReportPath = path;
        ReportButton.IsEnabled = !busy;
    }

    private void Report_Click(object sender, RoutedEventArgs e)
    {
        if (lastReportPath is null) return;
        try
        {
            var info = new ProcessStartInfo("notepad.exe") { UseShellExecute = false };
            info.ArgumentList.Add(lastReportPath);
            Process.Start(info);
        }
        catch (Exception error) { MessageBox.Show(this, "Отчёт сохранён: " + lastReportPath + "\n" + error.Message); }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (busy)
        {
            e.Cancel = true;
            if (conversion is not null) Cancel_Click(this, new RoutedEventArgs());
            return;
        }
        readGuard?.Dispose();
        mailPreview.Dispose();
    }

    private sealed class BoundedImageStream(int limit) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        { if (Position + count > limit) throw new IOException("Вложенное изображение слишком большое для предпросмотра."); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer)
        { if (Position + buffer.Length > limit) throw new IOException("Вложенное изображение слишком большое для предпросмотра."); base.Write(buffer); }
    }
}
