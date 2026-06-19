namespace P12Bridge.Core;

public sealed class AppStoreConnectProfileLookupResult
{
    public AppStoreConnectProfileLookupResult(
        string checkedBundleIdEndpoint,
        string checkedProfilesEndpoint,
        AppStoreConnectBundleId? bundleId,
        IReadOnlyList<AppStoreConnectProfile> profiles,
        IReadOnlyList<ValidationIssue> issues)
    {
        CheckedBundleIdEndpoint = checkedBundleIdEndpoint;
        CheckedProfilesEndpoint = checkedProfilesEndpoint;
        BundleId = bundleId;
        Profiles = profiles;
        Issues = issues;
    }

    public string CheckedBundleIdEndpoint { get; }

    public string CheckedProfilesEndpoint { get; }

    public AppStoreConnectBundleId? BundleId { get; }

    public IReadOnlyList<AppStoreConnectProfile> Profiles { get; }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public bool IsSuccess => !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public bool IsBundleIdFound => BundleId is not null;

    public bool HasProfiles => Profiles.Count > 0;

    public static AppStoreConnectProfileLookupResult Success(
        string checkedBundleIdEndpoint,
        string checkedProfilesEndpoint,
        AppStoreConnectBundleId? bundleId,
        IReadOnlyList<AppStoreConnectProfile> profiles) =>
        new(checkedBundleIdEndpoint, checkedProfilesEndpoint, bundleId, profiles, Array.Empty<ValidationIssue>());

    public static AppStoreConnectProfileLookupResult Failure(
        string checkedBundleIdEndpoint,
        string checkedProfilesEndpoint,
        params ValidationIssue[] issues) =>
        new(checkedBundleIdEndpoint, checkedProfilesEndpoint, null, Array.Empty<AppStoreConnectProfile>(), issues);

    public static AppStoreConnectProfileLookupResult Failure(
        string checkedBundleIdEndpoint,
        string checkedProfilesEndpoint,
        IReadOnlyList<ValidationIssue> issues) =>
        new(checkedBundleIdEndpoint, checkedProfilesEndpoint, null, Array.Empty<AppStoreConnectProfile>(), issues);

    public override string ToString() =>
        $"AppStoreConnectProfileLookupResult {{ CheckedBundleIdEndpoint = {CheckedBundleIdEndpoint}, CheckedProfilesEndpoint = {CheckedProfilesEndpoint}, IsSuccess = {IsSuccess}, IsBundleIdFound = {IsBundleIdFound}, ProfileCount = {Profiles.Count}, Issues = {Issues.Count} }}";
}
