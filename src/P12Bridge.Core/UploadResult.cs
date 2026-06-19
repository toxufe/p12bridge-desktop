namespace P12Bridge.Core;

public sealed record UploadResult(
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsSuccess => ExitCode == 0 && !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public static UploadResult Success(int exitCode, string standardOutput, string standardError) =>
        new(exitCode, standardOutput, standardError, Array.Empty<ValidationIssue>());

    public static UploadResult Failure(
        int? exitCode,
        string standardOutput,
        string standardError,
        params ValidationIssue[] issues) =>
        new(exitCode, standardOutput, standardError, issues);
}
