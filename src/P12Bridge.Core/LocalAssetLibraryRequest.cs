namespace P12Bridge.Core;

public sealed record LocalAssetLibraryRequest(
    string CertificateDirectory,
    string ProfileDirectory,
    string IpaDirectory);
