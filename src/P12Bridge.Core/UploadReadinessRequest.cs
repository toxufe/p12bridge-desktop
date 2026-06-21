namespace P12Bridge.Core;

public sealed record UploadReadinessRequest(
    UploadTarget Target,
    IpaMetadata? IpaMetadata,
    ProvisioningProfile? ImportedProvisioningProfile = null,
    string PackagePath = "",
    string AssetDescriptionPath = "",
    IReadOnlyList<string>? LocalCertificateFingerprints = null);
