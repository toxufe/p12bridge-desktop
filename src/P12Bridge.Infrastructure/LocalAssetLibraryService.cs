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
    private const int BackupTimestampLength = 14;

    private readonly IProvisioningProfileParser profileParser;
    private readonly IIpaInspector ipaInspector;

    public LocalAssetLibraryService(
        IProvisioningProfileParser? profileParser = null,
        IIpaInspector? ipaInspector = null)
    {
        this.profileParser = profileParser ?? new ProvisioningProfileParser();
        this.ipaInspector = ipaInspector ?? new IpaInspector(this.profileParser);
    }

    public LocalAssetLibraryResult Scan(LocalAssetLibraryRequest request)
    {
        var items = new List<LocalAssetItem>();
        var issues = new List<ValidationIssue>();

        var localCertificateFingerprints = AddCertificateProjects(request.CertificateDirectory, items, issues);
        AddProvisioningProfiles(request.ProfileDirectory, items, issues, localCertificateFingerprints);
        AddIpas(request.IpaDirectory, items, issues);

        return issues.Count == 0
            ? LocalAssetLibraryResult.Success(SortItems(items))
            : LocalAssetLibraryResult.Partial(SortItems(items), issues.ToArray());
    }

    private static IReadOnlySet<string> AddCertificateProjects(
        string rootDirectory,
        List<LocalAssetItem> items,
        List<ValidationIssue> issues)
    {
        var certificateFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(rootDirectory))
        {
            return certificateFingerprints;
        }

        try
        {
            var backupIndex = ReadCertificateBackupIndex(rootDirectory, issues);
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

                var certificateMetadata = ReadCertificateMetadata(projectDirectory, issues);
                if (!string.IsNullOrWhiteSpace(certificateMetadata.Fingerprint))
                {
                    certificateFingerprints.Add(certificateMetadata.Fingerprint);
                }

                items.Add(new LocalAssetItem(
                    LocalAssetType.CertificateProject,
                    Path.GetFileName(projectDirectory),
                    projectDirectory,
                    File.GetLastWriteTimeUtc(metadataPath),
                    ReadProjectNote(metadataPath),
                    ReadCertificateArtifacts(projectDirectory),
                    certificateMetadata.ExpiresAt,
                    BackupSummary: ReadCertificateBackupSummary(projectDirectory, backupIndex),
                    BackupPath: ReadCertificateBackupPath(projectDirectory, backupIndex)));
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

        return certificateFingerprints;
    }

    private void AddProvisioningProfiles(
        string rootDirectory,
        List<LocalAssetItem> items,
        List<ValidationIssue> issues,
        IReadOnlySet<string> localCertificateFingerprints)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return;
        }

        try
        {
            foreach (var path in Directory.EnumerateFiles(rootDirectory, "*.mobileprovision", SearchOption.TopDirectoryOnly))
            {
                var profileMetadata = ReadProvisioningProfileMetadata(path, issues, localCertificateFingerprints);
                items.Add(new LocalAssetItem(
                    LocalAssetType.ProvisioningProfile,
                    Path.GetFileName(path),
                    path,
                    File.GetLastWriteTimeUtc(path),
                    ExpiresAt: profileMetadata.ExpiresAt,
                    SafeMetadataSummary: profileMetadata.SafeSummary));
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

    private void AddIpas(
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
            foreach (var path in Directory.EnumerateFiles(rootDirectory, "*.ipa", SearchOption.TopDirectoryOnly))
            {
                var ipaSummary = ReadIpaSummary(path, issues);
                items.Add(new LocalAssetItem(
                    LocalAssetType.Ipa,
                    Path.GetFileName(path),
                    path,
                    File.GetLastWriteTimeUtc(path),
                    SafeMetadataSummary: ipaSummary));
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

    private static string ReadCertificateBackupSummary(
        string projectDirectory,
        IReadOnlyDictionary<string, CertificateBackupIndexItem> backupIndex)
    {
        var projectPrefix = CertificateProjectBackupNames.CreateProjectPrefix(projectDirectory);
        return backupIndex.TryGetValue(projectPrefix, out var backup)
            ? $"备份 {backup.LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd}"
            : string.Empty;
    }

    private static string ReadCertificateBackupPath(
        string projectDirectory,
        IReadOnlyDictionary<string, CertificateBackupIndexItem> backupIndex)
    {
        var projectPrefix = CertificateProjectBackupNames.CreateProjectPrefix(projectDirectory);
        return backupIndex.TryGetValue(projectPrefix, out var backup)
            ? backup.Path
            : string.Empty;
    }

    private static IReadOnlyDictionary<string, CertificateBackupIndexItem> ReadCertificateBackupIndex(
        string rootDirectory,
        List<ValidationIssue> issues)
    {
        var backups = new Dictionary<string, CertificateBackupIndexItem>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var backupPath in Directory.EnumerateFiles(rootDirectory, "*.zip", SearchOption.AllDirectories))
            {
                var projectPrefix = TryReadBackupProjectPrefix(backupPath);
                if (string.IsNullOrWhiteSpace(projectPrefix))
                {
                    continue;
                }

                var writeTime = new DateTimeOffset(File.GetLastWriteTimeUtc(backupPath), TimeSpan.Zero);
                if (!backups.TryGetValue(projectPrefix, out var existing) || writeTime > existing.LastWriteTimeUtc)
                {
                    backups[projectPrefix] = new CertificateBackupIndexItem(backupPath, writeTime);
                }
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            AddScanIssue(issues, rootDirectory, exception);
        }

        return backups;
    }

    private static string? TryReadBackupProjectPrefix(string backupPath)
    {
        var name = Path.GetFileNameWithoutExtension(backupPath);
        if (string.IsNullOrWhiteSpace(name)
            || name.Length <= BackupTimestampLength + 1
            || name[^15] != '-')
        {
            return null;
        }

        return name.Substring(name.Length - BackupTimestampLength).All(char.IsDigit)
            ? name[..^15]
            : null;
    }

    private sealed record CertificateBackupIndexItem(
        string Path,
        DateTimeOffset LastWriteTimeUtc);

    private static CertificateProjectArtifactStatus ReadCertificateArtifacts(string projectDirectory) =>
        new(
            File.Exists(Path.Combine(projectDirectory, PrivateKeyFileName)),
            File.Exists(Path.Combine(projectDirectory, CertificateSigningRequestFileName)),
            File.Exists(Path.Combine(projectDirectory, CertificateFileName)),
            File.Exists(Path.Combine(projectDirectory, P12FileName)));

    private static (DateTimeOffset? ExpiresAt, string Fingerprint) ReadCertificateMetadata(
        string projectDirectory,
        List<ValidationIssue> issues)
    {
        var certificatePath = Path.Combine(projectDirectory, CertificateFileName);
        if (!File.Exists(certificatePath))
        {
            return (null, string.Empty);
        }

        try
        {
            using var certificate = new X509Certificate2(certificatePath);
            var certificateBytes = certificate.Export(X509ContentType.Cert);
            return (
                new DateTimeOffset(certificate.NotAfter).ToUniversalTime(),
                Convert.ToHexString(SHA256.HashData(certificateBytes)));
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
            return (null, string.Empty);
        }
    }

    private (DateTimeOffset? ExpiresAt, string SafeSummary) ReadProvisioningProfileMetadata(
        string profilePath,
        List<ValidationIssue> issues,
        IReadOnlySet<string> localCertificateFingerprints)
    {
        try
        {
            var result = profileParser.Parse(File.ReadAllBytes(profilePath));
            if (result.Profile is not null)
            {
                AddProfileWarnings(issues, profilePath, result.Issues);
                return (
                    result.Profile.ExpirationDate,
                    FormatProfileSummary(result.Profile, localCertificateFingerprints));
            }

            AddProfileWarnings(issues, profilePath, result.Issues);
            return (null, string.Empty);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            AddScanIssue(issues, profilePath, exception);
            return (null, string.Empty);
        }
    }

    private static string FormatProfileSummary(
        ProvisioningProfile profile,
        IReadOnlySet<string> localCertificateFingerprints)
    {
        var matchCount = profile.DeveloperCertificateFingerprints
            .Count(localCertificateFingerprints.Contains);
        return $"{FormatProfileType(profile.Type)} / {FormatProfileStatus(profile.Status)} / {profile.BundleIdentifier} / {profile.TeamId} / 证书 {profile.DeveloperCertificateFingerprints.Count} / 匹配 {matchCount}";
    }

    private static string FormatProfileType(ProvisioningProfileType type) =>
        type switch
        {
            ProvisioningProfileType.Development => "开发",
            ProvisioningProfileType.AdHoc => "Ad Hoc",
            ProvisioningProfileType.AppStore => "App Store",
            ProvisioningProfileType.Enterprise => "企业",
            _ => "未知"
        };

    private static string FormatProfileStatus(ProvisioningProfileStatus status) =>
        status == ProvisioningProfileStatus.Active ? "有效" : "过期";

    private string ReadIpaSummary(
        string ipaPath,
        List<ValidationIssue> issues)
    {
        try
        {
            var result = ipaInspector.Inspect(File.ReadAllBytes(ipaPath));
            AddIpaWarnings(issues, ipaPath, result.Issues);
            return result.Metadata is null || !result.IsSuccess
                ? string.Empty
                : FormatIpaSummary(result.Metadata);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            AddScanIssue(issues, ipaPath, exception);
            return string.Empty;
        }
    }

    private static string FormatIpaSummary(IpaMetadata metadata)
    {
        var parts = new[]
        {
            metadata.BundleIdentifier,
            $"{metadata.ShortVersion} ({metadata.BuildVersion})",
            metadata.DisplayName
        };

        return string.Join(" / ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static void AddIpaWarnings(
        List<ValidationIssue> issues,
        string ipaPath,
        IReadOnlyList<ValidationIssue> inspectionIssues)
    {
        foreach (var issue in inspectionIssues)
        {
            issues.Add(new ValidationIssue(
                LocalAssetLibraryErrorCodes.ScanFailed,
                ValidationSeverity.Warning,
                $"Could not read IPA metadata for {ipaPath}.",
                issue.Code));
        }
    }

    private static void AddProfileWarnings(
        List<ValidationIssue> issues,
        string profilePath,
        IReadOnlyList<ValidationIssue> parseIssues)
    {
        foreach (var issue in parseIssues)
        {
            issues.Add(new ValidationIssue(
                LocalAssetLibraryErrorCodes.ScanFailed,
                ValidationSeverity.Warning,
                $"Could not read profile metadata for {profilePath}.",
                issue.Code));
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
