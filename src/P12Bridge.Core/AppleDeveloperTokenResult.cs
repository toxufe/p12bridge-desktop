namespace P12Bridge.Core;

public sealed class AppleDeveloperTokenResult
{
    public AppleDeveloperTokenResult(
        string token,
        DateTimeOffset? expiresAt,
        IReadOnlyList<ValidationIssue> issues)
    {
        Token = token;
        ExpiresAt = expiresAt;
        Issues = issues;
    }

    public string Token { get; }

    public DateTimeOffset? ExpiresAt { get; }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public bool IsSuccess => !string.IsNullOrWhiteSpace(Token) && !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public static AppleDeveloperTokenResult Success(string token, DateTimeOffset expiresAt) =>
        new(token, expiresAt, Array.Empty<ValidationIssue>());

    public static AppleDeveloperTokenResult Failure(params ValidationIssue[] issues) =>
        new(string.Empty, null, issues);

    public override string ToString() =>
        $"AppleDeveloperTokenResult {{ Token = [REDACTED], ExpiresAt = {ExpiresAt:O}, IsSuccess = {IsSuccess}, Issues = {Issues.Count} }}";
}
