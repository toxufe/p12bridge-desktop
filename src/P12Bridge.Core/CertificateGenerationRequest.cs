namespace P12Bridge.Core;

public sealed record CertificateGenerationRequest(
    CertificateSubject Subject,
    byte[] PrivateKeyPkcs8);
