namespace P12Bridge.Core;

public sealed record PrivateKeyGenerationResult(
    byte[] PrivateKeyPkcs8,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsSuccess => PrivateKeyPkcs8.Length > 0 && !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public static PrivateKeyGenerationResult Success(byte[] privateKeyPkcs8) =>
        new(privateKeyPkcs8, Array.Empty<ValidationIssue>());

    public static PrivateKeyGenerationResult Failure(params ValidationIssue[] issues) =>
        new(Array.Empty<byte>(), issues);
}
