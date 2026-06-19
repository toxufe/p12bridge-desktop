namespace P12Bridge.Core;

public sealed class AppleDeveloperConnectionResult
{
    public AppleDeveloperConnectionResult(
        string checkedEndpoint,
        IReadOnlyList<ValidationIssue> issues)
    {
        CheckedEndpoint = checkedEndpoint;
        Issues = issues;
    }

    public string CheckedEndpoint { get; }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public bool IsSuccess => !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public static AppleDeveloperConnectionResult Success(string checkedEndpoint) =>
        new(checkedEndpoint, Array.Empty<ValidationIssue>());

    public static AppleDeveloperConnectionResult Failure(string checkedEndpoint, params ValidationIssue[] issues) =>
        new(checkedEndpoint, issues);

    public override string ToString() =>
        $"AppleDeveloperConnectionResult {{ CheckedEndpoint = {CheckedEndpoint}, IsSuccess = {IsSuccess}, Issues = {Issues.Count} }}";
}
