namespace P12Bridge.Core;

public sealed record CertificateProjectArtifactPaths(
    string ProjectDirectory,
    string PrivateKeyPath,
    string CertificateSigningRequestPath,
    string MetadataPath);
