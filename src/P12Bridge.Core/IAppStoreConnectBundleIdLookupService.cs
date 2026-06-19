namespace P12Bridge.Core;

public interface IAppStoreConnectBundleIdLookupService
{
    Task<AppStoreConnectBundleIdLookupResult> LookupByIdentifierAsync(
        AppStoreConnectBundleIdLookupRequest request,
        CancellationToken cancellationToken = default);
}
