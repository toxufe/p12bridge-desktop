namespace P12Bridge.Core;

public sealed record ProvisioningProfile(
    string Uuid,
    string Name,
    string TeamId,
    string ApplicationIdentifier,
    string BundleIdentifier,
    DateTimeOffset CreationDate,
    DateTimeOffset ExpirationDate,
    ProvisioningProfileType Type,
    ProvisioningProfileStatus Status,
    int ProvisionedDeviceCount,
    IReadOnlyList<string> DeveloperCertificateFingerprints);
