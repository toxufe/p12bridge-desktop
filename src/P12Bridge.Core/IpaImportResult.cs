namespace P12Bridge.Core;

public sealed record IpaImportResult(
    IpaMetadata? Metadata,
    string ImportedPath,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsSuccess => Metadata is not null
        && !string.IsNullOrWhiteSpace(ImportedPath)
        && !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public static IpaImportResult Success(
        IpaMetadata metadata,
        string importedPath,
        IReadOnlyList<ValidationIssue>? issues = null) =>
        new(metadata, importedPath, issues ?? Array.Empty<ValidationIssue>());

    public static IpaImportResult Failure(params ValidationIssue[] issues) =>
        new(null, string.Empty, issues);

    public static IpaImportResult FromInspectedIpa(
        IpaMetadata metadata,
        string importedPath,
        IReadOnlyList<ValidationIssue> issues) =>
        new(metadata, importedPath, issues);
}
