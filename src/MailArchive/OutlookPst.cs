using System.Runtime.InteropServices;

namespace MailArchive;

public sealed record OpenedPst(string FolderPath, int Messages);

internal sealed class ComScope : IDisposable
{
    private readonly List<object> values = [];
    public dynamic Keep(object value) { values.Add(value); return value; }
    public void Dispose()
    {
        for (int i = values.Count - 1; i >= 0; i--)
        {
            try { if (Marshal.IsComObject(values[i])) Marshal.ReleaseComObject(values[i]); }
            catch (InvalidComObjectException) { }
        }
        values.Clear();
    }
}

public static class OutlookPst
{
    public static List<PstFolderInfo> ReadFolderCounts(object root)
    {
        var result = new List<PstFolderInfo>();
        void Visit(object value)
        {
            using var scope = new ComScope();
            dynamic folder = value;
            dynamic items = scope.Keep(folder.Items);
            int count = items.Count;
            if (count > 0) result.Add(new((string)folder.FolderPath, count));
            dynamic children = scope.Keep(folder.Folders);
            for (int i = 1; i <= (int)children.Count; i++)
            {
                using var childScope = new ComScope();
                Visit((object)childScope.Keep(children[i]));
            }
        }
        Visit(root);
        return result;
    }

    public static Task<OpenedPst> OpenAsync(string path)
    {
        var completion = new TaskCompletionSource<OpenedPst>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(Open(path)); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "Open converted PST in Outlook" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static OpenedPst Open(string path)
    {
        path = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(path), ".pst", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new IOException(L.T("Выберите существующий PST-файл."));
        using var scope = new ComScope();
        var type = Type.GetTypeFromProgID("Outlook.Application")
            ?? throw new InvalidOperationException(L.T("Для открытия PST нужен классический Outlook."));
        dynamic app = scope.Keep(Activator.CreateInstance(type)!);
        dynamic session = scope.Keep(app.GetNamespace("MAPI"));
        object? FindRoot()
        {
            dynamic stores = scope.Keep(session.Stores);
            for (int i = 1; i <= (int)stores.Count; i++)
            {
                using var storeScope = new ComScope();
                dynamic store = storeScope.Keep(stores[i]);
                if (string.Equals((string)store.FilePath, path, StringComparison.OrdinalIgnoreCase))
                    return (object)scope.Keep(store.GetRootFolder());
            }
            return null;
        }
        var root = FindRoot();
        if (root is null) { session.AddStoreEx(path, 2); root = FindRoot(); }
        if (root is null) throw new IOException(L.T("Outlook не подключил выбранный PST."));

        var folders = ReadFolderCounts(root);
        var first = folders.OrderByDescending(f => new[] { "Inbox", "Входящие" }.Contains(f.Path.Split('\\').Last(), StringComparer.OrdinalIgnoreCase))
            .ThenByDescending(f => f.Messages).FirstOrDefault();
        object FindFolder(object parent)
        {
            dynamic folder = parent;
            if (first is null || (string)folder.FolderPath == first.Path) return parent;
            dynamic children = scope.Keep(folder.Folders);
            for (int i = 1; i <= (int)children.Count; i++)
            {
                dynamic child = scope.Keep(children[i]);
                var childPath = (string)child.FolderPath;
                if (first.Path == childPath || first.Path.StartsWith(childPath + "\\", StringComparison.OrdinalIgnoreCase))
                    return FindFolder((object)child);
            }
            throw new IOException(L.T("Outlook не нашёл папку с сохранёнными письмами."));
        }
        dynamic chosen = FindFolder(root);
        dynamic explorer = scope.Keep(chosen.GetExplorer(0));
        explorer.Display();
        explorer.Activate();
        // Keep the store attached: the user asked to open it in Outlook.
        return new((string)chosen.FolderPath, folders.Sum(f => f.Messages));
    }
}
