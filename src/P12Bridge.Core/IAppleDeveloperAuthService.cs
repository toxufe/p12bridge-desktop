namespace P12Bridge.Core;

public interface IAppleDeveloperAuthService
{
    AppleDeveloperTokenResult CreateToken(AppleApiKeyCredential credential, DateTimeOffset? now = null);

    Task<AppleDeveloperConnectionResult> CheckConnectionAsync(
        AppleApiKeyCredential credential,
        CancellationToken cancellationToken = default);
}
