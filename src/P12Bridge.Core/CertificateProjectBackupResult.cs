namespace P12Bridge.Core;

public sealed record CertificateProjectBackupResult(
    string BackupPath,
    int FilesIncluded,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsSuccess => !string.IsNullOrWhiteSpace(BackupPath)
        && FilesIncluded > 0
        && !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public static CertificateProjectBackupResult Success(string backupPath, int filesIncluded) =>
        new(backupPath, filesIncluded, Array.Empty<ValidationIssue>());

    public static CertificateProjectBackupResult Failure(params ValidationIssue[] issues) =>
        new(string.Empty, 0, issues);
}
