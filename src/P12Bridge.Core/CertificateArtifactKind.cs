namespace P12Bridge.Core;

public enum CertificateArtifactKind
{
    PrivateKeyPkcs8 = 0,
    CertificateSigningRequestDer = 1,
    CertificateDer = 2,
    Pkcs12 = 3
}
