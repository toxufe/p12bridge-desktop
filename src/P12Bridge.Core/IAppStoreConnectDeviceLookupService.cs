namespace P12Bridge.Core;

public interface IAppStoreConnectDeviceLookupService
{
    Task<AppStoreConnectDeviceLookupResult> LookupAsync(
        AppStoreConnectDeviceLookupRequest request,
        CancellationToken cancellationToken = default);
}
