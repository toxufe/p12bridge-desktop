namespace P12Bridge.Core;

public sealed record AppStoreConnectBundleIdLookupRequest(
    AppleApiKeyCredential Credential,
    string BundleIdentifier);
