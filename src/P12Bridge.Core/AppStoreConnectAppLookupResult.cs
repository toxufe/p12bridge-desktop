namespace P12Bridge.Core;

public sealed class AppStoreConnectAppLookupResult
{
    public AppStoreConnectAppLookupResult(
        string checkedEndpoint,
        AppStoreConnectApp? app,
        IReadOnlyList<ValidationIssue> issues)
    {
        CheckedEndpoint = checkedEndpoint;
        App = app;
        Issues = issues;
    }

    public string CheckedEndpoint { get; }

    public AppStoreConnectApp? App { get; }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public bool IsSuccess => !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public bool IsFound => App is not null;

    public static AppStoreConnectAppLookupResult Success(string checkedEndpoint, AppStoreConnectApp? app) =>
        new(checkedEndpoint, app, Array.Empty<ValidationIssue>());

    public static AppStoreConnectAppLookupResult Failure(string checkedEndpoint, params ValidationIssue[] issues) =>
        new(checkedEndpoint, null, issues);

    public override string ToString() =>
        $"AppStoreConnectAppLookupResult {{ CheckedEndpoint = {CheckedEndpoint}, IsSuccess = {IsSuccess}, IsFound = {IsFound}, Issues = {Issues.Count} }}";
}
