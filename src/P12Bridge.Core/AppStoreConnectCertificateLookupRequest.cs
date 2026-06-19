namespace P12Bridge.Core;

public sealed record AppStoreConnectCertificateLookupRequest(
    AppleApiKeyCredential Credential,
    int Limit = 10);
