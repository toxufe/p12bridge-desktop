using System.Text.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class CertificateProjectServiceTests : IDisposable
{
    private readonly string temporaryDirectory;
    private readonly CertificateProjectService service;

    public CertificateProjectServiceTests()
    {
        temporaryDirectory = Path.Combine(Path.GetTempPath(), "P12BridgeTests", Guid.NewGuid().ToString("N"));
        service = new CertificateProjectService(
            new LocalCertificateService(),
            new FixedClock(DateTimeOffset.Parse("2026-06-20T08:30:00Z")));
    }

    [Fact]
    public void CreateWritesPrivateKeyCsrAndMetadata()
    {
        var request = new CertificateProjectCreateRequest(
            "Demo App",
            SigningPurpose.Distribution,
            new CertificateSubject(
                "Developer Name",
                EmailAddress: "developer@example.com",
                Organization: "P12Bridge",
                CountryCode: "CN"),
            temporaryDirectory,
            "  发布证书  ");

        var result = service.Create(request);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Project);
        Assert.NotNull(result.Artifacts);
        Assert.Equal(SigningPurpose.Distribution, result.Project.Purpose);
        Assert.Equal("发布证书", result.Project.Note);
        Assert.Equal(Path.Combine(temporaryDirectory, "Demo-App-20260620083000"), result.Artifacts.ProjectDirectory);
        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", File.ReadAllText(result.Artifacts.PrivateKeyPath), StringComparison.Ordinal);
        Assert.StartsWith("-----BEGIN CERTIFICATE REQUEST-----", File.ReadAllText(result.Artifacts.CertificateSigningRequestPath), StringComparison.Ordinal);

        using var metadata = JsonDocument.Parse(File.ReadAllText(result.Artifacts.MetadataPath));
        var root = metadata.RootElement;

        Assert.Equal("Demo App", root.GetProperty("Name").GetString());
        Assert.Equal("Distribution", root.GetProperty("Purpose").GetString());
        Assert.Equal("发布证书", root.GetProperty("Note").GetString());
        Assert.Equal("Developer Name", root.GetProperty("Subject").GetProperty("CommonName").GetString());
        Assert.Equal("private.key", root.GetProperty("Artifacts").GetProperty("PrivateKey").GetString());
        Assert.Equal("request.csr", root.GetProperty("Artifacts").GetProperty("CertificateSigningRequest").GetString());
    }

    [Fact]
    public void CreateRejectsMissingProjectName()
    {
        var request = ValidRequest() with { ProjectName = " " };

        var result = service.Create(request);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == CertificateProofErrorCodes.EmptyProjectName);
    }

    [Fact]
    public void CreateRejectsMissingBaseDirectory()
    {
        var request = ValidRequest() with { BaseDirectory = " " };

        var result = service.Create(request);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == CertificateProofErrorCodes.MissingProjectDirectory);
    }

    [Fact]
    public void CreateRejectsInvalidSubject()
    {
        var request = ValidRequest() with
        {
            Subject = new CertificateSubject("Developer Name", CountryCode: "CHN")
        };

        var result = service.Create(request);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == CertificateProofErrorCodes.InvalidCountryCode);
    }

    [Fact]
    public void ExportP12WritesCertificateAndP12NamedFromCsr()
    {
        var createResult = service.Create(ValidRequest());
        Assert.True(createResult.IsSuccess);
        Assert.NotNull(createResult.Artifacts);

        var certificatePath = Path.Combine(temporaryDirectory, "issued.cer");
        var certificateDer = CreateCertificateDerForProjectKey(createResult.Artifacts.PrivateKeyPath);
        File.WriteAllBytes(certificatePath, certificateDer);

        var result = service.ExportP12(new CertificateProjectP12ExportRequest(
            createResult.Artifacts.ProjectDirectory,
            certificatePath,
            "p12-password"));

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.Combine(createResult.Artifacts.ProjectDirectory, "certificate.cer"), result.CertificatePath);
        Assert.Equal(Path.Combine(createResult.Artifacts.ProjectDirectory, "request.p12"), result.P12Path);

        Assert.True(File.Exists(result.CertificatePath));
        Assert.True(File.Exists(result.P12Path));

        using var certificate = new X509Certificate2(result.P12Path, "p12-password", X509KeyStorageFlags.Exportable);
        Assert.True(certificate.HasPrivateKey);

        using var metadata = JsonDocument.Parse(File.ReadAllText(result.MetadataPath));
        Assert.Equal("certificate.cer", metadata.RootElement.GetProperty("Certificate").GetString());
        Assert.Equal("request.p12", metadata.RootElement.GetProperty("P12").GetString());
    }

    [Fact]
    public void ExportP12RejectsMissingProject()
    {
        var result = service.ExportP12(new CertificateProjectP12ExportRequest(
            Path.Combine(temporaryDirectory, "missing"),
            Path.Combine(temporaryDirectory, "issued.cer"),
            "p12-password"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == CertificateProofErrorCodes.ProjectNotFound);
    }

    [Fact]
    public void ExportP12RejectsMissingCertificate()
    {
        var createResult = service.Create(ValidRequest());
        Assert.True(createResult.IsSuccess);
        Assert.NotNull(createResult.Artifacts);

        var result = service.ExportP12(new CertificateProjectP12ExportRequest(
            createResult.Artifacts.ProjectDirectory,
            Path.Combine(temporaryDirectory, "missing.cer"),
            "p12-password"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == CertificateProofErrorCodes.MissingCertificate);
    }

    [Fact]
    public void ExportP12RejectsEmptyPassword()
    {
        var createResult = service.Create(ValidRequest());
        Assert.True(createResult.IsSuccess);
        Assert.NotNull(createResult.Artifacts);

        var certificatePath = Path.Combine(temporaryDirectory, "issued.cer");
        File.WriteAllBytes(certificatePath, CreateCertificateDerForProjectKey(createResult.Artifacts.PrivateKeyPath));

        var result = service.ExportP12(new CertificateProjectP12ExportRequest(
            createResult.Artifacts.ProjectDirectory,
            certificatePath,
            " "));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == CertificateProofErrorCodes.EmptyP12Password);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private CertificateProjectCreateRequest ValidRequest() =>
        new(
            "Demo App",
            SigningPurpose.Development,
            new CertificateSubject("Developer Name", CountryCode: "CN"),
            temporaryDirectory);

    private static byte[] CreateCertificateDerForProjectKey(string privateKeyPath)
    {
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(ReadPem(privateKeyPath, "PRIVATE KEY"), out _);

        var request = new CertificateRequest(
            "CN=Developer Name",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        return certificate.Export(X509ContentType.Cert);
    }

    private static byte[] ReadPem(string path, string label)
    {
        var text = File.ReadAllText(path);
        var beginMarker = $"-----BEGIN {label}-----";
        var endMarker = $"-----END {label}-----";
        var begin = text.IndexOf(beginMarker, StringComparison.Ordinal) + beginMarker.Length;
        var end = text.IndexOf(endMarker, begin, StringComparison.Ordinal);
        var base64 = text[begin..end]
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();

        return Convert.FromBase64String(base64);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
