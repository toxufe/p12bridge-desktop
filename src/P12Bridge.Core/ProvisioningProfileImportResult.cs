namespace P12Bridge.Core;

public sealed record ProvisioningProfileImportResult(
    ProvisioningProfile? Profile,
    string ImportedPath,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsSuccess => Profile is not null
        && !string.IsNullOrWhiteSpace(ImportedPath)
        && !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public static ProvisioningProfileImportResult Success(
        ProvisioningProfile profile,
        string importedPath,
        IReadOnlyList<ValidationIssue>? issues = null) =>
        new(profile, importedPath, issues ?? Array.Empty<ValidationIssue>());

    public static ProvisioningProfileImportResult Failure(params ValidationIssue[] issues) =>
        new(null, string.Empty, issues);

    public static ProvisioningProfileImportResult FromParsedProfile(
        ProvisioningProfile profile,
        string importedPath,
        IReadOnlyList<ValidationIssue> issues) =>
        new(profile, importedPath, issues);
}
