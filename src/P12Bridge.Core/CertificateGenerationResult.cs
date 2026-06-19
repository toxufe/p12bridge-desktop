namespace P12Bridge.Core;

public sealed record CertificateGenerationResult(
    byte[] CertificateSigningRequestDer,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsSuccess => CertificateSigningRequestDer.Length > 0 && !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public static CertificateGenerationResult Success(byte[] certificateSigningRequestDer) =>
        new(certificateSigningRequestDer, Array.Empty<ValidationIssue>());

    public static CertificateGenerationResult Failure(params ValidationIssue[] issues) =>
        new(Array.Empty<byte>(), issues);
}
