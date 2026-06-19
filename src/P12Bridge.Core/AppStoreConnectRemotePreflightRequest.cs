namespace P12Bridge.Core;

public sealed record AppStoreConnectRemotePreflightRequest(
    AppleApiKeyCredential Credential,
    string BundleIdentifier);
