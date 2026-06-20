namespace P12Bridge.Core;

public sealed record IpaMetadata(
    long FileSizeBytes,
    string AppBundlePath,
    string BundleIdentifier,
    string ShortVersion,
    string BuildVersion,
    string DisplayName,
    bool HasEmbeddedProvisioningProfile,
    ProvisioningProfile? EmbeddedProvisioningProfile,
    IpaSignaturePresence SignaturePresence);
