namespace P12Bridge.Core;

public sealed record AppStoreConnectCertificate(
    string Id,
    string Name,
    string DisplayName,
    string CertificateType,
    string SerialNumber,
    string Platform,
    DateTimeOffset? ExpirationDate,
    bool? Activated);
