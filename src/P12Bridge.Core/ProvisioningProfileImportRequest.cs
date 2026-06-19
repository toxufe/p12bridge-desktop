namespace P12Bridge.Core;

public sealed record ProvisioningProfileImportRequest(
    string ProfilePath,
    string BaseDirectory);
