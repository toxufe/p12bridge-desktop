using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class LocalAssetLibraryService : ILocalAssetLibraryService
{
    private const string CertificateMetadataFileName = "p12bridge.project.json";

    public LocalAssetLibraryResult Scan(LocalAssetLibraryRequest request)
    {
        var items = new List<LocalAssetItem>();
        var issues = new List<ValidationIssue>();

        AddCertificateProjects(request.CertificateDirectory, items, issues);
        AddFiles(request.ProfileDirectory, "*.mobileprovision", LocalAssetType.ProvisioningProfile, items, issues);
        AddFiles(request.IpaDirectory, "*.ipa", LocalAssetType.Ipa, items, issues);

        return issues.Count == 0
            ? LocalAssetLibraryResult.Success(SortItems(items))
            : LocalAssetLibraryResult.Partial(SortItems(items), issues.ToArray());
    }

    private static void AddCertificateProjects(
        string rootDirectory,
        List<LocalAssetItem> items,
        List<ValidationIssue> issues)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return;
        }

        try
        {
            foreach (var metadataPath in Directory.EnumerateFiles(
                rootDirectory,
                CertificateMetadataFileName,
                SearchOption.AllDirectories))
            {
                var projectDirectory = Path.GetDirectoryName(metadataPath);
                if (string.IsNullOrWhiteSpace(projectDirectory))
                {
                    continue;
                }

                items.Add(new LocalAssetItem(
                    LocalAssetType.CertificateProject,
                    Path.GetFileName(projectDirectory),
                    projectDirectory,
                    File.GetLastWriteTimeUtc(metadataPath)));
            }
        }
        catch (IOException exception)
        {
            AddScanIssue(issues, rootDirectory, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            AddScanIssue(issues, rootDirectory, exception);
        }
    }

    private static void AddFiles(
        string rootDirectory,
        string searchPattern,
        LocalAssetType type,
        List<LocalAssetItem> items,
        List<ValidationIssue> issues)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return;
        }

        try
        {
            foreach (var path in Directory.EnumerateFiles(rootDirectory, searchPattern, SearchOption.TopDirectoryOnly))
            {
                items.Add(new LocalAssetItem(
                    type,
                    Path.GetFileName(path),
                    path,
                    File.GetLastWriteTimeUtc(path)));
            }
        }
        catch (IOException exception)
        {
            AddScanIssue(issues, rootDirectory, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            AddScanIssue(issues, rootDirectory, exception);
        }
    }

    private static LocalAssetItem[] SortItems(List<LocalAssetItem> items) =>
        items
            .OrderByDescending(item => item.ModifiedAt)
            .ThenBy(item => item.Type)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static void AddScanIssue(List<ValidationIssue> issues, string path, Exception exception)
    {
        issues.Add(new ValidationIssue(
            LocalAssetLibraryErrorCodes.ScanFailed,
            ValidationSeverity.Warning,
            $"Could not scan {path}.",
            exception.GetType().Name));
    }
}
