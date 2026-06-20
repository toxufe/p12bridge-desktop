namespace P12Bridge.Core;

public static class LocalAssetSelection
{
    public static LocalAssetItem? FindByPath(
        IReadOnlyList<LocalAssetItem> items,
        string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return null;
        }

        var fullSelectedPath = Path.GetFullPath(selectedPath);
        return items.FirstOrDefault(item =>
            Path.GetFullPath(item.Path).Equals(fullSelectedPath, StringComparison.OrdinalIgnoreCase));
    }
}
