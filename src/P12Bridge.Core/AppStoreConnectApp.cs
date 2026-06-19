namespace P12Bridge.Core;

public sealed record AppStoreConnectApp(
    string Id,
    string Name,
    string BundleIdentifier,
    string Sku);
