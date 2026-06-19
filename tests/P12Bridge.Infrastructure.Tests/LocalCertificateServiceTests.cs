using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class LocalCertificateServiceTests
{
    private readonly LocalCertificateService service = new();

    [Fact]
    public void GeneratePrivateKeyCreatesPkcs8Key()
    {
        var result = service.GeneratePrivateKey();

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.PrivateKeyPkcs8);

        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(result.PrivateKeyPkcs8, out _);
        Assert.True(rsa.KeySize >= 2048);
    }

    [Fact]
    public void GeneratePrivateKeyRejectsSmallKeys()
    {
        var result = service.GeneratePrivateKey(1024);

        var issue = Assert.Single(result.Issues);
        Assert.False(result.IsSuccess);
        Assert.Equal(CertificateProofErrorCodes.InvalidKeySize, issue.Code);
    }

    [Fact]
    public void GenerateCertificateSigningRequestCreatesDerCsr()
    {
        var privateKey = service.GeneratePrivateKey().PrivateKeyPkcs8;
        var request = new CertificateGenerationRequest(TestSubject(), privateKey);

        var result = service.GenerateCertificateSigningRequest(request);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.CertificateSigningRequestDer);
        Assert.Equal(0x30, result.CertificateSigningRequestDer[0]);
    }

    [Fact]
    public void GenerateCertificateSigningRequestRejectsMissingPrivateKey()
    {
        var request = new CertificateGenerationRequest(TestSubject(), Array.Empty<byte>());

        var result = service.GenerateCertificateSigningRequest(request);

        var issue = Assert.Single(result.Issues);
        Assert.False(result.IsSuccess);
        Assert.Equal(CertificateProofErrorCodes.MissingPrivateKey, issue.Code);
    }

    [Fact]
    public void ExportPkcs12CreatesPackageWithPrivateKey()
    {
        var privateKey = service.GeneratePrivateKey().PrivateKeyPkcs8;
        var certificateDer = CreateSelfSignedCertificateDer(privateKey);
        const string password = "test-password";

        var result = service.ExportPkcs12(new P12ExportRequest(certificateDer, privateKey, password));

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Pkcs12Bytes);

        using var certificate = new X509Certificate2(result.Pkcs12Bytes, password, X509KeyStorageFlags.Exportable);
        Assert.True(certificate.HasPrivateKey);
    }

    [Fact]
    public void ExportPkcs12RejectsInvalidCertificate()
    {
        var privateKey = service.GeneratePrivateKey().PrivateKeyPkcs8;

        var result = service.ExportPkcs12(new P12ExportRequest([1, 2, 3], privateKey, "password"));

        var issue = Assert.Single(result.Issues);
        Assert.False(result.IsSuccess);
        Assert.Equal(CertificateProofErrorCodes.P12ExportFailed, issue.Code);
    }

    [Fact]
    public void ExportPkcs12RejectsMissingPrivateKey()
    {
        var privateKey = service.GeneratePrivateKey().PrivateKeyPkcs8;
        var certificateDer = CreateSelfSignedCertificateDer(privateKey);

        var result = service.ExportPkcs12(new P12ExportRequest(certificateDer, Array.Empty<byte>(), "password"));

        var issue = Assert.Single(result.Issues);
        Assert.False(result.IsSuccess);
        Assert.Equal(CertificateProofErrorCodes.MissingPrivateKey, issue.Code);
    }

    [Fact]
    public void ExportPkcs12RejectsEmptyPassword()
    {
        var privateKey = service.GeneratePrivateKey().PrivateKeyPkcs8;
        var certificateDer = CreateSelfSignedCertificateDer(privateKey);

        var result = service.ExportPkcs12(new P12ExportRequest(certificateDer, privateKey, " "));

        var issue = Assert.Single(result.Issues);
        Assert.False(result.IsSuccess);
        Assert.Equal(CertificateProofErrorCodes.EmptyP12Password, issue.Code);
    }

    private static CertificateSubject TestSubject() =>
        new(
            "P12Bridge Test",
            EmailAddress: "developer@example.com",
            Organization: "P12Bridge",
            CountryCode: "CN");

    private static byte[] CreateSelfSignedCertificateDer(byte[] privateKeyPkcs8)
    {
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);

        var request = new CertificateRequest(
            new X500DistinguishedName(TestSubject().ToDistinguishedName()),
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));

        return certificate.Export(X509ContentType.Cert);
    }
}
