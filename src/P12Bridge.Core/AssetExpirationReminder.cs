namespace P12Bridge.Core;

public sealed record AssetExpirationReminder(
    AssetExpirationReminderType Type,
    string Name,
    string Path,
    DateTimeOffset ExpiresAt,
    AssetExpirationReminderStatus Status,
    int DaysRemaining);
