namespace P12Bridge.Core;

public sealed class AppStoreConnectRemotePreflightResult
{
    public AppStoreConnectRemotePreflightResult(
        string bundleIdentifier,
        AppStoreConnectRemotePreflightSummary summary,
        IReadOnlyList<ValidationIssue> issues)
    {
        BundleIdentifier = bundleIdentifier;
        Summary = summary;
        Issues = issues;
    }

    public string BundleIdentifier { get; }

    public AppStoreConnectRemotePreflightSummary Summary { get; }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public bool IsSuccess => !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public bool HasWarnings => Issues.Any(issue => issue.Severity == ValidationSeverity.Warning);

    public static AppStoreConnectRemotePreflightResult Success(
        string bundleIdentifier,
        AppStoreConnectRemotePreflightSummary summary,
        IReadOnlyList<ValidationIssue>? issues = null) =>
        new(bundleIdentifier, summary, issues ?? Array.Empty<ValidationIssue>());

    public static AppStoreConnectRemotePreflightResult Failure(
        string bundleIdentifier,
        params ValidationIssue[] issues) =>
        new(bundleIdentifier, EmptySummary, issues);

    public static AppStoreConnectRemotePreflightResult Failure(
        string bundleIdentifier,
        IReadOnlyList<ValidationIssue> issues) =>
        new(bundleIdentifier, EmptySummary, issues);

    private static AppStoreConnectRemotePreflightSummary EmptySummary { get; } = new(
        AppFound: false,
        BundleIdFound: false,
        BuildCount: 0,
        ProfileCount: 0,
        CertificateCount: 0,
        DeviceCount: 0);

    public override string ToString() =>
        $"AppStoreConnectRemotePreflightResult {{ BundleIdentifier = {BundleIdentifier}, IsSuccess = {IsSuccess}, HasWarnings = {HasWarnings}, AppFound = {Summary.AppFound}, BundleIdFound = {Summary.BundleIdFound}, BuildCount = {Summary.BuildCount}, ProfileCount = {Summary.ProfileCount}, CertificateCount = {Summary.CertificateCount}, DeviceCount = {Summary.DeviceCount}, Issues = {Issues.Count} }}";
}
