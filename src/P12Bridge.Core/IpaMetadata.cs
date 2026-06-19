namespace P12Bridge.Core;

public sealed record IpaMetadata(
    long FileSizeBytes,
    string AppBundlePath,
    string BundleIdentifier,
    string ShortVersion,
    string BuildVersion,
    bool HasEmbeddedProvisioningProfile,
    ProvisioningProfile? EmbeddedProvisioningProfile,
    IpaSignaturePresence SignaturePresence);
