namespace P12Bridge.Core;

public sealed record AppStoreConnectDeviceLookupRequest(
    AppleApiKeyCredential Credential,
    int Limit = 10);
