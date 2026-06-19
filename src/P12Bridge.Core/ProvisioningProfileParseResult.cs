namespace P12Bridge.Core;

public sealed record ProvisioningProfileParseResult(
    ProvisioningProfile? Profile,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsSuccess => Profile is not null && !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public static ProvisioningProfileParseResult Success(
        ProvisioningProfile profile,
        IReadOnlyList<ValidationIssue>? issues = null) =>
        new(profile, issues ?? Array.Empty<ValidationIssue>());

    public static ProvisioningProfileParseResult Failure(params ValidationIssue[] issues) =>
        new(null, issues);
}
