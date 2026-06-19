namespace P12Bridge.Core;

public sealed record OperationHistoryItem(
    DateTimeOffset OccurredAt,
    string Operation,
    OperationHistoryStatus Status,
    string Summary,
    string Detail);
