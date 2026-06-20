using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class InMemoryOperationHistoryService : IOperationHistoryService
{
    private const int MaxItems = 200;

    private readonly IClock clock;
    private readonly List<OperationHistoryItem> items = [];

    public InMemoryOperationHistoryService(IClock? clock = null)
    {
        this.clock = clock ?? new SystemClock();
    }

    public OperationHistoryResult List() =>
        OperationHistoryResult.Success(items.ToArray());

    public OperationHistoryResult Record(OperationHistoryRecordRequest request)
    {
        var item = new OperationHistoryItem(
            clock.UtcNow,
            OperationHistoryRedactor.SanitizeSingleLine(request.Operation),
            request.Status,
            OperationHistoryRedactor.SanitizeSingleLine(request.Summary),
            OperationHistoryRedactor.Redact(request.Detail ?? string.Empty).Trim());

        items.Insert(0, item);

        if (items.Count > MaxItems)
        {
            items.RemoveRange(MaxItems, items.Count - MaxItems);
        }

        return List();
    }

    public OperationHistoryResult Clear()
    {
        items.Clear();
        return List();
    }
}
