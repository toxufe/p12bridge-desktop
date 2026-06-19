namespace P12Bridge.Core;

public sealed record CertificateProjectP12ExportResult(
    string CertificatePath,
    string P12Path,
    string MetadataPath,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsSuccess => !string.IsNullOrWhiteSpace(CertificatePath)
        && !string.IsNullOrWhiteSpace(P12Path)
        && !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public static CertificateProjectP12ExportResult Success(
        string certificatePath,
        string p12Path,
        string metadataPath) =>
        new(certificatePath, p12Path, metadataPath, Array.Empty<ValidationIssue>());

    public static CertificateProjectP12ExportResult Failure(params ValidationIssue[] issues) =>
        new(string.Empty, string.Empty, string.Empty, issues);
}
