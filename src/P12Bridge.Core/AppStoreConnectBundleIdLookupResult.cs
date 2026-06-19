namespace P12Bridge.Core;

public sealed class AppStoreConnectBundleIdLookupResult
{
    public AppStoreConnectBundleIdLookupResult(
        string checkedEndpoint,
        AppStoreConnectBundleId? bundleId,
        IReadOnlyList<ValidationIssue> issues)
    {
        CheckedEndpoint = checkedEndpoint;
        BundleId = bundleId;
        Issues = issues;
    }

    public string CheckedEndpoint { get; }

    public AppStoreConnectBundleId? BundleId { get; }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public bool IsSuccess => !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public bool IsFound => BundleId is not null;

    public static AppStoreConnectBundleIdLookupResult Success(
        string checkedEndpoint,
        AppStoreConnectBundleId? bundleId) =>
        new(checkedEndpoint, bundleId, Array.Empty<ValidationIssue>());

    public static AppStoreConnectBundleIdLookupResult Failure(
        string checkedEndpoint,
        params ValidationIssue[] issues) =>
        new(checkedEndpoint, null, issues);

    public static AppStoreConnectBundleIdLookupResult Failure(
        string checkedEndpoint,
        IReadOnlyList<ValidationIssue> issues) =>
        new(checkedEndpoint, null, issues);

    public override string ToString() =>
        $"AppStoreConnectBundleIdLookupResult {{ CheckedEndpoint = {CheckedEndpoint}, IsSuccess = {IsSuccess}, IsFound = {IsFound}, Issues = {Issues.Count} }}";
}
