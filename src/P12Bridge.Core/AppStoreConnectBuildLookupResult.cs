namespace P12Bridge.Core;

public sealed class AppStoreConnectBuildLookupResult
{
    public AppStoreConnectBuildLookupResult(
        string checkedAppEndpoint,
        string checkedBuildsEndpoint,
        AppStoreConnectApp? app,
        IReadOnlyList<AppStoreConnectBuild> builds,
        IReadOnlyList<ValidationIssue> issues)
    {
        CheckedAppEndpoint = checkedAppEndpoint;
        CheckedBuildsEndpoint = checkedBuildsEndpoint;
        App = app;
        Builds = builds;
        Issues = issues;
    }

    public string CheckedAppEndpoint { get; }

    public string CheckedBuildsEndpoint { get; }

    public AppStoreConnectApp? App { get; }

    public IReadOnlyList<AppStoreConnectBuild> Builds { get; }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public bool IsSuccess => !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public bool IsAppFound => App is not null;

    public bool HasBuilds => Builds.Count > 0;

    public static AppStoreConnectBuildLookupResult Success(
        string checkedAppEndpoint,
        string checkedBuildsEndpoint,
        AppStoreConnectApp? app,
        IReadOnlyList<AppStoreConnectBuild> builds) =>
        new(checkedAppEndpoint, checkedBuildsEndpoint, app, builds, Array.Empty<ValidationIssue>());

    public static AppStoreConnectBuildLookupResult Failure(
        string checkedAppEndpoint,
        string checkedBuildsEndpoint,
        params ValidationIssue[] issues) =>
        new(checkedAppEndpoint, checkedBuildsEndpoint, null, Array.Empty<AppStoreConnectBuild>(), issues);

    public static AppStoreConnectBuildLookupResult Failure(
        string checkedAppEndpoint,
        string checkedBuildsEndpoint,
        IReadOnlyList<ValidationIssue> issues) =>
        new(checkedAppEndpoint, checkedBuildsEndpoint, null, Array.Empty<AppStoreConnectBuild>(), issues);

    public override string ToString() =>
        $"AppStoreConnectBuildLookupResult {{ CheckedAppEndpoint = {CheckedAppEndpoint}, CheckedBuildsEndpoint = {CheckedBuildsEndpoint}, IsSuccess = {IsSuccess}, IsAppFound = {IsAppFound}, BuildCount = {Builds.Count}, Issues = {Issues.Count} }}";
}
