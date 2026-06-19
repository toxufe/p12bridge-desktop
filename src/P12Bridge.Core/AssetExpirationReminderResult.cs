namespace P12Bridge.Core;

public sealed record AssetExpirationReminderResult(
    IReadOnlyList<AssetExpirationReminder> Reminders,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsSuccess => Issues.All(issue => issue.Severity != ValidationSeverity.Error);

    public static AssetExpirationReminderResult Success(IReadOnlyList<AssetExpirationReminder> reminders) =>
        new(reminders, Array.Empty<ValidationIssue>());

    public static AssetExpirationReminderResult Partial(
        IReadOnlyList<AssetExpirationReminder> reminders,
        params ValidationIssue[] issues) =>
        new(reminders, issues);
}
