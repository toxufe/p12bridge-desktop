namespace P12Bridge.Core;

public sealed record IpaInspectionResult(
    IpaMetadata? Metadata,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsSuccess => Metadata is not null && !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public static IpaInspectionResult Success(
        IpaMetadata metadata,
        IReadOnlyList<ValidationIssue>? issues = null) =>
        new(metadata, issues ?? Array.Empty<ValidationIssue>());

    public static IpaInspectionResult Failure(params ValidationIssue[] issues) =>
        new(null, issues);
}
