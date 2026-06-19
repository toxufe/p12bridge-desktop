namespace P12Bridge.Core;

public interface IAppStoreConnectAppLookupService
{
    Task<AppStoreConnectAppLookupResult> LookupByBundleIdAsync(
        AppStoreConnectAppLookupRequest request,
        CancellationToken cancellationToken = default);
}
