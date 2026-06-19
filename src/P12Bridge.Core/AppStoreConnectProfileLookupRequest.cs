namespace P12Bridge.Core;

public sealed record AppStoreConnectProfileLookupRequest(
    AppleApiKeyCredential Credential,
    string BundleIdentifier,
    int Limit = 10);
