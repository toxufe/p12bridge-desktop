namespace P12Bridge.Core;

public sealed record OperationHistoryResult(
    IReadOnlyList<OperationHistoryItem> Items,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsSuccess => Issues.All(issue => issue.Severity != ValidationSeverity.Error);

    public static OperationHistoryResult Success(IReadOnlyList<OperationHistoryItem> items) =>
        new(items, Array.Empty<ValidationIssue>());
}
