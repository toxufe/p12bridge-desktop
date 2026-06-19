using System.Text.Json;
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
            temporaryDirectory);

        var result = service.Create(request);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Project);
        Assert.NotNull(result.Artifacts);
        Assert.Equal(SigningPurpose.Distribution, result.Project.Purpose);
        Assert.Equal(Path.Combine(temporaryDirectory, "Demo-App-20260620083000"), result.Artifacts.ProjectDirectory);
        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", File.ReadAllText(result.Artifacts.PrivateKeyPath), StringComparison.Ordinal);
        Assert.StartsWith("-----BEGIN CERTIFICATE REQUEST-----", File.ReadAllText(result.Artifacts.CertificateSigningRequestPath), StringComparison.Ordinal);

        using var metadata = JsonDocument.Parse(File.ReadAllText(result.Artifacts.MetadataPath));
        var root = metadata.RootElement;

        Assert.Equal("Demo App", root.GetProperty("Name").GetString());
        Assert.Equal("Distribution", root.GetProperty("Purpose").GetString());
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

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
