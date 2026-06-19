using System.Text.RegularExpressions;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class InMemoryOperationHistoryService : IOperationHistoryService
{
    private const int MaxItems = 200;

    private static readonly Regex JwtLikePattern = new(
        @"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+",
        RegexOptions.Compiled);

    private static readonly Regex PrivateKeyPattern = new(
        @"-----BEGIN [^-]*PRIVATE KEY-----[\s\S]*?-----END [^-]*PRIVATE KEY-----",
        RegexOptions.Compiled);

    private static readonly Regex BearerPattern = new(
        @"Authorization:\s*Bearer\s+\S+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PasswordPattern = new(
        @"(?i)\b(password|passwd|pwd|p12password|app[-_ ]?specific[-_ ]?password)\s*[:=]\s*\S+",
        RegexOptions.Compiled);

    private static readonly Regex AppSpecificPasswordPattern = new(
        @"\b[A-Za-z0-9]{4}-[A-Za-z0-9]{4}-[A-Za-z0-9]{4}-[A-Za-z0-9]{4}\b",
        RegexOptions.Compiled);

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
            SanitizeSingleLine(request.Operation),
            request.Status,
            SanitizeSingleLine(request.Summary),
            Redact(request.Detail ?? string.Empty).Trim());

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

    private static string SanitizeSingleLine(string value) =>
        Redact(value).ReplaceLineEndings(" ").Trim();

    private static string Redact(string value)
    {
        var redacted = PrivateKeyPattern.Replace(value, "[REDACTED-PRIVATE-KEY]");
        redacted = JwtLikePattern.Replace(redacted, "[REDACTED-JWT]");
        redacted = BearerPattern.Replace(redacted, "Authorization: Bearer [REDACTED]");
        redacted = PasswordPattern.Replace(redacted, match =>
        {
            var key = match.Groups[1].Value;
            return $"{key}=[REDACTED]";
        });
        redacted = AppSpecificPasswordPattern.Replace(redacted, "[REDACTED-PASSWORD]");
        return redacted;
    }
}
