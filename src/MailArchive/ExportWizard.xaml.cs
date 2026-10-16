using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using XstReader;

namespace MailArchive;

public sealed class MessageChoice : INotifyPropertyChanged
{
    public required MessageSelection Id { get; init; }
    public string Subject { get; init; } = "";
    public string Sender { get; init; } = "";
    public string Folder { get; init; } = "";
    private bool chosen;
    public bool Chosen { get => chosen; set { if (chosen != value) { chosen = value; PropertyChanged?.Invoke(this, new(nameof(Chosen))); } } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class ExportWizard : Window
{
    private readonly string source;
    private readonly bool includeService;
    private readonly MessageSelection? preselected;
    private List<MessageChoice> choices = [];
    private bool loading = true;
    private bool batch;
    private bool countPending;
    private bool formatStep;
    private readonly CancellationTokenSource loadCancellation = new();
    private int searchRevision;
    public IReadOnlyList<MessageSelection> Selected { get; private set; } = [];
    public ExportFormat Format { get; private set; }
    public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ExportWizard(string source, bool includeService, MessageSelection? preselected = null)
    {
        this.source = source; this.includeService = includeService; this.preselected = preselected;
        InitializeComponent(); WindowTheme.Attach(this); Loaded += Wizard_Loaded;
    }
    private async void Wizard_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            choices = await Task.Run(() =>
            {
                var archive = new XstFile(source);
                var outline = ArchiveOutline.Read(archive, archive.ReadFolderTree(), includeService, loadCancellation.Token);
                IEnumerable<ArchiveFolder> Walk(ArchiveFolder node)
                { yield return node; foreach (var child in node.Children) foreach (var descendant in Walk(child)) yield return descendant; }
                var list = new List<MessageChoice>();
                foreach (var node in outline.Folders.SelectMany(Walk))
                {
                    loadCancellation.Token.ThrowIfCancellationRequested();
                    foreach (var message in archive.ReadMessages(node.Source))
                    {
                        loadCancellation.Token.ThrowIfCancellationRequested();
                        var id = MessageSelection.FromMessage(message);
                        list.Add(new() { Id = id, Subject = string.IsNullOrWhiteSpace(message.Subject) ? "Без темы" : message.Subject,
                            Sender = message.From ?? "", Folder = node.Source.Path, Chosen = preselected is not null && preselected == id });
                    }
                    node.Source.Messages.Clear();
                }
                if (outline.Warnings.Count > 0) throw new IOException("Не все папки удалось прочитать. Проверьте журнал чтения архива перед выборочной конвертацией.");
                return list;
            });
            loadCancellation.Token.ThrowIfCancellationRequested();
            foreach (var choice in choices) choice.PropertyChanged += Choice_Changed;
            MessageChoices.ItemsSource = choices; AllButton.IsEnabled = NoneButton.IsEnabled = FoundButton.IsEnabled = true;
            StepHint.Text = "Отметьте нужные письма галочками или нажмите «Выбрать все». Затем выберите PST или MSG.";
            UpdateCount(); Ready.TrySetResult();
        }
        catch (OperationCanceledException) { Ready.TrySetCanceled(); }
        catch (Exception error) { StepHint.Text = "Не удалось подготовить список: " + error.Message; Ready.TrySetException(error); }
        finally { loading = false; }
    }
    private void Choice_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (batch || countPending) return;
        countPending = true;
        Dispatcher.BeginInvoke(new Action(() => { countPending = false; UpdateCount(); }));
    }
    private void UpdateCount()
    { int count = choices.Count(c => c.Chosen); CountText.Text = $"Выбрано: {count} из {choices.Count}"; NextButton.IsEnabled = count > 0; }
    private void SetChoices(IEnumerable<MessageChoice> rows, bool value)
    { batch = true; foreach (var choice in rows) choice.Chosen = value; batch = false; UpdateCount(); }
    private void All_Click(object sender, RoutedEventArgs e) => SetChoices(choices, true);
    private void None_Click(object sender, RoutedEventArgs e) => SetChoices(choices, false);
    private void Found_Click(object sender, RoutedEventArgs e) => SetChoices(MessageChoices.Items.Cast<MessageChoice>(), true);
    private async void Search_Changed(object sender, TextChangedEventArgs e)
    {
        int revision = ++searchRevision;
        await Task.Delay(300);
        if (revision != searchRevision || loadCancellation.IsCancellationRequested) return;
        if (MessageChoices?.ItemsSource is null) return;
        var query = MessageSearch.Text.Trim();
        CollectionViewSource.GetDefaultView(MessageChoices.ItemsSource).Filter = item => item is MessageChoice choice
            && (query.Length == 0 || choice.Subject.Contains(query, StringComparison.OrdinalIgnoreCase) || choice.Sender.Contains(query, StringComparison.OrdinalIgnoreCase) || choice.Folder.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        if (!formatStep)
        {
            Selected = choices.Where(c => c.Chosen).Select(c => c.Id).Distinct().ToArray();
            if (Selected.Count == 0) return;
            formatStep = true; SelectionStep.Visibility = Visibility.Collapsed; FormatStep.Visibility = Visibility.Visible;
            StepTitle.Text = "2. В какой формат сохранить?"; StepHint.Text = $"Выбрано сообщений: {Selected.Count}. После выбора формата укажите место сохранения.";
            BackButton.Visibility = Visibility.Visible; NextButton.Content = "Выбрать место и конвертировать…";
        }
        else { Format = MsgChoice.IsChecked == true ? ExportFormat.Msg : ExportFormat.Pst; DialogResult = true; }
    }
    private void Back_Click(object sender, RoutedEventArgs e)
    {
        formatStep = false; SelectionStep.Visibility = Visibility.Visible; FormatStep.Visibility = Visibility.Collapsed;
        StepTitle.Text = "1. Какие сообщения конвертировать?"; StepHint.Text = "Выбор сохранён. Можно отметить другие сообщения или выбрать все.";
        BackButton.Visibility = Visibility.Collapsed; NextButton.Content = "Далее: выбрать формат";
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { loadCancellation.Cancel(); DialogResult = false; }
    private void Wizard_Closing(object? sender, CancelEventArgs e) { loadCancellation.Cancel(); searchRevision++; }
}
