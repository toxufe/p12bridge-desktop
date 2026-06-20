using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public static class OperationHistoryExportFormatter
{
    public static string Format(IReadOnlyList<OperationHistoryItem> items) =>
        string.Join($"{Environment.NewLine}{Environment.NewLine}", items.Select(Format));

    public static string Format(OperationHistoryItem item)
    {
        var parts = new List<string>
        {
            $"{item.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} {FormatStatus(item.Status)} {item.Operation}",
            item.Summary
        };

        if (!string.IsNullOrWhiteSpace(item.Detail))
        {
            parts.Add(item.Detail);
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string FormatStatus(OperationHistoryStatus status) =>
        status switch
        {
            OperationHistoryStatus.Success => "成功",
            OperationHistoryStatus.Warning => "警告",
            OperationHistoryStatus.Failed => "失败",
            _ => "未知"
        };
}
