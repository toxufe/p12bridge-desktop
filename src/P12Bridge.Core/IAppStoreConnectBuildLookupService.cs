namespace P12Bridge.Core;

public interface IAppStoreConnectBuildLookupService
{
    Task<AppStoreConnectBuildLookupResult> LookupByBundleIdAsync(
        AppStoreConnectBuildLookupRequest request,
        CancellationToken cancellationToken = default);
}
