namespace P12Bridge.Core;

public sealed record LocalAssetLibraryResult(
    IReadOnlyList<LocalAssetItem> Items,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsSuccess => !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public static LocalAssetLibraryResult Success(IReadOnlyList<LocalAssetItem> items) =>
        new(items, Array.Empty<ValidationIssue>());

    public static LocalAssetLibraryResult Partial(IReadOnlyList<LocalAssetItem> items, params ValidationIssue[] issues) =>
        new(items, issues);
}
