namespace P12Bridge.Core;

public interface IAssetExpirationReminderService
{
    AssetExpirationReminderResult Scan(AssetExpirationReminderRequest request, DateTimeOffset? now = null);
}
