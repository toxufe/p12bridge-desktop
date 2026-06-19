namespace P12Bridge.Core;

public interface ILocalCertificateService
{
    PrivateKeyGenerationResult GeneratePrivateKey(int keySizeBits = 2048);

    CertificateGenerationResult GenerateCertificateSigningRequest(CertificateGenerationRequest request);

    P12ExportResult ExportPkcs12(P12ExportRequest request);
}
