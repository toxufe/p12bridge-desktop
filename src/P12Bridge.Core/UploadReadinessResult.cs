namespace P12Bridge.Core;

public sealed record UploadReadinessResult(
    UploadReadinessStatus Status,
    IReadOnlyList<UploadReadinessCheck> Checks,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsReady => Status == UploadReadinessStatus.Ready;

    public static UploadReadinessResult FromChecks(IReadOnlyList<UploadReadinessCheck> checks)
    {
        var status = checks.Any(check => check.Status == UploadReadinessCheckStatus.Blocked)
            ? UploadReadinessStatus.Blocked
            : checks.Any(check => check.Status == UploadReadinessCheckStatus.Warning)
                ? UploadReadinessStatus.ReadyWithWarnings
                : UploadReadinessStatus.Ready;

        return new UploadReadinessResult(status, checks, ToIssues(checks));
    }

    private static IReadOnlyList<ValidationIssue> ToIssues(IReadOnlyList<UploadReadinessCheck> checks) =>
        checks
            .Where(check => check.Status is UploadReadinessCheckStatus.Blocked or UploadReadinessCheckStatus.Warning)
            .Select(check => new ValidationIssue(
                check.Code,
                check.Status == UploadReadinessCheckStatus.Blocked ? ValidationSeverity.Error : ValidationSeverity.Warning,
                check.Message,
                check.SuggestedAction))
            .ToArray();
}
