namespace P12Bridge.Core;

public sealed record UploadEnvironmentValidationResult(IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsSuccess => !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public static UploadEnvironmentValidationResult Success() =>
        new(Array.Empty<ValidationIssue>());

    public static UploadEnvironmentValidationResult Failure(params ValidationIssue[] issues) =>
        new(issues);
}
