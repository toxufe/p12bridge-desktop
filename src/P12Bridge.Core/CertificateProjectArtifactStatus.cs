namespace P12Bridge.Core;

public sealed record CertificateProjectArtifactStatus(
    bool HasPrivateKey = false,
    bool HasCertificateSigningRequest = false,
    bool HasCertificate = false,
    bool HasP12 = false)
{
    public bool HasAny =>
        HasPrivateKey || HasCertificateSigningRequest || HasCertificate || HasP12;
}
