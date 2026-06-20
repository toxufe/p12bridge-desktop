namespace P12Bridge.Core;

public sealed record LocalAssetItem(
    LocalAssetType Type,
    string Name,
    string Path,
    DateTimeOffset ModifiedAt,
    string Note = "",
    CertificateProjectArtifactStatus? CertificateArtifacts = null,
    DateTimeOffset? ExpiresAt = null,
    string SafeMetadataSummary = "");
