namespace P12Bridge.Core;

public sealed record IpaSignaturePresence(
    bool HasCodeResources,
    bool HasEmbeddedProvisioningProfile);
