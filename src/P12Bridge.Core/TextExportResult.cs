namespace P12Bridge.Core;

public sealed record TextExportResult(
    string OutputPath,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsSuccess => !string.IsNullOrWhiteSpace(OutputPath)
        && !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public static TextExportResult Success(string outputPath) =>
        new(outputPath, Array.Empty<ValidationIssue>());

    public static TextExportResult Failure(params ValidationIssue[] issues) =>
        new(string.Empty, issues);
}
