namespace P12Bridge.Core;

public sealed record AppStoreConnectBuildLookupRequest(
    AppleApiKeyCredential Credential,
    string BundleIdentifier,
    int Limit = 5);
