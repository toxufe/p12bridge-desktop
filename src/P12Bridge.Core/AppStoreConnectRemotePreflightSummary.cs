namespace P12Bridge.Core;

public sealed record AppStoreConnectRemotePreflightSummary(
    bool AppFound,
    bool BundleIdFound,
    int BuildCount,
    int ProfileCount,
    int CertificateCount,
    int DeviceCount);
