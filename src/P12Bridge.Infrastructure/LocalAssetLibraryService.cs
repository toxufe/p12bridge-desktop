using System.Text.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class LocalAssetLibraryService : ILocalAssetLibraryService
{
    private const string CertificateMetadataFileName = "p12bridge.project.json";
    private const string PrivateKeyFileName = "private.key";
    private const string CertificateSigningRequestFileName = "request.csr";
    private const string CertificateFileName = "certificate.cer";
    private const string P12FileName = "export.p12";

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
                    File.GetLastWriteTimeUtc(metadataPath),
                    ReadProjectNote(metadataPath),
                    ReadCertificateArtifacts(projectDirectory),
                    ReadCertificateExpiration(projectDirectory, issues)));
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

    private static string ReadProjectNote(string metadataPath)
    {
        try
        {
            using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
            if (metadata.RootElement.TryGetProperty("Note", out var noteElement)
                && noteElement.ValueKind == JsonValueKind.String)
            {
                return noteElement.GetString()?.Trim() ?? string.Empty;
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private static CertificateProjectArtifactStatus ReadCertificateArtifacts(string projectDirectory) =>
        new(
            File.Exists(Path.Combine(projectDirectory, PrivateKeyFileName)),
            File.Exists(Path.Combine(projectDirectory, CertificateSigningRequestFileName)),
            File.Exists(Path.Combine(projectDirectory, CertificateFileName)),
            File.Exists(Path.Combine(projectDirectory, P12FileName)));

    private static DateTimeOffset? ReadCertificateExpiration(
        string projectDirectory,
        List<ValidationIssue> issues)
    {
        var certificatePath = Path.Combine(projectDirectory, CertificateFileName);
        if (!File.Exists(certificatePath))
        {
            return null;
        }

        try
        {
            using var certificate = new X509Certificate2(certificatePath);
            return new DateTimeOffset(certificate.NotAfter).ToUniversalTime();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or CryptographicException)
        {
            issues.Add(new ValidationIssue(
                LocalAssetLibraryErrorCodes.ScanFailed,
                ValidationSeverity.Warning,
                $"Could not read certificate metadata for {projectDirectory}.",
                exception.GetType().Name));
            return null;
        }
    }

    private static void AddScanIssue(List<ValidationIssue> issues, string path, Exception exception)
    {
        issues.Add(new ValidationIssue(
            LocalAssetLibraryErrorCodes.ScanFailed,
            ValidationSeverity.Warning,
            $"Could not scan {path}.",
            exception.GetType().Name));
    }
}
