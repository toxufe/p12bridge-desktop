namespace P12Bridge.Core;

public sealed record CertificateProjectBackupRequest(
    string ProjectDirectory,
    string OutputDirectory);
