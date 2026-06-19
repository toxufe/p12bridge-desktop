namespace P12Bridge.Core;

public sealed record OperationHistoryRecordRequest(
    string Operation,
    OperationHistoryStatus Status,
    string Summary,
    string? Detail = null);
