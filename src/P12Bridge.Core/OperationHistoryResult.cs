namespace P12Bridge.Core;

public sealed record OperationHistoryResult(
    IReadOnlyList<OperationHistoryItem> Items,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsSuccess => Issues.All(issue => issue.Severity != ValidationSeverity.Error);

    public static OperationHistoryResult Success(IReadOnlyList<OperationHistoryItem> items) =>
        new(items, Array.Empty<ValidationIssue>());

    public static OperationHistoryResult Warning(IReadOnlyList<OperationHistoryItem> items, params ValidationIssue[] issues) =>
        new(items, issues);

    public static OperationHistoryResult Failure(params ValidationIssue[] issues) =>
        new(Array.Empty<OperationHistoryItem>(), issues);
}
