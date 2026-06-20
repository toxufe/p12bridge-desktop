using System.Text.Json;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class JsonOperationHistoryService : IOperationHistoryService
{
    private const int MaxItems = 200;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string historyPath;
    private readonly IClock clock;

    public JsonOperationHistoryService(string? historyPath = null, IClock? clock = null)
    {
        this.historyPath = string.IsNullOrWhiteSpace(historyPath)
            ? GetDefaultHistoryPath()
            : historyPath;
        this.clock = clock ?? new SystemClock();
    }

    public OperationHistoryResult List()
    {
        if (!File.Exists(historyPath))
        {
            return OperationHistoryResult.Success(Array.Empty<OperationHistoryItem>());
        }

        try
        {
            var json = File.ReadAllText(historyPath);
            var stored = JsonSerializer.Deserialize<List<StoredOperationHistoryItem>>(json, SerializerOptions);

            if (stored is null)
            {
                return LoadWarning();
            }

            return OperationHistoryResult.Success(ToItems(stored));
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException
            or ArgumentException)
        {
            return LoadWarning();
        }
    }

    public OperationHistoryResult Record(OperationHistoryRecordRequest request)
    {
        var current = List();
        var items = current.Items.ToList();
        items.Insert(0, new OperationHistoryItem(
            clock.UtcNow,
            OperationHistoryRedactor.SanitizeSingleLine(request.Operation),
            request.Status,
            OperationHistoryRedactor.SanitizeSingleLine(request.Summary),
            OperationHistoryRedactor.Redact(request.Detail ?? string.Empty).Trim()));

        if (items.Count > MaxItems)
        {
            items.RemoveRange(MaxItems, items.Count - MaxItems);
        }

        return SaveItems(items);
    }

    public OperationHistoryResult Clear()
    {
        try
        {
            if (File.Exists(historyPath))
            {
                File.Delete(historyPath);
            }

            return OperationHistoryResult.Success(Array.Empty<OperationHistoryItem>());
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException)
        {
            return OperationHistoryResult.Failure(new ValidationIssue(
                OperationHistoryErrorCodes.ClearFailed,
                ValidationSeverity.Error,
                "Operation history could not be cleared.",
                "Check the history storage location and try again."));
        }
    }

    private OperationHistoryResult SaveItems(IReadOnlyList<OperationHistoryItem> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(historyPath) ?? ".");
            var stored = items.Select(FromItem).ToArray();
            var json = JsonSerializer.Serialize(stored, SerializerOptions);
            File.WriteAllText(historyPath, json);
            return OperationHistoryResult.Success(items.ToArray());
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException)
        {
            return OperationHistoryResult.Failure(new ValidationIssue(
                OperationHistoryErrorCodes.SaveFailed,
                ValidationSeverity.Error,
                "Operation history could not be saved.",
                "Check the history storage location and try again."));
        }
    }

    private static OperationHistoryResult LoadWarning() =>
        OperationHistoryResult.Warning(
            Array.Empty<OperationHistoryItem>(),
            new ValidationIssue(
                OperationHistoryErrorCodes.LoadFailed,
                ValidationSeverity.Warning,
                "Operation history could not be loaded.",
                "Continue with a new history list."));

    private static OperationHistoryItem[] ToItems(List<StoredOperationHistoryItem> stored) =>
        stored
            .Where(item => !string.IsNullOrWhiteSpace(item.Operation))
            .Select(item => new OperationHistoryItem(
                item.OccurredAt,
                OperationHistoryRedactor.SanitizeSingleLine(item.Operation ?? string.Empty),
                item.Status,
                OperationHistoryRedactor.SanitizeSingleLine(item.Summary ?? string.Empty),
                OperationHistoryRedactor.Redact(item.Detail ?? string.Empty).Trim()))
            .Take(MaxItems)
            .ToArray();

    private static StoredOperationHistoryItem FromItem(OperationHistoryItem item) =>
        new()
        {
            OccurredAt = item.OccurredAt,
            Operation = item.Operation,
            Status = item.Status,
            Summary = item.Summary,
            Detail = item.Detail
        };

    private static string GetDefaultHistoryPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "P12Bridge",
            "operation-history.json");

    private sealed class StoredOperationHistoryItem
    {
        public DateTimeOffset OccurredAt { get; set; }

        public string? Operation { get; set; }

        public OperationHistoryStatus Status { get; set; }

        public string? Summary { get; set; }

        public string? Detail { get; set; }
    }
}
