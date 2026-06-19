namespace P12Bridge.Core;

public interface IAppStoreConnectCertificateLookupService
{
    Task<AppStoreConnectCertificateLookupResult> LookupAsync(
        AppStoreConnectCertificateLookupRequest request,
        CancellationToken cancellationToken = default);
}
