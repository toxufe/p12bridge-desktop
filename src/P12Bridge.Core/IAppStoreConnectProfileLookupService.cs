namespace P12Bridge.Core;

public interface IAppStoreConnectProfileLookupService
{
    Task<AppStoreConnectProfileLookupResult> LookupByBundleIdAsync(
        AppStoreConnectProfileLookupRequest request,
        CancellationToken cancellationToken = default);
}
