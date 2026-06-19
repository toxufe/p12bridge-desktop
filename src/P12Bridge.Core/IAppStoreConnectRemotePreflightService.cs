namespace P12Bridge.Core;

public interface IAppStoreConnectRemotePreflightService
{
    Task<AppStoreConnectRemotePreflightResult> CheckAsync(
        AppStoreConnectRemotePreflightRequest request,
        CancellationToken cancellationToken = default);
}
