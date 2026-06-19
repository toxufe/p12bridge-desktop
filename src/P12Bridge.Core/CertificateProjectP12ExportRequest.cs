namespace P12Bridge.Core;

public sealed record CertificateProjectP12ExportRequest(
    string ProjectDirectory,
    string CertificatePath,
    string Password);
