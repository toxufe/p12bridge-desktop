namespace P12Bridge.Core;

public sealed record AppStoreConnectAppLookupRequest(
    AppleApiKeyCredential Credential,
    string BundleIdentifier);
