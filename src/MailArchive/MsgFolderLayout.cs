using XstReader;

namespace MailArchive;

// Plan names before exporting: sanitization/duplicate names must never merge source folders.
// No directories are created until a selected, supported message needs them.
public sealed class MsgFolderLayout
{
    private readonly Dictionary<Folder, string> relativePaths = [];

    public MsgFolderLayout(Folder root)
    {
        var names = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        void Walk(Folder folder, string relative, int depth)
        {
            relativePaths.Add(folder, relative);
            if (!names.TryGetValue(relative, out var used)) names[relative] = used = new(StringComparer.OrdinalIgnoreCase);
            foreach (var child in folder.Folders)
            {
                var path = ExportFolderLayout.IsWrapper(child, depth) ? relative
                    : Path.Combine(relative, ExportFolderLayout.UniqueName(ArchiveUtilities.SafeDirectoryName(child.Name), used));
                Walk(child, path, depth + 1);
            }
        }
        Walk(root, "", 0);
    }

    public string PathFor(string outputRoot, Folder source)
    {
        if (!relativePaths.TryGetValue(source, out var relative)) throw new IOException("Исходная папка не найдена в структуре архива.");
        return Path.Combine(outputRoot, relative);
    }

    public string EnsureDirectory(string outputRoot, Folder source)
    {
        var path = PathFor(outputRoot, source);
        Directory.CreateDirectory(path);
        return path;
    }
}
