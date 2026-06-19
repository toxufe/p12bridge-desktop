namespace P12Bridge.Core;

public sealed class AppStoreConnectCertificateLookupResult
{
    public AppStoreConnectCertificateLookupResult(
        string checkedEndpoint,
        IReadOnlyList<AppStoreConnectCertificate> certificates,
        IReadOnlyList<ValidationIssue> issues)
    {
        CheckedEndpoint = checkedEndpoint;
        Certificates = certificates;
        Issues = issues;
    }

    public string CheckedEndpoint { get; }

    public IReadOnlyList<AppStoreConnectCertificate> Certificates { get; }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public bool IsSuccess => !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public bool HasCertificates => Certificates.Count > 0;

    public static AppStoreConnectCertificateLookupResult Success(
        string checkedEndpoint,
        IReadOnlyList<AppStoreConnectCertificate> certificates) =>
        new(checkedEndpoint, certificates, Array.Empty<ValidationIssue>());

    public static AppStoreConnectCertificateLookupResult Failure(
        string checkedEndpoint,
        params ValidationIssue[] issues) =>
        new(checkedEndpoint, Array.Empty<AppStoreConnectCertificate>(), issues);

    public static AppStoreConnectCertificateLookupResult Failure(
        string checkedEndpoint,
        IReadOnlyList<ValidationIssue> issues) =>
        new(checkedEndpoint, Array.Empty<AppStoreConnectCertificate>(), issues);

    public override string ToString() =>
        $"AppStoreConnectCertificateLookupResult {{ CheckedEndpoint = {CheckedEndpoint}, IsSuccess = {IsSuccess}, CertificateCount = {Certificates.Count}, Issues = {Issues.Count} }}";
}
