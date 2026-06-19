namespace P12Bridge.Core;

public sealed record CertificateProjectCreateResult(
    SigningAssetProject? Project,
    CertificateProjectArtifactPaths? Artifacts,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsSuccess => Project is not null
        && Artifacts is not null
        && !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public static CertificateProjectCreateResult Success(
        SigningAssetProject project,
        CertificateProjectArtifactPaths artifacts) =>
        new(project, artifacts, Array.Empty<ValidationIssue>());

    public static CertificateProjectCreateResult Failure(params ValidationIssue[] issues) =>
        new(null, null, issues);
}
