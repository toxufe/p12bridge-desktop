namespace P12Bridge.Core;

public sealed record P12ExportRequest(
    byte[] CertificateDer,
    byte[] PrivateKeyPkcs8,
    string Password);
