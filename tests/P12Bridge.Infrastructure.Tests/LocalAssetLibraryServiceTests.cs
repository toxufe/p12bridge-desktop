using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class LocalAssetLibraryServiceTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), $"p12bridge-assets-{Guid.NewGuid():N}");
    private readonly string certificateDirectory;
    private readonly string profileDirectory;
    private readonly string ipaDirectory;

    public LocalAssetLibraryServiceTests()
    {
        certificateDirectory = Path.Combine(tempDirectory, "Certificates");
        profileDirectory = Path.Combine(tempDirectory, "Profiles");
        ipaDirectory = Path.Combine(tempDirectory, "IPAs");

        Directory.CreateDirectory(certificateDirectory);
        Directory.CreateDirectory(profileDirectory);
        Directory.CreateDirectory(ipaDirectory);
    }

    [Fact]
    public void ScanDiscoversCertificateProjectsProfilesAndIpas()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "Demo");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        File.WriteAllText(Path.Combine(projectDirectory, "private.key"), "sensitive");
        var profilePath = Path.Combine(profileDirectory, "demo.mobileprovision");
        var ipaPath = Path.Combine(ipaDirectory, "demo.ipa");
        File.WriteAllText(profilePath, "profile");
        File.WriteAllText(ipaPath, "ipa");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Items, item =>
            item.Type == LocalAssetType.CertificateProject
            && item.Name == "Demo"
            && item.Path == projectDirectory);
        Assert.Contains(result.Items, item =>
            item.Type == LocalAssetType.ProvisioningProfile
            && item.Name == "demo.mobileprovision"
            && item.Path == profilePath);
        Assert.Contains(result.Items, item =>
            item.Type == LocalAssetType.Ipa
            && item.Name == "demo.ipa"
            && item.Path == ipaPath);
    }

    [Fact]
    public void ScanTreatsMissingDirectoriesAsEmpty()
    {
        var service = new LocalAssetLibraryService();

        var result = service.Scan(new LocalAssetLibraryRequest(
            Path.Combine(tempDirectory, "MissingCerts"),
            Path.Combine(tempDirectory, "MissingProfiles"),
            Path.Combine(tempDirectory, "MissingIpas")));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void ScanReadsCertificateProjectNoteFromMetadata()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "Demo");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), """
            {
              "Note": "  发布证书  "
            }
            """);
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.Equal("发布证书", item.Note);
    }

    [Fact]
    public void ScanReportsEmptyCertificateProjectArtifactStatus()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "Empty");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.NotNull(item.CertificateArtifacts);
        Assert.False(item.CertificateArtifacts.HasAny);
    }

    [Fact]
    public void ScanReportsPrivateKeyAndCsrArtifactStatus()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "CsrReady");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        File.WriteAllText(Path.Combine(projectDirectory, "private.key"), "PRIVATE KEY CONTENT");
        File.WriteAllText(Path.Combine(projectDirectory, "request.csr"), "CSR CONTENT");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.NotNull(item.CertificateArtifacts);
        Assert.True(item.CertificateArtifacts.HasPrivateKey);
        Assert.True(item.CertificateArtifacts.HasCertificateSigningRequest);
        Assert.False(item.CertificateArtifacts.HasCertificate);
        Assert.False(item.CertificateArtifacts.HasP12);
    }

    [Fact]
    public void ScanReportsCertificateAndP12ArtifactStatus()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "Exported");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        File.WriteAllText(Path.Combine(projectDirectory, "certificate.cer"), "CER CONTENT");
        File.WriteAllText(Path.Combine(projectDirectory, "export.p12"), "P12 CONTENT");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.NotNull(item.CertificateArtifacts);
        Assert.False(item.CertificateArtifacts.HasPrivateKey);
        Assert.False(item.CertificateArtifacts.HasCertificateSigningRequest);
        Assert.True(item.CertificateArtifacts.HasCertificate);
        Assert.True(item.CertificateArtifacts.HasP12);
    }

    [Fact]
    public void ScanReportsCertificateExpiration()
    {
        var expiresAt = new DateTimeOffset(2027, 6, 20, 0, 0, 0, TimeSpan.Zero);
        var projectDirectory = Path.Combine(certificateDirectory, "Expiring");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        File.WriteAllBytes(Path.Combine(projectDirectory, "certificate.cer"), CreateCertificate(expiresAt));
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.NotNull(item.ExpiresAt);
        Assert.Equal(expiresAt.UtcDateTime.Date, item.ExpiresAt.Value.UtcDateTime.Date);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ScanKeepsCertificateProjectWithoutExpirationWhenCertificateIsMissing()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "NoCertificate");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.Null(item.ExpiresAt);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ScanKeepsCertificateProjectAndWarnsWhenCertificateIsInvalid()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "InvalidCertificate");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        File.WriteAllText(Path.Combine(projectDirectory, "certificate.cer"), "not a certificate");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.Null(item.ExpiresAt);
        Assert.Contains(result.Issues, issue => issue.Code == LocalAssetLibraryErrorCodes.ScanFailed);
    }

    [Fact]
    public void ScanKeepsCertificateProjectWhenMetadataNoteIsMalformed()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "Broken");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.Equal("Broken", item.Name);
        Assert.Equal(string.Empty, item.Note);
    }

    [Fact]
    public void ScanDoesNotReadPrivateKeyContents()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "Secret");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        File.WriteAllText(Path.Combine(projectDirectory, "private.key"), "PRIVATE KEY CONTENT");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        Assert.DoesNotContain(result.Items, item => item.Name.Contains("PRIVATE", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Items, item => item.Note.Contains("PRIVATE", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private LocalAssetLibraryRequest ValidRequest() =>
        new(certificateDirectory, profileDirectory, ipaDirectory);

    private static byte[] CreateCertificate(DateTimeOffset expiresAt)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Demo",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(expiresAt.AddDays(-365), expiresAt);
        return certificate.Export(X509ContentType.Cert);
    }
}
