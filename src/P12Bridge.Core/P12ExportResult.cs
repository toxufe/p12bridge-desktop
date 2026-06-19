namespace P12Bridge.Core;

public sealed record P12ExportResult(
    byte[] Pkcs12Bytes,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsSuccess => Pkcs12Bytes.Length > 0 && !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public static P12ExportResult Success(byte[] pkcs12Bytes) =>
        new(pkcs12Bytes, Array.Empty<ValidationIssue>());

    public static P12ExportResult Failure(params ValidationIssue[] issues) =>
        new(Array.Empty<byte>(), issues);
}
