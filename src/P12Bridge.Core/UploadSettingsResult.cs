namespace P12Bridge.Core;

public sealed record UploadSettingsResult(
    UploadSettings Settings,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsSuccess => Issues.All(issue => issue.Severity != ValidationSeverity.Error);

    public static UploadSettingsResult Success(UploadSettings settings) =>
        new(settings, Array.Empty<ValidationIssue>());

    public static UploadSettingsResult Warning(UploadSettings settings, params ValidationIssue[] issues) =>
        new(settings, issues);

    public static UploadSettingsResult Failure(params ValidationIssue[] issues) =>
        new(new UploadSettings(), issues);
}
